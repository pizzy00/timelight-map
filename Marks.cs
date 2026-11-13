// TimeLight Map: an in-game 3D picture-in-picture map for MeitouClient.
// Copyright (C) 2026 TimeLight Map contributors
// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Text;
using Meitou.Data.Gameplay;
using Meitou.Data.Ui;
using Meitou.Game;
using Meitou.Game.Rendering.Terrain;
using Meitou.Game.Rendering.Ui;
using Meitou.Simulation;
using Meitou.Simulation.Player;

namespace TimeLightMap
{
    /// <summary>The player's living characters as this frame draws them (GameHost.DrawnPosition: between the last two
    /// snapshots, where the 3D figures stand), and their middle. Read once a frame on the render hook, for the follow
    /// target and the marks.</summary>
    internal static class Pack
    {
        public const int Max = 40;
        public const double Near = 1200, Far = 1800;     // "within about 1500 units of the first living member"

        public static int Count;
        public static readonly CharacterSnapshot[] Who = new CharacterSnapshot[Max];
        public static readonly double[] X = new double[Max], Y = new double[Max], Z = new double[Max];
        public static bool HasCentre;
        public static double CentreX, CentreY, CentreZ;

        public static void Clear()
        {
            for (int i = 0; i < Count; i++) Who[i] = null;
            Count = 0;
            HasCentre = false;
        }

        public static void Read(GameHost host)
        {
            int n = 0;
            var all = host.Session?.CurrentSnapshot?.Characters;
            if (all != null)
            {
                for (int i = 0; i < all.Count && n < Max; i++)
                {
                    var c = all[i];
                    if (c == null || !c.IsPlayer) continue;
                    if (c.Body is { Dead: true }) continue;
                    var p = host.DrawnPosition(c);
                    if (!float.IsFinite(p.X) || !float.IsFinite(p.Z)) continue;
                    (Who[n], X[n], Y[n], Z[n]) = (c, p.X, float.IsFinite(p.Y) ? p.Y : 0, p.Z);
                    n++;
                }
            }
            for (int i = n; i < Count; i++) Who[i] = null;
            Count = n;
            HasCentre = MapMath.Centroid(X, Y, Z, n, 0, Near, Far, out CentreX, out CentreY, out CentreZ);
        }
    }

    /// <summary>
    /// The map's marks, drawn by the game so they can never lag the picture.
    ///   Shapes (discs, rings, diamonds, lines, dots, arrows): a list of analytic shapes the blit's fragment shader draws
    ///   over the map picture in the same draw, in the rect's own pixels.
    ///   Texts (names, places, the scale, N): HUD text boxes (UiControl "TextBox", the Kenshi fonts) added in the
    ///   postfix on SpeechBubbles.AddSpeech, at positions worked out on the render hook from the view the blit used.
    /// Thread: commands, the render hook (Build) and the HUD (AddLabels) all run on the game's main thread (GameLoop.Frame:
    /// PollControl, DrawWorldOrSky, DrawInterface); what the hook hands the HUD still goes through one lock (Gate).
    /// A failure anywhere in here switches the marks off (Error says why); the picture stays.
    /// </summary>
    internal static class Marks
    {
        public const int MaxShapes = 256, MaxSquad = 32, MaxHostiles = 40, MaxText = 96;

        // shape kinds (the shader's; it also knows 7, a plain filled triangle): 8 is a triangle that fades away from its first corner
        const float Disc = 0, Hollow = 1, Ring = 2, Diamond = 3, Line = 4, Dots = 5, Triangle = 6, TriFade = 8;
        // palette slots (the shader's palette has 32; the rest stay empty)
        const float PalSquad = 0, PalLeader = 1, PalHostile = 2, PalRing = 3, PalLead = 4, PalChrome = 5, PalPlace = 6,
            PalConeFill = 7, PalConeEdge = 8, PalConeArc = 9, PalBack = 10;
        const float BackAlpha = 0.5f;           // the dark bar behind each text on the map (so it reads on bright ground)
        public const int PaletteSlots = 32;
        public const float ConeMinPx = 30f;     // the shortest view cone on the map, pixels
        public const float ArrowReach = 10f;    // a heading arrow's tip stands this far out from the edge of the dot
        // text styles
        const byte StyleName = 0, StyleLeader = 1, StylePlace = 2, StyleChrome = 3;

        public const string Font = "Kenshi_StandardFont_Medium";

        // ---- settings (commands) ----
        public static bool On;
        public static bool Hostiles = true;
        public static bool AutoPlaces = true;           // topcam places on|off: the game's own towns and places, once discovered
        public static string PlacesError;               // a failure there switches off only those labels
        public static int LastGamePlaces;
        public static string Error;
        public static float TextScale = 1f;
        public static int Layers = MapLayers.All;      // topcam layer <name> on|off
        public static double ConeReach = ViewCone.DefaultReach;      // topcam cone reach <x>
        public static string ConeError, ArrowError;     // a failure there switches off only that layer

        static readonly Dictionary<string, string> Captions = new Dictionary<string, string>(StringComparer.Ordinal);

        // ---- what the shader draws (render hook only) ----
        public static readonly float[] Shapes = new float[MaxShapes * 8];
        public static int ShapeCount;
        public static readonly float[] Palette = MakePalette();
        public static int PaletteVersion;      // a colour changed ('cone fill'): the blit sends the palette again
        public static int LastSquad, LastHostiles, LastTexts, LastArrows;

        static float[] MakePalette()
        {
            var p = new float[PaletteSlots * 4];
            float[] used =
            {
                0.96f, 0.96f, 0.94f, 1f,      // 0 squad: white
                1.00f, 0.80f, 0.32f, 1f,      // 1 leader: gold
                0.93f, 0.16f, 0.13f, 1f,      // 2 hostile: red
                1.00f, 1.00f, 1.00f, 0.95f,   // 3 ring (selected)
                1.00f, 1.00f, 1.00f, 0.55f,   // 4 leader lines
                1.00f, 1.00f, 1.00f, 0.92f,   // 5 scale bar, north
                0.98f, 0.92f, 0.74f, 0.95f,   // 6 place marks
                1.00f, 0.82f, 0.48f, 0.18f,   // 7 the camera's view cone, inside (it fades to a third of this at its far edge)
                1.00f, 0.82f, 0.48f, 0.95f,   // 8 its two sides (dotted)
                1.00f, 0.82f, 0.48f, 0.70f,   // 9 its far edge (dotted)
                0.03f, 0.03f, 0.035f, BackAlpha, // 10 the dark bar behind a text
            };
            Array.Copy(used, p, used.Length);
            return p;
        }

        // ---- what the HUD draws ----
        struct TextItem
        {
            public string Text;
            public float X, Y, W, H;
            public byte Style, Alpha;      // Alpha: hundredths
        }

        static readonly object Gate = new object();
        static TextItem[] _front = new TextItem[MaxText], _back = new TextItem[MaxText];
        static int _frontCount, _backCount;
        static float _clipX, _clipY, _clipW, _clipH;
        static readonly Dictionary<string, Vector2> Sizes = new Dictionary<string, Vector2>(StringComparer.Ordinal);
        static (int Height, float Min, int Mode, float Scale) _sizesFor;
        static float _lineGuess = 24f;
        static readonly UiControl[] _control = new UiControl[MaxText];
        static readonly TextItem[] _controlOf = new TextItem[MaxText];
        static readonly UiRect[] _controlClip = new UiRect[MaxText];
        static readonly string[] Names = MakeNames();
        static readonly Dictionary<string, string>[][] StylesByAlpha = new Dictionary<string, string>[101][];
        static TerrainRenderer _terrain;
        static byte _alpha = 100;

        /// <summary>The text styles at an alpha (hundredths): name, leader, place, scale and N.</summary>
        static Dictionary<string, string>[] StylesAt(int alpha)
        {
            alpha = Math.Clamp(alpha, 0, 100);
            return StylesByAlpha[alpha] ??= new[]
            {
                Style("0.96 0.96 0.94", "Left VCenter", alpha),
                Style("1 0.84 0.42", "Left VCenter", alpha),
                Style("0.98 0.92 0.74", "Center", alpha),
                Style("1 1 1", "Left VCenter", alpha),
            };
        }

        static readonly LabelLayout Layout = new LabelLayout();
        static readonly PlaceLayout PlaceSpots = new PlaceLayout();      // where the place labels' texts go (round the squad)
        static readonly string[] _placeText = new string[PlaceLayout.Max];
        static readonly Dictionary<string, string> PlaceCaptions = new Dictionary<string, string>(StringComparer.Ordinal);
        public static int LastPlacesShown, LastPlacesDropped;
        static bool _sqNames, _sqArrows;      // the squad's layers of this frame (SquadLayout -> SquadDraw)
        static readonly int[] _packOf = new int[LabelLayout.Max];
        static readonly string[] _captionOf = new string[LabelLayout.Max];
        static double _scaleMetres = -1;
        static string _scaleText = "";

        // the view cone (its outline on the ground and on the picture) and the heading arrows
        static readonly double[] _coneX = new double[18], _coneZ = new double[18];
        static readonly float[] _conePx = new float[18], _conePy = new float[18];
        static bool _coneDrawn;
        static double _coneAx, _coneAz, _coneYaw, _coneHalf, _coneLen;
        static readonly Heading Walk = new Heading();
        static readonly bool[] _walking = new bool[LabelLayout.Max];
        static readonly double[] _walkX = new double[LabelLayout.Max], _walkZ = new double[LabelLayout.Max];
        static int _limit = MaxShapes;      // the shapes the layers drawn before the squad may use (the squad's own are kept free)

        static string[] MakeNames()
        {
            var names = new string[MaxText];
            for (int i = 0; i < names.Length; i++) names[i] = "topcam-" + i.ToString(CultureInfo.InvariantCulture);
            return names;
        }

        static Dictionary<string, string> Style(string colour, string align, int alpha) => new Dictionary<string, string>
        {
            ["FontName"] = Font,
            ["TextAlign"] = align,
            ["TextShadow"] = "true",
            ["TextColour"] = colour,
            ["Alpha"] = (alpha / 100.0).ToString("0.##", CultureInfo.InvariantCulture),
        };

        public static void Fail(string where, Exception e)
        {
            On = false;
            ShapeCount = 0;
            lock (Gate) _frontCount = 0;
            Error = where + ": " + e.GetType().Name + ": " + e.Message;
            Log.Error("topcam marks switched off after an error (" + where + "): " + e);
        }

        // =====================================================================================================
        //  the frame (render hook)
        // =====================================================================================================

        /// <summary>The marks of this frame for the picture drawn with <paramref name="v"/>, shown on the rect
        /// (<paramref name="rx"/>, <paramref name="ry"/>, <paramref name="rw"/>, <paramref name="rh"/>: window pixels, origin top
        /// left). Fills Shapes for the blit and hands the texts to the HUD. Every point goes through MapView.ToPixel with its own
        /// height (a man's feet; the terrain under a place), so the marks sit on the picture at any
        /// pitch. <paramref name="alpha"/>: the map's strength (the texts fade with it; the shader fades the shapes).
        /// <paramref name="cam"/>: the main game camera of this frame (the view cone); <paramref name="gameSeconds"/>: the
        /// game clock as the frame draws it (the heading arrows). Each layer is drawn only while its switch is on (Layers); the
        /// cone goes under everything; the layers before the squad leave the squad's own shapes free (the squad never runs short).</summary>
        public static void Build(GameHost host, TerrainRenderer terrain, in MapView v, float rx, float ry, float rw, float rh, float alpha,
            in CamPose cam, double gameSeconds)
        {
            lock (Gate)
            {
                ShapeCount = 0;
                _backCount = 0;
                _terrain = terrain;
                _alpha = (byte)Math.Clamp((int)MathF.Round(alpha * 100f), 0, 100);
                _coneDrawn = false;
                LastArrows = 0;
                if (v.Valid && rw >= 16 && rh >= 16 && v.Span > 0 && v.SpanZ > 0)
                {
                    int lay = Layers;
                    int squad = Math.Min(Pack.Count, MaxSquad), each = MapLayers.On(lay, MapLayers.Arrows) ? 6 : 4;      // dot, ring, leader line, name backing (+ the arrow's two)
                    _limit = MaxShapes - Math.Min(squad * each, MaxShapes / 2);
                    SquadLayout(v, rw, rh, lay, gameSeconds);      // first, so the place labels can keep off the names and dots
                    if (MapLayers.On(lay, MapLayers.Cone)) ConeLayer(cam, v, rw, rh);
                    if (MapLayers.On(lay, MapLayers.Scale)) ScaleBar(v, rw, rh);
                    if (MapLayers.On(lay, MapLayers.North)) North(v, rw, rh);
                    Places(host, v, rw, rh, lay);
                    if (MapLayers.On(lay, MapLayers.Foes)) Foes(host, v, rw, rh);
                    else LastHostiles = 0;
                    _limit = MaxShapes;
                    SquadDraw(v, rw, rh);
                }
                // whole window pixels: sharp text, and a still picture gives the same controls every frame (the painter replays them)
                for (int i = 0; i < _backCount; i++)
                {
                    _back[i].X = MathF.Round(rx + _back[i].X);
                    _back[i].Y = MathF.Round(ry + _back[i].Y);
                    _back[i].Alpha = _alpha;
                }
                _terrain = null;
                (_front, _back) = (_back, _front);
                _frontCount = _backCount;
                (_clipX, _clipY, _clipW, _clipH) = (rx, ry, rw, rh);
                LastTexts = _frontCount;
            }
        }

        static void Shape(float kind, float a0, float a1, float a2, float a3, float size, float pal, float param)
        {
            if (ShapeCount >= _limit || ShapeCount >= MaxShapes) return;
            int o = ShapeCount++ * 8;
            var s = Shapes;
            (s[o], s[o + 1], s[o + 2], s[o + 3]) = (a0, a1, a2, a3);
            (s[o + 4], s[o + 5], s[o + 6], s[o + 7]) = (kind, size, pal, param);
        }

        static void Text(string text, float x, float y, float w, float h, byte style)
        {
            if (_backCount >= MaxText || string.IsNullOrEmpty(text)) return;
            _back[_backCount++] = new TextItem { Text = text, X = x, Y = y, W = w, H = h, Style = style };
        }

        /// <summary>The size a text is drawn at: measured by the HUD the first time it showed it, a guess until then.</summary>
        static void Size(string text, out float w, out float h)
        {
            if (Sizes.TryGetValue(text, out var s))
            {
                (w, h) = (s.X, s.Y);
                return;
            }
            h = _lineGuess;
            w = CaptionText.ShownLength(text) * _lineGuess * 0.44f + 4f;
        }

        /// <summary>The height a ground point is projected with: the terrain there when the view is tilted (straight down
        /// the height does not move the pixel, so it is not looked up).</summary>
        static double Ground(in MapView v, double x, double z)
        {
            if (v.CosPitch == 0 || _terrain == null) return v.Cy;
            float h = _terrain.HeightAt((float)x, (float)z);
            return float.IsFinite(h) ? h : v.Cy;
        }

        static bool Inside(float px, float py, float rw, float rh, float inset) =>
            px >= inset && py >= inset && px <= rw - inset && py <= rh - inset;

        static void ScaleBar(in MapView v, float rw, float rh)
        {
            double unitsPerPixel = v.Span / rw;
            double metres = MapMath.ScaleMetres(unitsPerPixel, rw * 0.24);
            if (!(metres > 0)) return;
            float len = (float)(metres * MapMath.UnitsPerMetre / unitsPerPixel);
            float x0 = 12f, y = rh - 13f;
            Shape(Line, x0, y, x0 + len, y, 2.4f, PalChrome, 1f);
            Shape(Line, x0, y - 5f, x0, y + 1f, 2f, PalChrome, 1f);
            Shape(Line, x0 + len, y - 5f, x0 + len, y + 1f, 2f, PalChrome, 1f);
            if (metres != _scaleMetres)
            {
                _scaleMetres = metres;
                _scaleText = MapMath.ScaleText(metres);
            }
            Size(_scaleText, out float tw, out float th);
            Text(_scaleText, x0 + len + 7f, y - th * 0.5f - 2f, tw, th, StyleChrome);
        }

        static void North(in MapView v, float rw, float rh)
        {
            v.DirToPixel(0, -1, rw, rh, out float nx, out float ny);
            float len = MathF.Sqrt(nx * nx + ny * ny);
            if (len > 1e-6f) (nx, ny) = (nx / len, ny / len);
            else (nx, ny) = (0f, -1f);
            float cx = rw - 17f, cy = 19f;
            Shape(Triangle, cx + nx * 8f, cy + ny * 8f, nx, ny, 15f, PalChrome, 5.5f);
            Size("N", out float tw, out float th);
            Text("N", cx - nx * 15f - tw * 0.5f + 1f, cy - ny * 15f - th * 0.5f, tw, th, StyleChrome);
        }

        /// <summary>The place labels (the "towns" layer): the game's own discovered towns and places (GamePlaces), each a
        /// diamond with its name. The texts are laid out by PlaceLayout round the squad's names and dots (laid out first,
        /// this frame), the scale bar, the N, each other and the other labels' marks: above the mark where there is room,
        /// else the nearest free spot with a thin leader line, else dropped (small places before towns); a dark bar behind each.</summary>
        static void Places(GameHost host, in MapView v, float rw, float rh, int lay)
        {
            var pl = PlaceSpots;
            pl.ClearObstacles();
            var layout = Layout;
            for (int j = 0; j < layout.Count; j++)
            {
                float r = layout.DotR[j] + 2f;
                pl.AddObstacle(layout.DotX[j] - r, layout.DotY[j] - r, layout.DotX[j] + r, layout.DotY[j] + r);
                if (_sqNames && layout.W[j] > 0)
                    pl.AddObstacle(layout.X[j], layout.Y[j], layout.X[j] + layout.W[j], layout.Y[j] + layout.H[j]);
            }
            if (MapLayers.On(lay, MapLayers.Scale)) pl.AddObstacle(0f, rh - 32f, rw * 0.42f, rh);
            if (MapLayers.On(lay, MapLayers.North)) pl.AddObstacle(rw - 40f, 0f, rw, 44f);
            int n = 0;
            if (AutoPlaces && MapLayers.On(lay, MapLayers.Towns)) n = GamePlacesLayer(host, v, rw, rh);
            else LastGamePlaces = 0;
            pl.Count = n;
            pl.Solve(rw, rh);
            int shown = 0;
            for (int k = 0; k < n; k++)
            {
                if (!pl.Shown[k]) continue;
                shown++;
                if (pl.Moved[k])
                {
                    pl.LeadEnd(k, out float lx, out float ly);
                    Shape(Line, pl.AnchorX[k], pl.AnchorY[k], lx, ly, 1.1f, PalLead, 0f);
                }
                Backing(pl.X[k], pl.Y[k], pl.W[k], pl.H[k]);
                Text(_placeText[k], pl.X[k], pl.Y[k], pl.W[k], pl.H[k], StylePlace);
            }
            LastPlacesShown = shown;
            LastPlacesDropped = pl.Dropped;
        }

        /// <summary>The game's own towns and places on the map: a failure in there switches off only these labels
        /// (PlacesError says why; 'topcam places on' tries again). Returns how many labels the layout now holds.</summary>
        static int GamePlacesLayer(GameHost host, in MapView v, float rw, float rh)
        {
            try
            {
                return GamePlaces(host, v, rw, rh);
            }
            catch (Exception e)
            {
                AutoPlaces = false;
                LastGamePlaces = 0;
                PlacesError = e.GetType().Name + ": " + e.Message;
                Log.Error("topcam: the game's place names switched off after an error (the other marks stay): " + e);
                return 0;
            }
        }

        /// <summary>The placed towns (GameHost.PopData.Sites: the list the game's own world map draws from) the player has
        /// discovered (WorldSnapshot.Towns, the same index), and the player's own outposts (WorldSnapshot.Outposts), as place
        /// labels. Which ones and under which name: KnownPlaces. Nests and camps are left out. Returns the label count.
        /// Never inlined: it names game members a later game version may not have, and that failure must surface inside
        /// GamePlacesLayer's try, not in the whole marks pass.</summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        static int GamePlaces(GameHost host, in MapView v, float rw, float rh)
        {
            int n = 0;
            LastGamePlaces = 0;
            var snapshot = host.Session?.CurrentSnapshot;
            if (snapshot == null) return 0;
            var sites = host.PopData?.Sites;
            var known = snapshot.Towns;
            if (sites != null && known != null)
            {
                int count = Math.Min(sites.Count, known.Count);
                for (int i = 0; i < count && n < PlaceLayout.Max; i++)
                {
                    var site = sites[i];
                    var town = site?.Town;
                    if (town == null) continue;
                    var type = town.Type;
                    if (type is TownType.Nest or TownType.NestMarker or TownType.Null) continue;
                    bool isTown = type == TownType.Town;
                    var at = site.Position;
                    if (!KnownPlaces.Shows(isTown, v.Span) || !KnownPlaces.Near(v, at.X, at.Z)) continue;
                    var k = known[i];
                    string name = KnownPlaces.Name((k & TownKnowledge.Discovered) != 0, (k & TownKnowledge.Explored) != 0, town.Name, town.UnexploredName);
                    if (name != null) AddGamePlace(v, rw, rh, at.X, at.Z, name, isTown, ref n);
                }
            }
            var outposts = snapshot.Outposts;
            if (outposts != null && KnownPlaces.Shows(false, v.Span))
            {
                for (int i = 0; i < outposts.Count && n < PlaceLayout.Max; i++)
                {
                    var outpost = outposts[i];
                    if (outpost == null) continue;
                    var at = outpost.Position;
                    if (!KnownPlaces.Near(v, at.X, at.Z)) continue;
                    string name = KnownPlaces.Name(true, true, outpost.Name, null);
                    if (name != null) AddGamePlace(v, rw, rh, at.X, at.Z, name, false, ref n);
                }
            }
            LastGamePlaces = n;
            return n;
        }

        /// <summary>One of the game's places as label <paramref name="n"/> of the layout, when it is on the picture: its
        /// diamond and its text. A town's text is placed before a smaller place's (PlaceLayout's priority).</summary>
        static void AddGamePlace(in MapView v, float rw, float rh, double x, double z, string name, bool isTown, ref int n)
        {
            v.ToPixel(x, Ground(v, x, z), z, rw, rh, out float px, out float py);
            if (!Inside(px, py, rw, rh, 2f)) return;
            var pl = PlaceSpots;
            Shape(Diamond, px, py, 3.5f, 0f, 0f, PalPlace, 0f);
            pl.AddObstacle(px - 4.5f, py - 4.5f, px + 4.5f, py + 4.5f);
            // one caption object per name, so a still map hands the HUD the same texts every frame
            if (!PlaceCaptions.TryGetValue(name, out string caption))
            {
                if (PlaceCaptions.Count > 512) PlaceCaptions.Clear();
                PlaceCaptions[name] = caption = CaptionText.Escape(name);
            }
            Size(caption, out float tw, out float th);
            pl.Key[n] = PlaceLayout.KeyOf(caption, x, z);
            (pl.AnchorX[n], pl.AnchorY[n], pl.Above[n], pl.W[n], pl.H[n], pl.Priority[n]) = (px, py, 7f, tw, th, isTown ? 1 : 0);
            _placeText[n] = caption;
            n++;
        }

        /// <summary>A dark rounded bar behind a text (a wide line of the shader), so it reads on bright ground.</summary>
        static void Backing(float x, float y, float w, float h)
        {
            if (!(w > 0) || !(h > 0)) return;
            float r = h * 0.42f, cy = y + h * 0.5f;
            Shape(Line, x + r * 0.6f, cy, x + w - r * 0.6f, cy, h * 0.86f, PalBack, 0f);
        }

        /// <summary>A red dot on every character the game counts as hostile to the player or fighting the player, awake
        /// and alive, on the picture.</summary>
        static void Foes(GameHost host, in MapView v, float rw, float rh)
        {
            int drawn = 0;
            if (Hostiles)
            {
                var all = host.Session?.CurrentSnapshot?.Characters;
                if (all != null)
                {
                    for (int i = 0; i < all.Count && drawn < MaxHostiles; i++)
                    {
                        var c = all[i];
                        if (c == null || c.IsPlayer || !(c.HostileToPlayer || c.FightsPlayer)) continue;
                        if (c.Body is { } body && (body.Dead || body.Unconscious)) continue;
                        var at = c.Position;      // the tick's position first: most are far off the picture
                        v.ToPixel(at.X, at.Y, at.Z, rw, rh, out float px, out float py);
                        if (!Inside(px, py, rw, rh, -40f)) continue;
                        at = host.DrawnPosition(c);
                        v.ToPixel(at.X, at.Y, at.Z, rw, rh, out px, out py);
                        if (!Inside(px, py, rw, rh, -5f)) continue;
                        Shape(Disc, px, py, 4.5f, 0f, 0f, PalHostile, 0f);
                        drawn++;
                    }
                }
            }
            LastHostiles = drawn;
        }

        /// <summary>The main camera's view cone (ViewCone), under everything else: a fan of triangles from the eye's ground
        /// point that fades towards its far edge, its two sides, its far edge. Every corner goes through MapView.ToPixel with the
        /// ground under it, so the cone lies on the picture at any tilt and turn of the map, in the same frame as the camera that
        /// drew the main picture. A failure here switches off only the cone.</summary>
        static void ConeLayer(in CamPose cam, in MapView v, float rw, float rh)
        {
            try
            {
                Cone(cam, v, rw, rh);
            }
            catch (Exception e)
            {
                _coneDrawn = false;
                Layers &= ~(1 << MapLayers.Cone);
                ConeError = e.GetType().Name + ": " + e.Message;
                Log.Error("topcam: the view cone switched off after an error (the other marks stay): " + e);
            }
        }

        static void Cone(in CamPose cam, in MapView v, float rw, float rh)
        {
            double unitsPerPixel = v.Span / Math.Max(rw, 1f);
            if (!ViewCone.Make(cam, ConeReach, ConeMinPx * unitsPerPixel, Math.Max(v.Span, v.SpanZ) * 4, out double ax, out double az, out double dx, out double dz,
                    out double half, out double len))
                return;
            int n = ViewCone.Segments(half);
            ViewCone.Points(ax, az, dx, dz, half, len, n, _coneX, _coneZ);
            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
            for (int i = 0; i <= n + 1; i++)
            {
                v.ToPixel(_coneX[i], Ground(v, _coneX[i], _coneZ[i]), _coneZ[i], rw, rh, out _conePx[i], out _conePy[i]);
                float px = _conePx[i], py = _conePy[i];
                if (!float.IsFinite(px) || !float.IsFinite(py)) return;
                (x0, y0, x1, y1) = (MathF.Min(x0, px), MathF.Min(y0, py), MathF.Max(x1, px), MathF.Max(y1, py));
            }
            (_coneAx, _coneAz, _coneYaw, _coneHalf, _coneLen) = (ax, az, Math.Atan2(dx, -dz), half, len);
            if (x1 < -2f || y1 < -2f || x0 > rw + 2f || y0 > rh + 2f) return;      // wholly off the picture
            for (int i = 1; i <= n; i++)
                Shape(TriFade, _conePx[0], _conePy[0], _conePx[i], _conePy[i], _conePx[i + 1], PalConeFill, _conePy[i + 1]);
            // a dotted outline; the dots are counted from the apex on each side
            for (int i = 1; i <= n; i++)
                Shape(Dots, _conePx[i], _conePy[i], _conePx[i + 1], _conePy[i + 1], 2f, PalConeArc, 0f);
            Shape(Dots, _conePx[1], _conePy[1], _conePx[0], _conePy[0], 2.4f, PalConeEdge, 0f);
            Shape(Dots, _conePx[n + 1], _conePy[n + 1], _conePx[0], _conePy[0], 2.4f, PalConeEdge, 0f);
            _coneDrawn = true;
        }

        /// <summary>A heading arrow on a dot: a short shaft and a head, pointing the way he walks (a level world direction) as the
        /// map shows it (turned, tilted, flipped), in the dot's colour, under the dot.</summary>
        static void Arrow(in MapView v, float x, float y, float r, double wx, double wz, float pal, float rw, float rh)
        {
            v.DirToPixel(wx, wz, rw, rh, out float dx, out float dy);
            float len = MathF.Sqrt(dx * dx + dy * dy);
            if (!(len > 1e-6f) || !float.IsFinite(len)) return;
            (dx, dy) = (dx / len, dy / len);
            float tip = r + ArrowReach;
            Shape(Line, x + dx * (r + 1f), y + dy * (r + 1f), x + dx * (tip - 6f), y + dy * (tip - 6f), 2.4f, pal, 1f);
            Shape(Triangle, x + dx * tip, y + dy * tip, dx, dy, 7f, pal, 4.5f);
            LastArrows++;
        }

        static long KeyOf(CharacterSnapshot c) => ((long)c.Id.Slot << 32) | (uint)c.Id.Generation;

        /// <summary>The squad's layout (drawn later by SquadDraw): dots always; names (with their leader lines) while the names
        /// layer is on; heading arrows while the arrows layer is on (a failure there switches off only the arrows).</summary>
        static void SquadLayout(in MapView v, float rw, float rh, int lay, double gameSeconds)
        {
            var layout = Layout;
            bool names = MapLayers.On(lay, MapLayers.Names), arrows = MapLayers.On(lay, MapLayers.Arrows);
            int n = Math.Min(Pack.Count, MaxSquad), k = 0;
            if (arrows)
            {
                try { Walk.BeginFrame(); }
                catch (Exception e) { arrows = ArrowsFail(e); }
            }
            for (int i = 0; i < n; i++)
            {
                var c = Pack.Who[i];
                if (c == null) continue;
                bool walking = false;
                double hx = 0, hz = 0;
                if (arrows)
                {
                    try { walking = Walk.Step(KeyOf(c), Pack.X[i], Pack.Z[i], gameSeconds, out hx, out hz); }      // every man, on the picture or not
                    catch (Exception e) { arrows = ArrowsFail(e); }
                }
                v.ToPixel(Pack.X[i], Pack.Y[i], Pack.Z[i], rw, rh, out float px, out float py);
                if (!Inside(px, py, rw, rh, -8f)) continue;
                string caption = null;
                float w = 0, h = 0;
                if (names)
                {
                    caption = Caption(c.Name);
                    Size(caption, out w, out h);
                }
                _packOf[k] = i;
                _captionOf[k] = caption;
                (_walking[k], _walkX[k], _walkZ[k]) = (walking, hx, hz);
                layout.Key[k] = KeyOf(c);
                (layout.DotX[k], layout.DotY[k], layout.DotR[k], layout.W[k], layout.H[k]) = (px, py, i == 0 ? 6f : 5f, w, h);
                k++;
            }
            if (arrows)
            {
                try { Walk.EndFrame(); }
                catch (Exception e) { arrows = ArrowsFail(e); }
            }
            layout.Count = k;
            if (names) layout.Solve(rw, rh);
            LastSquad = k;
            (_sqNames, _sqArrows) = (names, arrows);
        }

        /// <summary>The squad as SquadLayout placed it (the layout comes first so the place labels can keep off it):
        /// leader lines, arrows, rings, dots, and each name on a dark bar.</summary>
        static void SquadDraw(in MapView v, float rw, float rh)
        {
            var layout = Layout;
            bool names = _sqNames, arrows = _sqArrows;
            int k = layout.Count;

            if (names)
                for (int j = 0; j < k; j++)      // leader lines under everything else
                {
                    if (!layout.Stacked[j]) continue;
                    float dx = layout.LeadX[j] - layout.DotX[j], dy = layout.LeadY[j] - layout.DotY[j], len = MathF.Sqrt(dx * dx + dy * dy);
                    float from = layout.DotR[j] + 1.5f;
                    if (len <= from + 2f) continue;
                    Shape(Line, layout.DotX[j] + dx / len * from, layout.DotY[j] + dy / len * from, layout.LeadX[j], layout.LeadY[j], 1.3f, PalLead, 0f);
                }
            if (arrows)
                for (int j = 0; j < k; j++)      // under the dots
                    if (_walking[j]) Arrow(v, layout.DotX[j], layout.DotY[j], layout.DotR[j], _walkX[j], _walkZ[j], _packOf[j] == 0 ? PalLeader : PalSquad, rw, rh);
            for (int j = 0; j < k; j++)
            {
                var c = Pack.Who[_packOf[j]];
                if (c.Selected) Shape(Ring, layout.DotX[j], layout.DotY[j], layout.DotR[j] + 4.5f, 2f, 0f, PalRing, 0f);
            }
            for (int j = k - 1; j >= 0; j--)      // the first man (the leader) last: on top
            {
                var c = Pack.Who[_packOf[j]];
                bool leader = _packOf[j] == 0;
                bool out_ = c.Body is { } body && (body.Unconscious || body.Coma);
                Shape(out_ ? Hollow : Disc, layout.DotX[j], layout.DotY[j], layout.DotR[j], 2.2f, 0f, leader ? PalLeader : PalSquad, 0f);
                if (names && !string.IsNullOrEmpty(_captionOf[j]))
                {
                    Backing(layout.X[j], layout.Y[j], layout.W[j], layout.H[j]);
                    Text(_captionOf[j], layout.X[j], layout.Y[j], layout.W[j], layout.H[j], leader ? StyleLeader : StyleName);
                }
            }
        }

        static bool ArrowsFail(Exception e)
        {
            Layers &= ~(1 << MapLayers.Arrows);
            ArrowError = e.GetType().Name + ": " + e.Message;
            Log.Error("topcam: the heading arrows switched off after an error (the other marks stay): " + e);
            try { Walk.Clear(); } catch { }
            return false;
        }

        /// <summary>A squad member's name as a caption (CaptionText.Escape), one object per name so a still map hands the
        /// HUD the same texts every frame.</summary>
        static string Caption(string name)
        {
            if (string.IsNullOrEmpty(name)) return "?";
            if (Captions.TryGetValue(name, out string caption)) return caption;
            if (Captions.Count > 256) Captions.Clear();
            return Captions[name] = CaptionText.Escape(name);
        }

        // =====================================================================================================
        //  the HUD (postfix on SpeechBubbles.AddSpeech)
        // =====================================================================================================

        /// <summary>The frame's texts as HUD text boxes, clipped to the map rect.</summary>
        public static void AddLabels(List<UiControl> controls)
        {
            if (!On || controls == null) return;
            try
            {
                if (!TopCam.MapShown(out _, out _, out _, out _)) return;
                var player = TopCam.Host?.Player;
                var painter = player?.UiPainter;
                if (painter == null) return;
                float minimum = player.TitleOpen ? 0f : player.StandardPixels;      // as SpeechBubbles measures and the HUD draws
                var key = (player.Height, minimum, (int)painter.Mode, TextScale);
                lock (Gate)
                {
                    if (key != _sizesFor || Sizes.Count > 512)
                    {
                        Sizes.Clear();
                        _sizesFor = key;
                        Array.Clear(_control, 0, _control.Length);
                    }
                    var clip = new UiRect(_clipX, _clipY, _clipW, _clipH);
                    for (int i = 0; i < _frontCount; i++)
                    {
                        ref var t = ref _front[i];
                        if (!Sizes.TryGetValue(t.Text, out var size))
                        {
                            var measured = painter.WrapCaption(Font, t.Text, TextScale, 100000f, minimum).Size;
                            size = new Vector2(MathF.Ceiling(measured.Width) + 4f, MathF.Ceiling(measured.Height) + 2f);
                            Sizes[t.Text] = size;
                            if (size.Y > 4f) _lineGuess = size.Y;
                        }
                        // a text met for the first time was laid out with a guess: its box is at least its real size
                        float w = MathF.Max(t.W, size.X), h = MathF.Max(t.H, size.Y);
                        var c = _control[i];
                        ref var of = ref _controlOf[i];
                        if (c == null || !ReferenceEquals(of.Text, t.Text) || of.X != t.X || of.Y != t.Y || of.W != w || of.H != h || of.Style != t.Style
                            || of.Alpha != t.Alpha
                            || _controlClip[i] != clip || c.TextScale != TextScale)
                        {
                            c = new UiControl(Names[i], "TextBox", new UiRect(t.X, t.Y, w, h), t.Text, "", "", true, StylesAt(t.Alpha)[t.Style], Vector4.Zero,
                                Pickable: false, TextScale: TextScale, Clip: clip);
                            _control[i] = c;
                            of = new TextItem { Text = t.Text, X = t.X, Y = t.Y, W = w, H = h, Style = t.Style, Alpha = t.Alpha };
                            _controlClip[i] = clip;
                        }
                        controls.Add(c);
                    }
                }
            }
            catch (Exception e)
            {
                Fail("labels", e);
            }
        }

        // =====================================================================================================
        //  commands (main thread)
        // =====================================================================================================

        static readonly CultureInfo Ci = CultureInfo.InvariantCulture;

        static bool Num(IReadOnlyList<string> a, int i, out double v)
        {
            v = 0;
            return i < a.Count && double.TryParse(a[i], NumberStyles.Float, Ci, out v) && double.IsFinite(v);
        }

        /// <summary>The marks' commands (a[0] is the word after "topcam"). Null: not one of them; "": done; else the refusal.</summary>
        public static string Command(IReadOnlyList<string> a)
        {
            switch (a[0].ToLowerInvariant())
            {
                case "marks":
                    if (a.Count != 2) return "usage: topcam marks on|off";
                    if (a[1].Equals("on", StringComparison.OrdinalIgnoreCase))
                    {
                        Error = null;
                        Layout.Forget();
                        On = true;
                    }
                    else if (a[1].Equals("off", StringComparison.OrdinalIgnoreCase))
                    {
                        On = false;
                        ShapeCount = 0;
                        lock (Gate) _frontCount = 0;
                    }
                    else return "usage: topcam marks on|off";
                    return "";
                case "hostiles":
                    if (a.Count != 2) return "usage: topcam hostiles on|off   (the red dots of characters hostile to the player)";
                    Hostiles = a[1].Equals("on", StringComparison.OrdinalIgnoreCase);
                    return "";
                case "places":
                {
                    bool on = a.Count == 2 && a[1].Equals("on", StringComparison.OrdinalIgnoreCase);
                    if (!on && !(a.Count == 2 && a[1].Equals("off", StringComparison.OrdinalIgnoreCase)))
                        return "usage: topcam places on|off   (the names of the towns and places you have discovered, from the game's own map data)";
                    AutoPlaces = on;
                    if (on) PlacesError = null;
                    return "";
                }
                case "textscale":
                    if (a.Count != 2 || !Num(a, 1, out double scale) || scale < 0.5 || scale > 3)
                        return "usage: topcam textscale <0.5..3>   (1: the HUD's standard text, which is also the smallest the HUD draws)";
                    TextScale = (float)scale;
                    return "";
                case "cone":
                {
                    // cone [on|off] [reach <0.5..8>] [fill <0..1>]: how far the view cone reaches (times the camera's ground distance to
                    // the point it looks at; the picture's top edge on the ground caps it) and how strong its inside is; no argument: the state
                    const string usage = "usage: topcam cone [on|off] [reach <0.5..8>] [fill <0..1>]   (the view cone; also: topcam layer cone on|off)";
                    if (a.Count >= 2 && (a[1].Equals("on", StringComparison.OrdinalIgnoreCase) || a[1].Equals("off", StringComparison.OrdinalIgnoreCase)))
                    {
                        // topcam cone on|off = topcam layer cone on|off (the same switch, reported in marks.layers.cone)
                        bool coneOn = a[1].Equals("on", StringComparison.OrdinalIgnoreCase);
                        if (coneOn && !MapLayers.On(Layers, MapLayers.Cone)) ConeError = null;
                        Layers = coneOn ? Layers | (1 << MapLayers.Cone) : Layers & ~(1 << MapLayers.Cone);
                        if (a.Count == 2) return "";
                        var rest = new List<string> { a[0] };
                        for (int r = 2; r < a.Count; r++) rest.Add(a[r]);
                        a = rest;
                    }
                    if ((a.Count - 1) % 2 != 0) return usage;
                    double reach = ConeReach, fill = Palette[(int)PalConeFill * 4 + 3];
                    for (int i = 1; i < a.Count; i += 2)
                    {
                        if (!Num(a, i + 1, out double val)) return usage;
                        if (a[i].Equals("reach", StringComparison.OrdinalIgnoreCase) && val >= ViewCone.MinReach && val <= ViewCone.MaxReach) reach = val;
                        else if (a[i].Equals("fill", StringComparison.OrdinalIgnoreCase) && val >= 0 && val <= 1) fill = val;
                        else return usage;
                    }
                    ConeReach = reach;
                    if (Palette[(int)PalConeFill * 4 + 3] != (float)fill)
                    {
                        Palette[(int)PalConeFill * 4 + 3] = (float)fill;
                        PaletteVersion++;
                    }
                    return "";
                }
                case "layer":
                {
                    // layer [<name> on|off ...] | layer all on|off; no argument: the state (the status's marks.layers)
                    if (a.Count == 1) return "";
                    string bad = MapLayers.Apply(a, 1, Layers, out int mask);
                    if (bad != null) return bad;
                    if (MapLayers.On(mask, MapLayers.Cone) && !MapLayers.On(Layers, MapLayers.Cone)) ConeError = null;
                    if (MapLayers.On(mask, MapLayers.Arrows) && !MapLayers.On(Layers, MapLayers.Arrows)) ArrowError = null;
                    Layers = mask;
                    return "";
                }
                default:
                    return null;
            }
        }

        public static void Status(StringBuilder sb)
        {
            sb.Append("{\"on\":").Append(On ? "true" : "false");
            sb.Append(",\"squad\":").Append(LastSquad).Append(",\"hostiles\":").Append(LastHostiles);
            sb.Append(",\"autoHostiles\":").Append(Hostiles ? "true" : "false");
            sb.Append(",\"shapes\":").Append(ShapeCount).Append(",\"texts\":").Append(LastTexts);
            sb.Append(",\"textScale\":").Append(MapMath.Num(TextScale, "0.##"));
            // the layers, the cone of the last frame, the arrows drawn in it
            sb.Append(",\"layers\":");
            MapLayers.Json(sb, Layers);
            sb.Append(",\"cone\":{\"drawn\":").Append(_coneDrawn ? "true" : "false");
            if (_coneLen > 0)
            {
                double heading = _coneYaw * 180 / Math.PI;
                if (heading < 0) heading += 360;
                sb.Append(",\"x\":").Append(MapMath.Num(_coneAx)).Append(",\"z\":").Append(MapMath.Num(_coneAz))
                  .Append(",\"heading\":").Append(MapMath.Num(heading, "0.#")).Append(",\"open\":").Append(MapMath.Num(_coneHalf * 360 / Math.PI, "0.#"))
                  .Append(",\"len\":").Append(MapMath.Num(_coneLen));
            }
            sb.Append(",\"reach\":").Append(MapMath.Num(ConeReach, "0.##")).Append(",\"fill\":").Append(MapMath.Num(Palette[(int)PalConeFill * 4 + 3], "0.##"));
            sb.Append(",\"error\":").Append(MapMath.JsonText(ConeError));
            sb.Append(",\"on\":").Append(MapLayers.On(Layers, MapLayers.Cone) ? "true" : "false").Append(",\"dotted\":true").Append('}');
            // the place labels of the last frame: drawn, and dropped for want of room round the squad
            sb.Append(",\"placeTexts\":{\"shown\":").Append(LastPlacesShown).Append(",\"dropped\":").Append(LastPlacesDropped).Append('}');
            // the game's own towns and places: the switch, how many were on the last picture, and why it went off if it did
            sb.Append(",\"places\":{\"on\":").Append(AutoPlaces ? "true" : "false").Append(",\"shown\":").Append(LastGamePlaces)
              .Append(",\"error\":").Append(MapMath.JsonText(PlacesError)).Append('}');
            sb.Append(",\"arrows\":{\"drawn\":").Append(LastArrows).Append(",\"tracked\":").Append(Walk.Count)
              .Append(",\"error\":").Append(MapMath.JsonText(ArrowError)).Append('}');
            sb.Append(",\"error\":").Append(MapMath.JsonText(Error)).Append('}');
        }
    }

    /// <summary>
    /// The HUD's side of the map: Harmony patches on the methods that add the world-anchored controls of a frame.
    ///   SpeechBubbles.AddSpeech (prefix + postfix): the prefix notes how many controls the list held, the postfix clips the
    ///     speech bubbles the method added off the map rect and then adds the map's own texts.
    ///   StealthView.AddNameTags / AddTaskBars, GroundItemPicks.AddItemLabels, DamageFloaters.AddFloaters (prefix + postfix):
    ///     the prefix notes how many controls the list held, the postfix clips the ones the method added off the map rect.
    /// "Clipped": a control wholly under the map is dropped; one that straddles the map's edge keeps the part outside it
    /// (UiControl.Clip, which the painter honours for the background, the skin, the picture and the caption); controls
    /// elsewhere are not touched. Only while the map picture is on screen (TopCam.MapShown).
    /// </summary>
    internal static class MapHud
    {
        public static bool ClipOn = true;
        public static string ClipError;
        public static long Dropped, Trimmed;

        // every patched adder has the shape (List<UiControl> controls): the prefix notes where its controls will begin
        public static void CountPrefix(List<UiControl> controls, out int __state) => __state = controls != null ? controls.Count : 0;

        // SpeechBubbles.AddSpeech(List<UiControl> controls)
        public static void SpeechPostfix(List<UiControl> controls, int __state)
        {
            if (controls == null) return;
            ClipAdded(controls, __state);
            Marks.AddLabels(controls);
        }


        public static void ClipPostfix(List<UiControl> controls, int __state)
        {
            if (controls != null) ClipAdded(controls, __state);
        }

        static void ClipAdded(List<UiControl> controls, int from)
        {
            if (!ClipOn || from < 0 || from >= controls.Count) return;
            try
            {
                if (!TopCam.MapShown(out float mx, out float my, out float mw, out float mh)) return;
                float mx1 = mx + mw, my1 = my + mh;
                int write = from;
                for (int read = from; read < controls.Count; read++)
                {
                    var c = controls[read];
                    if (c != null)
                    {
                        var b = c.Bounds;
                        float x0 = b.X, y0 = b.Y, x1 = b.X + b.Width, y1 = b.Y + b.Height;
                        if (x0 < mx1 && mx < x1 && y0 < my1 && my < y1)      // it touches the map
                        {
                            // the part outside the map: the largest of the strips left, right, above and below it
                            float left = MathF.Max(mx - x0, 0f) * (y1 - y0), right = MathF.Max(x1 - mx1, 0f) * (y1 - y0);
                            float above = MathF.Max(my - y0, 0f) * (x1 - x0), below = MathF.Max(y1 - my1, 0f) * (x1 - x0);
                            float best = MathF.Max(MathF.Max(left, right), MathF.Max(above, below));
                            if (!(best > 0f))
                            {
                                Dropped++;
                                continue;      // wholly under the map
                            }
                            UiRect keep = best == left ? new UiRect(x0, y0, mx - x0, y1 - y0)
                                : best == right ? new UiRect(mx1, y0, x1 - mx1, y1 - y0)
                                : best == above ? new UiRect(x0, y0, x1 - x0, my - y0)
                                : new UiRect(x0, my1, x1 - x0, y1 - my1);
                            if (c.Clip is { } old)
                            {
                                float kx0 = MathF.Max(keep.X, old.X), ky0 = MathF.Max(keep.Y, old.Y);
                                float kx1 = MathF.Min(keep.X + keep.Width, old.X + old.Width), ky1 = MathF.Min(keep.Y + keep.Height, old.Y + old.Height);
                                if (kx1 <= kx0 || ky1 <= ky0)
                                {
                                    Dropped++;
                                    continue;
                                }
                                keep = new UiRect(kx0, ky0, kx1 - kx0, ky1 - ky0);
                            }
                            c = c with { Clip = keep };
                            Trimmed++;
                        }
                    }
                    controls[write++] = c;
                }
                if (write < controls.Count) controls.RemoveRange(write, controls.Count - write);
            }
            catch (Exception e)
            {
                ClipOn = false;
                ClipError = e.GetType().Name + ": " + e.Message;
                Log.Error("topcam clip (name tags off the map) switched off after an error: " + e);
            }
        }
    }
}
