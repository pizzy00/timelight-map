// TimeLight Map: an in-game 3D picture-in-picture map for MeitouClient.
// Copyright (C) 2026 TimeLight Map contributors
// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Reflection;
using System.Text;
using Meitou.Game;
using Meitou.Game.Rendering.Frame;
using Meitou.Game.Rendering.Gpu;
using Meitou.Game.Rendering.Post;
using Meitou.Game.Rendering.Profiling;
using Meitou.Game.Rendering.Shadows;
using Meitou.Game.Rendering.Sky;
using Meitou.Game.Rendering.Terrain;

namespace TimeLightMap
{
    /// <summary>
    /// TOPCAM: a live 3D view of the world from above drawn inside the game window (picture in picture): the map.
    ///
    /// How: the same way the game draws its water reflection (Rendering/Frame/ReflectionPass.cs, WorldFrame.DrawReflectedScene),
    /// a second scene pass through the public GPU seam:
    ///   IGpuContext.BeginNative, IGpuCommandList.BeginRendering on own colour + depth textures, BeginHostPassParallel,
    ///   TerrainRenderer.Draw(vp, eye, frustum, options, light, cullBelow, secondary: true)   (its spare quadtree)
    ///   WorldObjectRenderer.Draw, FoliageRenderer.Draw, CharacterRenderer.Draw                 (all take a view-projection)
    ///   EndHostPassParallel, EndRendering, EndHostPass, EndNative
    /// with an orthographic camera looking at the view's centre: straight down at pitch 90, tilted back below that (MapView).
    /// While it draws, the shadow receiver block is replaced by zeros unless 'shadows on' (the reflection's own "no shadows"
    /// switch), and the atmosphere's haze and weather fog are switched off at their source (see DrawTop). No water, sky or
    /// particles.
    /// Then, every frame, one full-screen triangle (LegacyProgram on PostProcessShaders.Vertex) copies that picture into the
    /// post chain's finished world picture (IWorldPostChain.Target) through a viewport on the rect, with the game's own
    /// exposure (PostOptions.Exposure x 0.55 / the adapted luminance texture, as the composite does). It lands under the HUD.
    /// With marks on or the map faded ('alpha' below 1) a second program does the copy: the same picture, the marks' shapes
    /// drawn over it in the fragment shader (Marks), alpha blended. A failure of that program leaves the plain copy.
    ///
    /// The view: 'topcam view' sets one at once (a cut); 'topcam follow squad' has the mod keep the view on the squad,
    /// which it eases towards every frame (ViewTarget) and redraws every frame it moves.
    ///
    /// Hooks: a prefix on TerrainRenderer.Draw keeps the main view's light and options of the frame; a postfix on
    /// GameHost.DrawFrame (after WorldFrame.Draw returned and the ahead tick ended) draws. All of it, the commands and the HUD
    /// patches (MapHud) run on the game's main thread.
    /// GameHost, WorldGpu and CharacterRenderer are internal: the mod's assembly is named Meitou.Tests, which MeitouClient
    /// names in InternalsVisibleTo, so the compiler and the runtime both let it in.
    ///
    /// Orientation (yaw 0): north up. Kenshi's world has +X east and +Z south, so picture right = +X, picture up = -Z.
    /// yaw turns the map like a compass heading: yaw 90 puts east (+X) at the top.
    /// Targets are bottom-up (row 0 = the picture's bottom, as GL), so window y maps to target row height - (y + h).
    /// </summary>
    internal static class TopCam
    {
        public const string Version = "1.0.0";

        // ---- settings (set by commands, read by the render hook; both on the main thread) ----
        // The values here are the state before the options run (MapPrefs.Commands, at the first drawn frame): off, top right.
        public static bool On;
        public static bool Follow = true;           // centre on the main camera's target until 'topcam view' fixes one
        public static float Cx, Cz, Span = 1500f;   // world units (10 = 1 m); Span = world width across the rect
        public static int RectX = 24, RectY = 24, RectW = 480, RectH = 336;   // window pixels, origin top left; used while Corner is 0
        public static readonly AngleEase YawAngle = new AngleEase(0, true);       // degrees; both glide to a new value (AngleEase)
        public static readonly AngleEase PitchAngle = new AngleEase(90, false);   // 90: straight down; down to 35: tilted back
        public static float Fps = 30f;
        public static float Scale = 1f;             // render size = rect size x Scale
        public static float ManualExposure = -1f;   // < 0: the game's exposure (auto)
        public static bool FlipX, FlipY;
        public static bool Shadows;                 // false: the receiver block zeroed, as the reflection
        public static bool NoFog = true;            // WorldLighting.FogDistance pushed away
        public static bool NoHaze = true;           // the atmosphere's haze and weather fog off in this pass
        public static bool Wide = true;             // the wide-span distances (objects, material, characters)
        public static string Layers = "tofc";       // t terrain, o objects (buildings), f foliage, c characters
        public static readonly ViewTarget Target = new ViewTarget();
        public static string TargetError, FxError;

        public const float MinPitch = 35f;
        public const float ObjectEyeHeight = 5200f;     // objects are measured from no higher than this (see DrawTop)
        public const float CharacterSpan = 9000f;       // wider than this a man is under a pixel: the character layer is left out

        // ---- state ----
        static GpuTexture _colour, _depth;
        static int _width, _height;
        static LegacyProgram _blit, _full;
        static SamplerSlot _sTop, _sAdapted, _fsTop, _fsAdapted;
        static UniformHandle _uExposure, _uAuto, _uFlip, _fuExposure, _fuAuto, _fuFlip, _fuMarkRect, _fuPal, _fuShape;
        static int _palVersion = -1;

        // The simple options (MapPrefs): the map kept in a corner of the window whatever the window's size, and the config file
        public static int Corner = MapPrefs.DefaultCorner;          // 0: the rect as 'topcam rect' set it; 1..4: a corner (MapPrefs.Corner)
        public static double CornerSize = MapPrefs.Medium;          // the map's height, percent of the window's
        public static int CornerMargin = MapPrefs.DefaultMargin;
        static List<string[]> _prefs;                               // the options as commands, run at the first drawn frame
        static string _prefsFrom = "the built-in defaults";
        static bool _holdFirstView;                                 // 'follow off' before anything was drawn: stay where the first frame looks
        static bool _fullBroken;
        static GpuSampler _linear;
        static bool _linearMade;
        static FrameBlock _unshadowed;
        static readonly WorldRenderOptions Options = new WorldRenderOptions();
        static readonly Stopwatch Clock = Stopwatch.StartNew();
        static readonly Func<float, float, float> FlatGround = (x, z) => 0f;
        static readonly BlendState Fade = new BlendState(true, BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha,
            AlphaSrc: BlendingFactor.Zero, AlphaDst: BlendingFactor.One);      // the picture's own alpha is left as it was
        static double _nextDraw, _lastStep = -1;
        static WorldLighting _light;
        static WorldRenderOptions _mainOptions;
        static long _lightFrame = -1, _frame;
        static FieldInfo _adaptedField;
        static bool _adaptedTried;
        static MapView _drawn;                      // the view of the picture in the target
        static bool _moving, _wasHidden;
        static bool _angleGlide;                    // an angle moved in this frame's step
        static double _drawnYaw, _drawnPitch = 90;  // the angles of the picture in the target
        static long _shownStamp;                    // when the map was last put on screen (Stopwatch ticks)
        static float _shownX, _shownY, _shownW, _shownH;
        static float _alphaGoal = 1f, _alphaFrom = 1f, _alphaNow = 1f;
        static double _alphaStart, _alphaOver;

        public static double LastDrawMs, LastBlitMs;
        public static long Draws;
        public static string Error;
        public static string LastView = "";
        public static GameHost Host;

        // Harmony prefix on TerrainRenderer.Draw: the main view's call (not the reflection's, not ours) carries the frame's light.
        public static void TerrainDrawPrefix(WorldRenderOptions options, WorldLighting light, bool secondary)
        {
            if (!On || secondary) return;
            _light = light;
            _mainOptions = options;
            _lightFrame = _frame;
        }

        // Harmony postfix on GameHost.DrawFrame(int width, int height).
        public static void DrawFramePostfix(GameHost __instance, int width, int height)
        {
            Host = __instance;
            _frame++;
            if (_prefs != null) RunPrefs();
            if (!On)
            {
                if (_colour != null) FreeTargets();
                return;
            }
            try
            {
                Frame(__instance, width, height);
            }
            catch (Exception e)
            {
                On = false;
                Error = e.GetType().Name + ": " + e.Message;
                Log.Error("topcam switched off after an error: " + e);
            }
        }

        /// <summary>The map's options (MapPrefs: map, corner, size, margin, zoom, follow, pitch, alpha, marks, cone, places):
        /// the built-in defaults, with the values of &lt;mod folder&gt;/timelight-map.cfg over them when that file is there.
        /// With no file the map is simply on, in the top right corner, following the squad. The options are applied as
        /// topcam commands at the first drawn frame (the commands need the running game).</summary>
        public static void LoadPrefs(string folder)
        {
            string text = null;
            try
            {
                if (!string.IsNullOrEmpty(folder))
                {
                    string path = System.IO.Path.Combine(folder, MapPrefs.FileName);
                    if (System.IO.File.Exists(path))
                    {
                        text = System.IO.File.ReadAllText(path);
                        _prefsFrom = MapPrefs.FileName;
                    }
                }
            }
            catch (Exception e) { Log.Error(MapPrefs.FileName + " not read, the defaults are used: " + e.Message); }
            var list = MapPrefs.Commands(text, out var faults);
            foreach (string f in faults) Log.Error(MapPrefs.FileName + ": " + f);
            _prefs = list;
        }

        static void RunPrefs()
        {
            var list = _prefs;
            _prefs = null;
            int ok = 0;
            foreach (var words in list)
            {
                try
                {
                    var r = Command(words);
                    if (r.Ok) ok++;
                    else Log.Error(MapPrefs.FileName + ": " + string.Join(" ", words) + ": " + r.Text);
                }
                catch (Exception e) { Log.Error(MapPrefs.FileName + ": " + string.Join(" ", words) + ": " + e.Message); }
            }
            Log.Info("map options from " + _prefsFrom + ": " + ok + " of " + list.Count + " applied; " + Summary());
        }

        /// <summary>Whether the map picture is on screen now, and where (window pixels, origin top left): for the HUD patches,
        /// which run after the blit in the same frame. The stamp also covers frames in which nothing of the world is drawn
        /// (the title, the editor): the map is not there then.</summary>
        public static bool MapShown(out float x, out float y, out float w, out float h)
        {
            (x, y, w, h) = (_shownX, _shownY, _shownW, _shownH);
            return On && _shownStamp != 0 && Stopwatch.GetElapsedTime(_shownStamp).TotalSeconds < 0.15;
        }

        static void Frame(GameHost host, int width, int height)
        {
            var gpu = host.Gpu;
            if (gpu == null || gpu.Terrain == null) return;
            if (Corner != 0) MapPrefs.Rect(Corner, CornerSize, CornerMargin, width, height, out RectX, out RectY, out RectW, out RectH);
            var chain = gpu.PostChain;
            if (chain == null) return;
            var ctx = chain.Context;
            var picture = chain.Target;
            if (ctx == null || picture == null) return;
            if (_lightFrame != _frame - 1 || _mainOptions == null) return;   // no world drawn this frame (menus, loading)
            if (ctx.PassOpen) return;                                        // never inside somebody else's pass

            double now = Clock.Elapsed.TotalSeconds;
            double dt = _lastStep < 0 ? 0 : Math.Clamp(now - _lastStep, 0, 0.25);
            _lastStep = now;
            float alpha = StepAlpha(now);

            int w = Math.Clamp((int)MathF.Round(RectW * Scale), 16, 4096), h = Math.Clamp((int)MathF.Round(RectH * Scale), 16, 4096);
            bool marks = Marks.On && alpha > 0f;
            if (Target.Mode == TargetMode.Follow || marks) ReadPack(host);
            else if (Pack.Count > 0) Pack.Clear();

            // the view of this frame: the eased one is stepped even while the map is faded out, so it comes back in the right place
            var view = NextView(host, gpu, dt, w / (float)h);
            if (alpha <= 0f)
            {
                _wasHidden = true;
                _moving = false;
                _shownStamp = 0;
                return;      // faded out: no top pass, no blit, no labels (MapShown goes false)
            }

            _moving = (Target.Mode != TargetMode.None || _angleGlide) && _drawn.Valid && !view.Near(_drawn, 0.05, RectW);
            if (_colour == null || w != _width || h != _height || now >= _nextDraw || _moving || _wasHidden || !_drawn.Valid)
            {
                _nextDraw = now + 1.0 / Math.Clamp(Fps, 0.2f, 240f);
                long t0 = Stopwatch.GetTimestamp();
                EnsureTargets(ctx, w, h);
                DrawTop(ctx, gpu, host, view);
                _drawn = view;
                (_drawnYaw, _drawnPitch) = (YawAngle.Value, PitchAngle.Value);
                _wasHidden = false;
                LastDrawMs = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
                Draws++;
            }

            // the rect in window pixels, clipped to the output
            int outW = Math.Min(width, picture.Desc.Width), outH = Math.Min(height, picture.Desc.Height);
            int x = Math.Clamp(RectX, 0, Math.Max(outW - 1, 0)), rw = Math.Clamp(RectW, 1, Math.Max(outW - x, 1));
            int top = Math.Clamp(RectY, 0, Math.Max(outH - 1, 0)), rh = Math.Clamp(RectH, 1, Math.Max(outH - top, 1));

            // the marks of this frame, in the view the picture was drawn with (with the main camera of this same frame, for
            // the view cone, and the game clock as this frame draws it, for the heading arrows)
            int shapes = 0;
            if (marks)
            {
                CamPose cam = default;      // neither may cost the other marks: no camera = no cone, no clock = no arrows
                double clock = double.NaN;
                try { cam = ReadCamera(host, width, height); } catch { }
                try { clock = GameSeconds(host); } catch { }
                try
                {
                    Marks.Build(host, gpu.Terrain, _drawn, x, top, rw, rh, alpha, cam, clock);
                    shapes = Marks.ShapeCount;
                }
                catch (Exception e)
                {
                    Marks.Fail("build", e);
                }
            }

            long t1 = Stopwatch.GetTimestamp();
            bool full = (shapes > 0 || alpha < 1f) && !_fullBroken;
            try
            {
                Blit(ctx, gpu, picture, x, outH - (top + rh), rw, rh, full, full ? alpha : 1f, full ? shapes : 0);
            }
            catch (Exception e) when (full)
            {
                // the marks' and the fade's program failed: the picture stays, drawn by the plain copy
                _fullBroken = true;
                FxError = e.GetType().Name + ": " + e.Message;
                Log.Error("topcam: the marks / fade program failed, the plain picture stays: " + e);
                if (Marks.On) Marks.Fail("blit", e);
                Blit(ctx, gpu, picture, x, outH - (top + rh), rw, rh, false, 1f, 0);
            }
            LastBlitMs = Stopwatch.GetElapsedTime(t1).TotalMilliseconds;
            (_shownX, _shownY, _shownW, _shownH) = (x, top, rw, rh);
            _shownStamp = Stopwatch.GetTimestamp();
        }

        static void ReadPack(GameHost host)
        {
            try
            {
                Pack.Read(host);
            }
            catch (Exception e)
            {
                Pack.Clear();
                if (Marks.On) Marks.Fail("pack", e);
                if (Target.Mode == TargetMode.Follow) TargetFail(e);
            }
        }

        /// <summary>The map's strength now: eased (smoothstep) from where it was to the asked value over the asked time.</summary>
        static float StepAlpha(double now)
        {
            if (_alphaNow == _alphaGoal) return _alphaNow;
            double t = _alphaOver > 1e-3 ? (now - _alphaStart) / _alphaOver : 1;
            if (t >= 1) return _alphaNow = _alphaGoal;
            t = Math.Clamp(t, 0, 1);
            t = t * t * (3 - 2 * t);
            return _alphaNow = (float)(_alphaFrom + (_alphaGoal - _alphaFrom) * t);
        }

        static double GameSeconds(GameHost host)
        {
            var session = host.Session;
            return session.Clock.GameSeconds + session.SimulationAlpha * session.Simulation.TickSeconds;
        }

        /// <summary>The main game camera as this frame drew it (GameHost.Camera, a WorldCamera: Eye, Target, Yaw, Pitch,
        /// FieldOfView), and the main view's aspect (the frame's own width / height, as WorldFrame takes it). Not valid without one.</summary>
        static CamPose ReadCamera(GameHost host, int width, int height)
        {
            var cam = host.Camera;
            if (cam == null || width <= 0 || height <= 0) return default;
            var eye = cam.Eye;
            var target = cam.Target;
            return new CamPose
            {
                EyeX = eye.X, EyeY = eye.Y, EyeZ = eye.Z, TargetX = target.X, TargetY = target.Y, TargetZ = target.Z,
                Yaw = cam.Yaw, Pitch = cam.Pitch, Fov = cam.FieldOfView, Aspect = width / (double)height, Valid = true,
            };
        }

        /// <summary>The view to draw this frame: the squad follow's eased view (ViewTarget), else the plain settings (the main
        /// camera's target while 'follow camera', else the fixed centre).</summary>
        static MapView NextView(GameHost host, WorldGpu gpu, double dt, float aspect)
        {
            var v = new MapView { FlipX = FlipX, FlipY = FlipY };
            // the angles glide: the frame that brings one to rest still counts (its picture is the first at the final angle)
            _angleGlide = YawAngle.Moving || PitchAngle.Moving;
            YawAngle.Step(dt, Target.Ease);
            PitchAngle.Step(dt, Target.Ease);
            v.Angles(YawAngle.Value, PitchAngle.Value);
            double cx = Cx, cz = Cz, span = Span;
            bool eased = false;
            if (Target.Mode == TargetMode.Follow)
            {
                try
                {
                    Target.Step(dt, Pack.HasCentre, Pack.CentreX, Pack.CentreZ);
                    (cx, cz, span) = (Target.Cx, Target.Cz, Target.Span);
                    eased = true;
                    if (Target.WaitingForPoint && host.Camera != null)
                    {
                        // nobody to follow yet (no living squad member): look where the main camera looks until there is
                        var look = host.Camera.Target;
                        (cx, cz) = (look.X, look.Z);
                    }
                }
                catch (Exception e)
                {
                    TargetFail(e);
                    (cx, cz, span) = (Cx, Cz, Span);
                }
            }
            if (!eased && (Follow || _holdFirstView) && host.Camera != null)
            {
                var target = host.Camera.Target;
                (cx, cz) = (target.X, target.Z);
                // 'follow off' was asked before anything was drawn: this first view is where the map stays
                if (_holdFirstView) (Cx, Cz, _holdFirstView) = (target.X, target.Z, false);
            }
            span = Math.Clamp(span, 20, 60000);
            float ground = gpu.Terrain.HeightAt((float)cx, (float)cz);
            if (!float.IsFinite(ground)) ground = 0;
            (v.Cx, v.Cz, v.Span, v.SpanZ) = (cx, cz, span, span / aspect);
            v.Cy = eased ? Target.StepHeight(ground, dt) : ground;
            v.Valid = true;
            return v;
        }

        /// <summary>The squad follow ends where the view stands (as if 'topcam view' had been sent with the view on screen).</summary>
        static void TargetOff()
        {
            if (Target.Mode != TargetMode.None && _drawn.Valid) (Cx, Cz, Span, Follow) = ((float)_drawn.Cx, (float)_drawn.Cz, (float)_drawn.Span, false);
            Target.Stop();
            _moving = false;
        }

        static void TargetFail(Exception e)
        {
            TargetOff();
            TargetError = e.GetType().Name + ": " + e.Message;
            Log.Error("topcam: following the squad switched off after an error (the view stays where it was): " + e);
        }

        /// <summary>'follow squad': the view follows the squad at this span (world units across). The eased view begins at the
        /// view on screen (once; a new span keeps gliding). With no picture on screen (the map is off, or nothing has been
        /// drawn yet) there is nothing to glide from: the target is left unstarted and its first step cuts straight to the
        /// squad (ViewTarget.Step).</summary>
        static void FollowSquad(double span)
        {
            TargetError = null;
            bool running = Target.Mode == TargetMode.Follow && Target.Started;
            if (!running && On && _drawn.Valid) Target.Start(_drawn.Cx, _drawn.Cz, _drawn.Span, _drawn.Cy);
            (Target.Mode, Target.FollowSpan) = (TargetMode.Follow, span);
        }

        static void EnsureTargets(IGpuContext ctx, int w, int h)
        {
            if (_colour != null && w == _width && h == _height) return;
            FreeTargets();
            // the reflection's formats without its multisampling: the scene's programs are known to draw into these
            _colour = ctx.CreateTexture(new TextureDesc(InternalFormat.Rgba16F, w, h,
                Use: TextureUse.Sampled | TextureUse.ColourTarget | TextureUse.TransferDst, Name: "topcam colour"));
            _depth = ctx.CreateTexture(new TextureDesc(InternalFormat.DepthComponent24, w, h,
                Use: TextureUse.DepthTarget | TextureUse.Sampled, Name: "topcam depth"));
            (_width, _height) = (w, h);
        }

        static void FreeTargets()
        {
            try { _colour?.Dispose(); } catch { }   // the context releases them after the frames in flight
            try { _depth?.Dispose(); } catch { }
            (_colour, _depth, _width, _height) = (null, null, 0, 0);
            _drawn.Valid = false;
        }

        /// <summary>The scene from above into the mod's own targets.</summary>
        static void DrawTop(IGpuContext ctx, WorldGpu gpu, GameHost host, in MapView v)
        {
            var terrain = gpu.Terrain;
            float span = (float)v.Span, spanZ = (float)v.SpanZ;
            float sinPitch = (float)v.SinPitch, cosPitch = (float)v.CosPitch;
            var centre = new Vector3((float)v.Cx, (float)v.Cy, (float)v.Cz);

            // the picture's up on the ground: north (-Z) turned by yaw towards east (+X); the view direction: straight down at
            // pitch 90, tilted towards that up below it (the camera stands back on the other side)
            var up = new Vector3((float)v.SinYaw, 0f, -(float)v.CosYaw);
            var forward = up * cosPitch - Vector3.UnitY * sinPitch;
            float height = MathF.Max(6000f, span * 2f);   // the camera's distance from the centre; the depth covers twice that
            var view = Matrix4x4.CreateLookAt(centre - forward * height, centre, up);
            var projection = DepthConvention.Apply(Matrix4x4.CreateOrthographic(span, spanZ, 1f, height * 2f));
            var vp = view * projection;
            var frustum = WorldCamera.FrustumPlanes(vp);

            // the point the renderers measure their detail and draw distances from: on the camera's axis, close to the centre
            // (not the far camera)
            float lodHeight = MathF.Max(span * 0.5f, 150f);
            var eye = centre - forward * lodHeight;
            // objects are streamed around the main camera and switch to their distant stand-ins 12000 units from the eye they
            // are drawn for: an eye higher than that would draw none of them, so theirs stays low at wide spans
            var objectEye = Wide ? centre - forward * MathF.Min(lodHeight, ObjectEyeHeight) : eye;
            LastView = string.Create(CultureInfo.InvariantCulture, $"{v.Cx:0.#},{v.Cz:0.#},{v.Span:0.#}");

            var light = NoFog ? _light with { FogDistance = 1e7f } : _light;
            var main = _mainOptions;
            Options.Textures = main.Textures;
            Options.NormalMaps = false;
            Options.Objects = main.Objects;
            Options.Water = main.Water;
            Options.Wireframe = 0;
            Options.Debug = 0;
            Options.TerrainPixelError = main.TerrainPixelError;
            Options.TerrainPixelScale = _height * lodHeight / spanZ;   // pixels per unit at the detail point = height / spanZ
            // the textured ground gives way to the flat far colour between 0.8 and 1 of this distance from the eye: at wide
            // spans the eye is further than the game's 30000 from all of the picture
            Options.MaterialDistance = Wide ? MathF.Max(main.MaterialDistance, span * 1.6f) : main.MaterialDistance;

            bool overridden = false, native = false, rendering = false, hosting = false;
            IGpuCommandList cmd = null;
            var objects = gpu.Objects;
            float objDistance = 0, objLandmarks = 0, objBias = 0;
            bool objAllFloors = false, objSaved = false;
            Func<Vector3, float, bool> objMirror = null;
            var sky = gpu.Sky;
            bool hazeSaved = false, skyMeitouHaze = false, skyKenshiHaze = false;
            float skyStrength = 0, skyClearance = 0;
            (float Weight, Vector3 Colour, float Distance)? skyFog = null;
            try
            {
                if (!Shadows)
                {
                    _unshadowed ??= new FrameBlock(ctx, ShadowPass.ReceiverBytes);
                    ctx.Globals.OverrideBlock(ShadowShaders.ReceiverBlock, _unshadowed.Binding);
                    overridden = true;
                }

                // THE HAZE. Every world shader ends with atmoApply(colour, eye, position) (Rendering/Sky/AtmosphereShaders.cs):
                // the game's haze, a ramp over the distance from the eye between 0.06 and 0.6 of SkyRenderer.HazeDistance (50000)
                // times SkyRenderer.HazeStrength, plus the weather's fog (complete at its fog distance, 25000 in a dust storm),
                // plus, for an eye high above the game's camera heights, the "physical" haze. This pass's eye stands span / 2
                // from the ground, so from a span of about 14000 the picture drowned in it. Their inputs are loose frame-global
                // uniforms (uAtmoHaze, uAtmoFog, uAtmoAltitude) that SkyRenderer computes from its own properties each time a
                // draw prepares its segment (SkyRenderer.Uniforms, read through FrameGlobals at NativeFrame.Prepare /
                // LegacyProgram.ApplyGlobals), so for the length of this pass's draws the properties say "no haze": strength 0,
                // no weather fog, the eye within the game's camera heights. Put back in the finally, before the pass ends
                // (as the reflection does with the shadow block): the main view never sees it.
                if (NoHaze && sky != null)
                {
                    (skyStrength, skyMeitouHaze, skyKenshiHaze, skyFog, skyClearance) =
                        (sky.HazeStrength, sky.MeitouHaze, sky.KenshiHaze, sky.FogInput, sky.EyeClearance);
                    hazeSaved = true;
                    sky.MeitouHaze = true;
                    sky.HazeStrength = 0f;
                    sky.KenshiHaze = true;
                    sky.FogInput = (0f, Vector3.Zero, 0f);
                    sky.SetEye(Vector3.Zero, FlatGround, float.NegativeInfinity);      // EyeClearance 0: no altitude blend
                }

                cmd = ctx.BeginNative("topcam");
                native = true;
                var target = PassTargets.Of(_colour, _depth);
                var fog = light.FogColour;
                cmd.BeginRendering(new RenderingDesc(
                    target.Colour with { Load = LoadOp.Clear, Clear = new ClearColour(fog.X, fog.Y, fog.Z, 1f) },
                    target.Depth with { Load = LoadOp.Clear, DepthClear = DepthConvention.Clear },
                    target.Width, target.Height), ctx.RecordsSecondaries);
                rendering = true;
                ctx.BeginHostPassParallel(cmd, target, DrawState.SceneDepth(target.Formats), 4);   // 4: the reflection's profiler stage
                hosting = true;

                if (Layers.Contains('t'))
                    terrain.Draw(vp, eye, frustum, Options, light, float.NegativeInfinity, true, false);

                if (Layers.Contains('o') && Options.Objects && objects != null)
                {
                    (objDistance, objLandmarks, objBias, objAllFloors, objMirror) =
                        (objects.ObjectDistance, objects.LandmarkDistance, objects.LodBias, objects.AllFloors, objects.MirrorCull);
                    objSaved = true;
                    objects.MirrorCull = null;
                    objects.AllFloors = true;   // whole buildings with their roofs, whatever floor the main view shows
                    objects.Draw(vp, objectEye, frustum, Options, light.SunDirection, light.FogColour, light.FogDistance, terrain);
                }

                if (Layers.Contains('f') && Options.Objects && gpu.Foliage is { } foliage)
                    foliage.Draw(vp, eye, frustum, Options, light.SunDirection, light.FogColour, light.FogDistance, terrain, false,
                        MathF.Max(span, 1500f) + cosPitch * lodHeight);

                if (Layers.Contains('c') && gpu.Characters is { } characters && !(Wide && span > CharacterSpan))
                {
                    var fogCull = characters.FogCull;
                    var interior = characters.InteriorObjects;
                    characters.FogCull = null;
                    characters.InteriorObjects = null;
                    try { characters.Draw(vp, eye, frustum, light.SunDirection, light.FogColour, light.FogDistance, null); }
                    finally { characters.FogCull = fogCull; characters.InteriorObjects = interior; }
                }
            }
            finally
            {
                // put everything back and close what was opened, each step on its own: a failure in here must not leave a pass open
                if (objSaved)
                {
                    try
                    {
                        objects.ObjectDistance = objDistance;
                        objects.LandmarkDistance = objLandmarks;
                        objects.LodBias = objBias;
                        objects.AllFloors = objAllFloors;
                        objects.MirrorCull = objMirror;
                    }
                    catch { }
                }
                if (hazeSaved)
                {
                    try
                    {
                        sky.HazeStrength = skyStrength;
                        sky.MeitouHaze = skyMeitouHaze;
                        sky.KenshiHaze = skyKenshiHaze;
                        sky.FogInput = skyFog;
                        sky.SetEye(new Vector3(0f, skyClearance, 0f), FlatGround, float.NegativeInfinity);      // EyeClearance as it was
                    }
                    catch (Exception e) { Log.Error("topcam: the sky's haze settings were not put back: " + e.Message); }
                }
                if (overridden) { try { ctx.Globals.OverrideBlock(ShadowShaders.ReceiverBlock, null); } catch { } }
                if (hosting) { try { ctx.EndHostPassParallel(cmd); } catch (Exception e) { Log.Error("topcam EndHostPassParallel: " + e.Message); } }
                if (rendering) { try { cmd.EndRendering(); } catch (Exception e) { Log.Error("topcam EndRendering: " + e.Message); } }
                if (hosting) { try { ctx.EndHostPass(cmd); } catch (Exception e) { Log.Error("topcam EndHostPass: " + e.Message); } }
                if (native) { try { ctx.EndNative(cmd); } catch (Exception e) { Log.Error("topcam EndNative: " + e.Message); } }
            }
        }

        const string BlitFragment = """
            #version 330 core
            in vec2 vUv;
            out vec4 fragColour;
            uniform sampler2D uTop;
            uniform sampler2D uAdapted;   // the post chain's adapted luminance (1 x 1, r)
            uniform float uExposure;
            uniform int uAuto;            // 1: the game's exposure, 0.55 over the adapted luminance
            uniform vec4 uFlip;
            void main()
            {
                vec2 uv = vec2(uFlip.x > 0.5 ? 1.0 - vUv.x : vUv.x, uFlip.y > 0.5 ? 1.0 - vUv.y : vUv.y);
                float exposure = uExposure;
                if (uAuto != 0) exposure *= max(0.55 / max(texture(uAdapted, vec2(0.5)).r, 1e-6), 0.001);
                fragColour = vec4(clamp(max(texture(uTop, uv).rgb, 0.0) * exposure, 0.0, 1.0), 1.0);
            }
            """;

        /// <summary>The copy with the marks and the fade. The picture as BlitFragment makes it, then the frame's shapes over it
        /// (Marks.Shapes: two vec4 a shape, in the rect's own pixels with the origin at its top left and y downward), each
        /// with a dark casing and a smooth edge, then the whole at uMarkRect.w (the map's alpha; the pipeline blends).
        /// Kinds: 0 disc, 1 hollow disc, 2 ring, 3 diamond (a = centre, radius, ring width); 4 line, 5 dotted line
        /// (a = both ends; b.y width, b.w casing / the dots' phase); 6 triangle (a = tip, unit direction; b.y length, b.w half
        /// width). b = kind, size, palette slot, parameter.
        /// 7 filled triangle, 8 the same fading away from its first corner (to 0.35 of its strength at the far side): the
        /// corners are a.xy, a.zw and (b.y, b.w), no casing (the view cone's fan). The palette has 32 slots.</summary>
        public const string FullFragment = """
            #version 330 core
            in vec2 vUv;
            out vec4 fragColour;
            uniform sampler2D uTop;
            uniform sampler2D uAdapted;
            uniform float uExposure;
            uniform int uAuto;
            uniform vec4 uFlip;
            uniform vec4 uMarkRect;       // xy: the rect's size in pixels, z: the number of shapes, w: the map's alpha
            uniform vec4 uPal[32];
            uniform vec4 uShape[512];
            void main()
            {
                vec2 uv = vec2(uFlip.x > 0.5 ? 1.0 - vUv.x : vUv.x, uFlip.y > 0.5 ? 1.0 - vUv.y : vUv.y);
                float exposure = uExposure;
                if (uAuto != 0) exposure *= max(0.55 / max(texture(uAdapted, vec2(0.5)).r, 1e-6), 0.001);
                vec3 c = clamp(max(texture(uTop, uv).rgb, 0.0) * exposure, 0.0, 1.0);

                vec2 p = vec2(vUv.x, 1.0 - vUv.y) * uMarkRect.xy;
                int count = int(uMarkRect.z + 0.5);
                for (int i = 0; i < count; i++)
                {
                    vec4 a = uShape[2 * i];
                    vec4 b = uShape[2 * i + 1];
                    int kind = int(b.x + 0.5);
                    float fill = 0.0;
                    float dark = 0.0;
                    if (kind <= 3)
                    {
                        vec2 q = p - a.xy;
                        float reach = a.z + a.w + 3.0;
                        if (abs(q.x) > reach || abs(q.y) > reach) continue;
                        float d = kind == 3 ? (abs(q.x) + abs(q.y) - a.z) * 0.70710678 : length(q) - a.z;
                        if (kind == 2)
                        {
                            float t = abs(d) - a.w * 0.5;
                            fill = 1.0 - smoothstep(-0.5, 0.5, t);
                            dark = 1.0 - smoothstep(0.5, 1.5, t);
                        }
                        else
                        {
                            fill = 1.0 - smoothstep(-0.5, 0.5, d);
                            dark = 1.0 - smoothstep(0.7, 1.7, d);
                            if (kind == 1)
                            {
                                float rim = smoothstep(-a.w - 0.5, -a.w + 0.5, d);
                                fill *= rim;
                                dark *= mix(0.55, 1.0, rim);
                            }
                        }
                    }
                    else if (kind <= 5)
                    {
                        float hw = b.y * 0.5;
                        float reach = hw + 2.5;
                        vec2 lo = min(a.xy, a.zw) - vec2(reach);
                        vec2 hi = max(a.xy, a.zw) + vec2(reach);
                        if (p.x < lo.x || p.y < lo.y || p.x > hi.x || p.y > hi.y) continue;
                        vec2 pa = p - a.xy;
                        vec2 ba = a.zw - a.xy;
                        float len = max(length(ba), 1e-4);
                        vec2 dir = ba / len;
                        float t = clamp(dot(pa, dir), 0.0, len);
                        float off = length(pa - dir * t);
                        float d;
                        if (kind == 4)
                        {
                            d = off - hw;
                            dark = b.w > 0.5 ? 1.0 - smoothstep(0.5, 1.5, d) : 0.0;
                        }
                        else
                        {
                            float period = b.y * 2.4 + 2.0;
                            float s = b.w + (len - t);
                            float m = mod(s + period * 0.5, period) - period * 0.5;
                            d = length(vec2(m, off)) - hw;
                            dark = 1.0 - smoothstep(0.5, 1.5, d);
                        }
                        fill = 1.0 - smoothstep(-0.5, 0.5, d);
                    }
                    else if (kind == 6)
                    {
                        vec2 q = p - a.xy;
                        float reach = b.y + b.w + 3.0;
                        if (abs(q.x) > reach || abs(q.y) > reach) continue;
                        float x = -dot(q, a.zw);
                        float y = abs(dot(q, vec2(-a.w, a.z)));
                        float k = b.w / max(b.y, 1e-3);
                        float d = max(max(-x, x - b.y), (y - x * k) / sqrt(1.0 + k * k));
                        fill = 1.0 - smoothstep(-0.5, 0.5, d);
                        dark = 1.0 - smoothstep(0.7, 1.7, d);
                    }
                    else
                    {
                        vec2 v0 = a.xy;
                        vec2 v1 = a.zw;
                        vec2 v2 = vec2(b.y, b.w);
                        vec2 lo = min(min(v0, v1), v2) - vec2(1.5);
                        vec2 hi = max(max(v0, v1), v2) + vec2(1.5);
                        if (p.x < lo.x || p.y < lo.y || p.x > hi.x || p.y > hi.y) continue;
                        vec2 e0 = v1 - v0;
                        vec2 e1 = v2 - v1;
                        vec2 e2 = v0 - v2;
                        float area = e0.x * (v2.y - v0.y) - e0.y * (v2.x - v0.x);
                        if (abs(area) < 1e-3) continue;
                        float s = area > 0.0 ? 1.0 : -1.0;
                        float l1 = max(length(e1), 1e-4);
                        float d0 = s * (e0.x * (p.y - v0.y) - e0.y * (p.x - v0.x)) / max(length(e0), 1e-4);
                        float d1 = s * (e1.x * (p.y - v1.y) - e1.y * (p.x - v1.x)) / l1;
                        float d2 = s * (e2.x * (p.y - v2.y) - e2.y * (p.x - v2.x)) / max(length(e2), 1e-4);
                        fill = 1.0 - smoothstep(-0.5, 0.5, -min(min(d0, d1), d2));
                        if (kind == 8) fill *= mix(0.35, 1.0, clamp(d1 * l1 / abs(area), 0.0, 1.0));
                    }
                    vec4 pal = uPal[int(b.z + 0.5)];
                    c = mix(c, vec3(0.03, 0.03, 0.035), dark * 0.85 * pal.a);
                    c = mix(c, pal.rgb, fill * pal.a);
                }
                fragColour = vec4(c, uMarkRect.w);
            }
            """;

        /// <summary>The mod's picture into the finished world picture, on the rect (x, y: the picture's own rows, row 0 = the
        /// bottom). <paramref name="full"/>: the marks' program (shapes, alpha), else the plain copy.</summary>
        static void Blit(IGpuContext ctx, WorldGpu gpu, GpuTexture picture, int x, int y, int w, int h, bool full, float alpha, int shapes)
        {
            if (_colour == null) return;
            LegacyProgram program;
            if (full)
            {
                if (_full == null)
                {
                    var made = LegacyProgram.Create(ctx, PostProcessShaders.Vertex, FullFragment, "topcam marks");
                    _fsTop = made.Sampler("uTop");
                    _fsAdapted = made.Sampler("uAdapted");
                    _fuExposure = made.Uniform("uExposure");
                    _fuAuto = made.Uniform("uAuto");
                    _fuFlip = made.Uniform("uFlip");
                    _fuMarkRect = made.Uniform("uMarkRect");
                    _fuPal = made.Uniform("uPal");
                    _fuShape = made.Uniform("uShape");
                    made.Set(_fuPal, Marks.Palette, 4);      // kept: a program's uniform values stay set
                    _palVersion = Marks.PaletteVersion;
                    _full = made;
                }
                program = _full;
            }
            else
            {
                if (_blit == null)
                {
                    _blit = LegacyProgram.Create(ctx, PostProcessShaders.Vertex, BlitFragment, "topcam blit");
                    _sTop = _blit.Sampler("uTop");
                    _sAdapted = _blit.Sampler("uAdapted");
                    _uExposure = _blit.Uniform("uExposure");
                    _uAuto = _blit.Uniform("uAuto");
                    _uFlip = _blit.Uniform("uFlip");
                }
                program = _blit;
            }
            if (!_linearMade)
            {
                _linear = ctx.Samplers.Get(SamplerDesc.FromGl(TextureMinFilter.Linear, TextureMagFilter.Linear, TextureWrapMode.ClampToEdge,
                    TextureWrapMode.ClampToEdge, TextureWrapMode.Repeat, false, DepthFunction.Lequal, false, 1, false, 0));
                _linearMade = true;
            }

            var post = gpu.PostChain as PostProcess ?? gpu.Post;
            GpuTexture adapted = ManualExposure < 0 ? Adapted(post) : null;
            float exposure = ManualExposure >= 0 ? ManualExposure : post?.Options?.Exposure ?? 1f;

            var targets = PassTargets.Of(picture, null).At(new PixelViewport(x, y, w, h, 0, 1), new PixelRect(x, y, (uint)w, (uint)h));
            bool native = false, rendering = false;
            IGpuCommandList cmd = null;
            try
            {
                cmd = ctx.BeginNative(full ? "topcam marks" : "topcam blit");
                native = true;
                cmd.BeginRendering(targets.Rendering);
                rendering = true;
                var state = full && alpha < 1f ? DrawState.For(targets.Formats, ctx.DepthClamp, blend: Fade) : DrawState.For(targets.Formats, ctx.DepthClamp);
                state.Record(cmd, targets);
                var pipeline = state.Pipeline(program.Program, program.VertexLayout(default), PrimitiveType.Triangles, targets.Formats,
                    full ? "topcam marks" : "topcam blit");
                cmd.BindPipeline(ctx.Pipelines.Get(pipeline));
                var flip = new Vector4(FlipX ? 1f : 0f, FlipY ? 1f : 0f, 0f, 0f);
                var top = new SampledTexture(_linear, _colour.View());
                var lum = new SampledTexture(_linear, (adapted ?? _colour).View());
                if (full)
                {
                    program.Set(_fuExposure, exposure);
                    program.Set(_fuAuto, adapted != null ? 1 : 0);
                    program.Set(_fuFlip, flip);
                    program.Set(_fuMarkRect, new Vector4(w, h, shapes, Math.Clamp(alpha, 0f, 1f)));
                    if (shapes > 0) program.Set(_fuShape, new ReadOnlySpan<float>(Marks.Shapes, 0, shapes * 8), 4);
                    if (_palVersion != Marks.PaletteVersion)      // a colour of the palette was changed ('cone fill')
                    {
                        program.Set(_fuPal, Marks.Palette, 4);
                        _palVersion = Marks.PaletteVersion;
                    }
                    program.Bind(_fsTop, top);
                    program.Bind(_fsAdapted, lum);
                }
                else
                {
                    program.Set(_uExposure, exposure);
                    program.Set(_uAuto, adapted != null ? 1 : 0);
                    program.Set(_uFlip, flip);
                    program.Bind(_sTop, top);
                    program.Bind(_sAdapted, lum);
                }
                program.Flush(cmd);
                cmd.Draw(3);
            }
            finally
            {
                if (rendering) { try { cmd.EndRendering(); } catch (Exception e) { Log.Error("topcam blit EndRendering: " + e.Message); } }
                if (native) { try { ctx.EndNative(cmd); } catch (Exception e) { Log.Error("topcam blit EndNative: " + e.Message); } }
            }
        }

        /// <summary>PostProcess._adaptB (private): the 1 x 1 RG32F texture the composite reads its exposure from; null when
        /// there is none (then the plain exposure is used).</summary>
        static GpuTexture Adapted(PostProcess post)
        {
            if (post == null || post.AutoExposure == null) return null;
            if (!_adaptedTried)
            {
                _adaptedTried = true;
                _adaptedField = typeof(PostProcess).GetField("_adaptB", BindingFlags.Instance | BindingFlags.NonPublic);
                if (_adaptedField == null) Log.Error("topcam: no PostProcess._adaptB; exposure is the plain setting (use 'topcam exposure <x>')");
            }
            return (_adaptedField?.GetValue(post) as PostProcess.Target2D)?.Texture;
        }

        // ---- commands ----

        public const string Usage =
            "usage: topcam [on|off | view <cx> <cz> <span> | view follow [span] | rect <x> <y> <w> <h> | yaw <deg> [--over <s>] | pitch <35..90> [--over <s>] | fps <n>"
            + " | scale <x> | exposure auto|<x> | flip none|x|y|xy | layers <tofc> | shadows on|off | fog on|off | haze on|off | wide on|off"
            + " | alpha <0..1> [--over <s>] | ease <s> | marks on|off | hostiles on|off | textscale <x> | clip on|off"
            + " | layer [<" + MapLayers.WordList + "|all> on|off ...] | cone [on|off] [reach <0.5..8>] [fill <0..1>] | places on|off"
            + " | " + MapPrefs.Words + " | status]";

        public static (bool Ok, string Text) Command(IReadOnlyList<string> a)
        {
            var ci = CultureInfo.InvariantCulture;
            bool F(int i, out float v) { v = 0; return i < a.Count && float.TryParse(a[i], NumberStyles.Float, ci, out v) && float.IsFinite(v); }
            bool Is(int i, string word) => i < a.Count && a[i].Equals(word, StringComparison.OrdinalIgnoreCase);
            // an angle's [--over <seconds>]: -1 without it (the spring), else the seconds (0: at once)
            bool Glide(out double over)
            {
                over = -1;
                if (a.Count == 2) return true;
                if (a.Count != 4 || !(Is(2, "--over") || Is(2, "over")) || !F(3, out float seconds) || seconds < 0f || seconds > 600f) return false;
                over = seconds;
                return true;
            }
            if (a.Count == 0) return (true, Status());
            switch (a[0].ToLowerInvariant())
            {
                case "on":
                    Error = null;
                    On = true;
                    _nextDraw = 0;
                    return (true, Status());
                case "off":
                    On = false;
                    return (true, Status());
                case "view":
                    if (a.Count >= 2 && a[1].Equals("follow", StringComparison.OrdinalIgnoreCase))
                    {
                        Target.Stop();      // a view is a cut: it ends the squad follow
                        Follow = true;
                        if (F(2, out float fs) && fs > 0) Span = fs;
                        break;
                    }
                    if (a.Count != 4 || !F(1, out float cx) || !F(2, out float cz) || !F(3, out float span) || span <= 0)
                        return (false, "usage: topcam view <cx> <cz> <span> | topcam view follow [span]   (world units, 10 = 1 m)");
                    Target.Stop();
                    (Cx, Cz, Span, Follow, _holdFirstView) = (cx, cz, span, false, false);
                    break;
                case "ease":
                    if (a.Count != 2 || !F(1, out float ease) || ease < 0 || ease > 10)
                        return (false, "usage: topcam ease <seconds>   (0..10; how far behind the squad the view glides, and how softly yaw and pitch turn; default 0.5)");
                    Target.Ease = ease;
                    break;
                case "rect":
                    if (a.Count != 5 || !F(1, out float rx) || !F(2, out float ry) || !F(3, out float rw) || !F(4, out float rh) || rw < 1 || rh < 1)
                        return (false, "usage: topcam rect <x> <y> <w> <h>   (window pixels, origin top left)");
                    (RectX, RectY, RectW, RectH) = ((int)rx, (int)ry, (int)rw, (int)rh);
                    Corner = 0;      // an explicit rect ends a corner
                    break;
                // ---- the simple options (MapPrefs) ----
                case "corner":
                {
                    if (a.Count < 2 || a.Count > 3 || !MapPrefs.Corner(a[1], out int corner)) return (false, "usage: topcam " + MapPrefs.Words);
                    if (a.Count == 3)
                    {
                        if (!MapPrefs.Size(a[2], out double pct)) return (false, "usage: topcam " + MapPrefs.Words);
                        CornerSize = pct;
                    }
                    Corner = corner;
                    break;
                }
                case "size":
                {
                    if (a.Count != 2 || !MapPrefs.Size(a[1], out double pct)) return (false, "usage: topcam size <small|medium|large|<10..60>>   (the map's height, percent of the window's)");
                    CornerSize = pct;
                    if (Corner == 0) Corner = 2;      // a size needs a corner: top right
                    break;
                }
                case "margin":
                    if (a.Count != 2 || !F(1, out float margin) || margin < 0 || margin > MapPrefs.MaxMargin) return (false, "usage: topcam margin <0..400>   (pixels from the window's edges, for a corner)");
                    CornerMargin = (int)margin;
                    break;
                case "zoom":
                {
                    if (a.Count != 2 || !F(1, out float metres) || metres < MapPrefs.MinZoom || metres > MapPrefs.MaxZoom)
                        return (false, "usage: topcam zoom <10..5000>   (metres across the map)");
                    float units = metres * (float)MapMath.UnitsPerMetre;
                    if (Target.Mode == TargetMode.Follow) Target.FollowSpan = units;
                    else Span = units;
                    break;
                }
                case "follow":
                {
                    string what = a.Count == 2 ? a[1].ToLowerInvariant() : "";
                    if (what == "squad")
                    {
                        _holdFirstView = false;
                        float across = Target.Mode == TargetMode.Follow ? (float)Target.FollowSpan : _drawn.Valid ? (float)_drawn.Span : Span;
                        FollowSquad(MathF.Max(across, 100f));
                    }
                    else if (what == "camera")
                    {
                        _holdFirstView = false;
                        if (Target.Mode != TargetMode.None) TargetOff();
                        Follow = true;
                    }
                    else if (what == "off")
                    {
                        if (Target.Mode != TargetMode.None) TargetOff();
                        else if (Follow && _drawn.Valid) (Cx, Cz) = ((float)_drawn.Cx, (float)_drawn.Cz);
                        Follow = false;
                        _holdFirstView = !_drawn.Valid;      // nothing drawn yet: stay where the first frame looks
                    }
                    else return (false, "usage: topcam follow squad|camera|off   (squad: your people; camera: where the main view looks; off: stays put)");
                    break;
                }
                case "yaw":
                {
                    if (a.Count < 2 || !F(1, out float yaw) || !Glide(out double over))
                        return (false, "usage: topcam yaw <degrees> [--over <seconds>]   (0: north up; 90: east up; glides the short way round: "
                                       + "over the time given, by the view's spring ('topcam ease') without one, at once with --over 0)");
                    YawAngle.Set(yaw, over);
                    break;
                }
                case "pitch":
                {
                    if (a.Count < 2 || !F(1, out float pitch) || pitch < MinPitch || pitch > 90f || !Glide(out double over))
                        return (false, "usage: topcam pitch <35..90> [--over <seconds>]   (90: straight down; lower: tilted back, heights show; "
                                       + "glides over the time given, by the view's spring ('topcam ease') without one, at once with --over 0)");
                    PitchAngle.Set(pitch, over);
                    break;
                }
                case "alpha":
                {
                    float over = 0f;
                    bool ok = a.Count >= 2 && F(1, out float alpha) && alpha >= 0f && alpha <= 1f
                              && (a.Count == 2 || (a.Count == 4 && (Is(2, "--over") || Is(2, "over")) && F(3, out over) && over >= 0f && over <= 600f));
                    if (!ok) return (false, "usage: topcam alpha <0..1> [--over <seconds>]   (the map's strength; 0: not drawn at all)");
                    F(1, out float goal);
                    (_alphaFrom, _alphaGoal, _alphaStart, _alphaOver) = (_alphaNow, goal, Clock.Elapsed.TotalSeconds, over);
                    if (!(over > 1e-3f)) _alphaNow = goal;
                    FxError = null;
                    _fullBroken = false;      // a fade is asked for again: the program gets another try
                    break;
                }
                case "fps":
                    if (a.Count != 2 || !F(1, out float fps) || fps <= 0) return (false, "usage: topcam fps <n>");
                    Fps = fps;
                    break;
                case "scale":
                    if (a.Count != 2 || !F(1, out float scale) || scale < 0.1f || scale > 4f) return (false, "usage: topcam scale <0.1..4>   (render size = rect x scale)");
                    Scale = scale;
                    break;
                case "exposure":
                    if (a.Count == 2 && a[1].Equals("auto", StringComparison.OrdinalIgnoreCase)) ManualExposure = -1f;
                    else if (a.Count == 2 && F(1, out float ex) && ex >= 0) ManualExposure = ex;
                    else return (false, "usage: topcam exposure auto | <x>");
                    break;
                case "flip":
                    if (a.Count != 2) return (false, "usage: topcam flip none|x|y|xy");
                    string f = a[1].ToLowerInvariant();
                    (FlipX, FlipY) = (f.Contains('x'), f.Contains('y'));
                    break;
                case "layers":
                    if (a.Count != 2) return (false, "usage: topcam layers <letters of: t terrain, o objects, f foliage, c characters>   e.g. topcam layers toc");
                    Layers = a[1].ToLowerInvariant();
                    break;
                case "shadows":
                    if (a.Count != 2) return (false, "usage: topcam shadows on|off");
                    Shadows = a[1].Equals("on", StringComparison.OrdinalIgnoreCase);
                    break;
                case "fog":
                    if (a.Count != 2) return (false, "usage: topcam fog on|off");
                    NoFog = !a[1].Equals("on", StringComparison.OrdinalIgnoreCase);
                    break;
                case "haze":
                    if (a.Count != 2) return (false, "usage: topcam haze on|off   (off, the default: no distance haze or weather fog in the map; on: the game's own)");
                    NoHaze = !a[1].Equals("on", StringComparison.OrdinalIgnoreCase);
                    break;
                case "wide":
                    if (a.Count != 2) return (false, "usage: topcam wide on|off   (on, the default: draw distances that suit a zoomed-out map; off: the game's own)");
                    Wide = a[1].Equals("on", StringComparison.OrdinalIgnoreCase);
                    break;
                case "clip":
                    if (a.Count != 2) return (false, "usage: topcam clip on|off   (on, the default: the game's name tags, task bars, item labels, damage numbers and speech bubbles are kept off the map rect)");
                    MapHud.ClipOn = a[1].Equals("on", StringComparison.OrdinalIgnoreCase);
                    if (MapHud.ClipOn) MapHud.ClipError = null;
                    break;
                default:
                    string said = Marks.Command(a);
                    if (said == null) return (false, Usage);
                    if (said.Length > 0) return (false, said);
                    if (Is(0, "marks") && Is(1, "on"))
                    {
                        FxError = null;
                        _fullBroken = false;
                    }
                    break;
            }
            _nextDraw = 0;
            return (true, Status());
        }

        /// <summary>Where the map stands, in one plain line: what a command that changed something answers with.</summary>
        public static string Summary()
        {
            var ci = CultureInfo.InvariantCulture;
            var sb = new StringBuilder(200);
            sb.Append("map ").Append(On ? "on" : "off");
            if (Error != null) sb.Append(" (switched off after an error: ").Append(Error).Append(')');
            string corner = MapPrefs.CornerName(Corner);
            if (corner != null)
                sb.Append(", ").Append(corner).Append(", size ").Append(CornerSize.ToString("0.#", ci)).Append("% of the window's height, margin ")
                  .Append(CornerMargin.ToString(ci));
            else
                sb.Append(", rect ").Append(RectX.ToString(ci)).Append(' ').Append(RectY.ToString(ci)).Append(' ').Append(RectW.ToString(ci))
                  .Append(' ').Append(RectH.ToString(ci));
            bool squad = Target.Mode == TargetMode.Follow;
            double across = (squad ? Target.FollowSpan : Span) / MapMath.UnitsPerMetre;
            sb.Append(", ").Append(MapMath.Num(across)).Append(" m across, ");
            sb.Append(squad ? "follows the squad" : Follow ? "follows the camera" : "stays put");
            if (PitchAngle.Target < 90) sb.Append(", pitch ").Append(MapMath.Num(PitchAngle.Target));
            if (YawAngle.Target != 0) sb.Append(", yaw ").Append(MapMath.Num(YawAngle.Target));
            if (_alphaGoal < 1f) sb.Append(", alpha ").Append(MapMath.Num(_alphaGoal, "0.##"));
            sb.Append(", marks ").Append(Marks.On ? "on" : "off");
            if (Marks.Error != null) sb.Append(" (switched off after an error: ").Append(Marks.Error).Append(')');
            if (Marks.On)
                sb.Append(", cone ").Append(MapLayers.On(Marks.Layers, MapLayers.Cone) ? "on" : "off")
                  .Append(", places ").Append(Marks.AutoPlaces ? "on" : "off");
            if (FxError != null) sb.Append("; the marks' shader failed (").Append(FxError).Append("), the plain picture is shown");
            return sb.ToString();
        }

        public static string Status()
        {
            var ci = CultureInfo.InvariantCulture;
            var sb = new StringBuilder(1024);
            sb.Append("{\"on\":").Append(On ? "true" : "false");
            sb.Append(",\"version\":\"").Append(Version).Append('"');
            sb.Append(",\"corner\":").Append(MapMath.JsonText(MapPrefs.CornerName(Corner)));      // null: the rect is 'topcam rect''s
            sb.Append(",\"cornerSize\":").Append(MapMath.Num(CornerSize, "0.#")).Append(",\"cornerMargin\":").Append(CornerMargin.ToString(ci));
            sb.Append(",\"view\":{\"follow\":").Append(Follow ? "true" : "false");
            sb.Append(",\"cx\":").Append(Cx.ToString("0.#", ci)).Append(",\"cz\":").Append(Cz.ToString("0.#", ci));
            // yaw, pitch: the angles of the last drawn frame (the settings themselves while nothing is drawn)
            bool shown = On && _drawn.Valid;
            sb.Append(",\"span\":").Append(Span.ToString("0.#", ci)).Append(",\"yaw\":").Append(MapMath.Num(shown ? _drawnYaw : YawAngle.Value, "0.##"));
            sb.Append(",\"pitch\":").Append(MapMath.Num(shown ? _drawnPitch : PitchAngle.Value, "0.##"));
            sb.Append(",\"yawTarget\":").Append(MapMath.Num(YawAngle.Target, "0.##")).Append(",\"pitchTarget\":").Append(MapMath.Num(PitchAngle.Target, "0.##"));
            sb.Append(",\"drawn\":\"").Append(LastView).Append("\"}");

            sb.Append(",\"target\":{\"mode\":\"").Append(Target.Mode.ToString().ToLowerInvariant()).Append('"');
            if (Target.Mode == TargetMode.Follow)
                sb.Append(",\"span\":").Append(MapMath.Num(Target.FollowSpan)).Append(",\"pack\":").Append(Pack.Count)
                  .Append(",\"cx\":").Append(MapMath.Num(Target.AimCx)).Append(",\"cz\":").Append(MapMath.Num(Target.AimCz))
                  .Append(",\"aimSpan\":").Append(MapMath.Num(Target.AimSpan));
            sb.Append(",\"ease\":").Append(MapMath.Num(Target.Ease, "0.###"));
            sb.Append(",\"error\":").Append(MapMath.JsonText(TargetError)).Append('}');

            sb.Append(",\"now\":{\"cx\":").Append(MapMath.Num(_drawn.Valid ? _drawn.Cx : Cx, "0.##"));
            sb.Append(",\"cz\":").Append(MapMath.Num(_drawn.Valid ? _drawn.Cz : Cz, "0.##"));
            sb.Append(",\"span\":").Append(MapMath.Num(_drawn.Valid ? _drawn.Span : Span, "0.##"));
            sb.Append(",\"cy\":").Append(MapMath.Num(_drawn.Valid ? _drawn.Cy : 0, "0.#")).Append('}');
            sb.Append(",\"moving\":").Append(_moving ? "true" : "false");
            sb.Append(",\"yawTarget\":").Append(MapMath.Num(YawAngle.Target, "0.##")).Append(",\"pitchTarget\":").Append(MapMath.Num(PitchAngle.Target, "0.##"));
            sb.Append(",\"turning\":").Append(YawAngle.Moving || PitchAngle.Moving ? "true" : "false");
            sb.Append(",\"alpha\":").Append(MapMath.Num(_alphaNow, "0.###")).Append(",\"alphaTarget\":").Append(MapMath.Num(_alphaGoal, "0.###"));

            sb.Append(",\"rect\":[").Append(RectX).Append(',').Append(RectY).Append(',').Append(RectW).Append(',').Append(RectH).Append(']');
            sb.Append(",\"size\":[").Append(_width).Append(',').Append(_height).Append(']');
            sb.Append(",\"fps\":").Append(Fps.ToString("0.#", ci));
            sb.Append(",\"lastDrawMs\":").Append(LastDrawMs.ToString("0.00", ci));
            sb.Append(",\"lastBlitMs\":").Append(LastBlitMs.ToString("0.00", ci));
            sb.Append(",\"draws\":").Append(Draws);
            sb.Append(",\"layers\":\"").Append(Layers).Append('"');
            sb.Append(",\"shadows\":").Append(Shadows ? "true" : "false");
            sb.Append(",\"haze\":").Append(NoHaze ? "false" : "true").Append(",\"wide\":").Append(Wide ? "true" : "false");
            sb.Append(",\"exposure\":").Append(ManualExposure < 0 ? "\"auto\"" : ManualExposure.ToString("0.###", ci));
            sb.Append(",\"marks\":");
            Marks.Status(sb);
            sb.Append(",\"clip\":{\"on\":").Append(MapHud.ClipOn ? "true" : "false").Append(",\"dropped\":").Append(MapHud.Dropped)
              .Append(",\"trimmed\":").Append(MapHud.Trimmed).Append(",\"error\":").Append(MapMath.JsonText(MapHud.ClipError)).Append('}');
            sb.Append(",\"fxError\":").Append(MapMath.JsonText(FxError));
            sb.Append(",\"up\":\"-Z (north) at yaw 0\",\"right\":\"+X (east) at yaw 0\"");
            sb.Append(",\"error\":").Append(Error == null ? "null" : "\"" + Error.Replace("\\", "\\\\").Replace("\"", "'").Replace("\n", " ") + "\"");
            sb.Append('}');
            return sb.ToString();
        }
    }
}
