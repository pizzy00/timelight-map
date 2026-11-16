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
    /// <summary>Whom 'follow' keeps the map on: the active squad (the squad bar's), or the people selected now.</summary>
    internal enum FollowWho { Squad, Selected }

    /// <summary>The player's living characters as this frame draws them (GameHost.DrawnPosition: between the last two
    /// snapshots, where the 3D figures stand), the active squad first (the squad bar's squad, its leader first, in squad
    /// order), then everybody else of the player's; and the middle of the people the map follows. Read once a frame on the
    /// render hook, for the follow target and the marks.</summary>
    internal static class Pack
    {
        public const int Max = 64;
        public const double Near = 1200, Far = 1800;     // "within about 1500 units of the first living member"

        public static int Count;                         // everybody read
        public static int Squad;                         // the first Squad of them are the active squad (0: the game named none)
        public static int Followed;                      // how many the centre is the middle of
        public static readonly CharacterSnapshot[] Who = new CharacterSnapshot[Max];
        public static readonly double[] X = new double[Max], Y = new double[Max], Z = new double[Max];
        public static bool HasCentre;
        public static double CentreX, CentreY, CentreZ;

        static readonly Dictionary<long, int> Index = new Dictionary<long, int>();
        static readonly CharacterSnapshot[] _who = new CharacterSnapshot[Max];
        static readonly double[] _x = new double[Max], _y = new double[Max], _z = new double[Max];
        static readonly bool[] _taken = new bool[Max];

        public static long KeyOf(CharacterSnapshot c) => ((long)c.Id.Slot << 32) | (uint)c.Id.Generation;

        public static void Clear()
        {
            for (int i = 0; i < Count; i++) Who[i] = null;
            Count = Squad = Followed = 0;
            HasCentre = false;
        }

        public static void Read(GameHost host, FollowWho who)
        {
            // everybody of the player's who lives and stands somewhere, in the snapshot's order
            int n = 0;
            var snapshot = host.Session?.CurrentSnapshot;
            var all = snapshot?.Characters;
            if (all != null)
            {
                for (int i = 0; i < all.Count && n < Max; i++)
                {
                    var c = all[i];
                    if (c == null || !c.IsPlayer) continue;
                    if (c.Body is { Dead: true }) continue;
                    var p = host.DrawnPosition(c);
                    if (!float.IsFinite(p.X) || !float.IsFinite(p.Z)) continue;
                    (_who[n], _x[n], _y[n], _z[n], _taken[n]) = (c, p.X, float.IsFinite(p.Y) ? p.Y : 0, p.Z, false);
                    n++;
                }
            }

            // the active squad first, in its own order (the leader first), then the rest as they came
            int k = 0;
            var squads = snapshot?.PlayerSquads;
            int active = snapshot?.ActiveSquad ?? -1;
            if (squads != null && active >= 0 && n > 0)
            {
                Index.Clear();
                for (int i = 0; i < n; i++) Index[KeyOf(_who[i])] = i;
                for (int s = 0; s < squads.Count; s++)
                {
                    var squad = squads[s];
                    if (squad == null || squad.Id != active || squad.Members == null) continue;
                    foreach (var id in squad.Members)
                    {
                        if (!Index.TryGetValue(((long)id.Slot << 32) | (uint)id.Generation, out int i) || _taken[i]) continue;
                        _taken[i] = true;
                        Put(k++, i);
                    }
                    break;
                }
            }
            Squad = k;
            for (int i = 0; i < n; i++)
                if (!_taken[i]) Put(k++, i);
            for (int i = k; i < Count; i++) Who[i] = null;
            for (int i = 0; i < n; i++) _who[i] = null;
            Count = k;

            // the middle: of the selected people when asked and there are any, else of the active squad (everybody when the
            // game named no squad), weighted round its first man
            HasCentre = false;
            Followed = 0;
            if (who == FollowWho.Selected)
            {
                int m = 0, anchor = 0;
                for (int i = 0; i < Count; i++)
                {
                    if (!Who[i].Selected) continue;
                    if (Who[i].FirstSelected) anchor = m;
                    (_x[m], _y[m], _z[m]) = (X[i], Y[i], Z[i]);
                    m++;
                }
                if (m > 0)
                {
                    Followed = m;
                    HasCentre = MapMath.Centroid(_x, _y, _z, m, anchor, Near, Far, out CentreX, out CentreY, out CentreZ);
                    return;
                }
            }
            Followed = Squad > 0 ? Squad : Count;
            HasCentre = MapMath.Centroid(X, Y, Z, Followed, 0, Near, Far, out CentreX, out CentreY, out CentreZ);
        }

        static void Put(int to, int from) => (Who[to], X[to], Y[to], Z[to]) = (_who[from], _x[from], _y[from], _z[from]);
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
        public const int MaxShapes = 256, MaxPeople = 48, MaxHostiles = 40, MaxText = 96, MaxRouteShapes = 72;

        // shape kinds (the shader's; it also knows 7, a plain filled triangle): 8 is a triangle that fades away from its first corner
        const float Disc = 0, Hollow = 1, Ring = 2, Diamond = 3, Line = 4, Dots = 5, Triangle = 6, TriFade = 8;
        // palette slots (the shader's palette has 32; the rest stay empty)
        const float PalSquad = 0, PalLeader = 1, PalHostile = 2, PalRing = 3, PalLead = 4, PalChrome = 5, PalPlace = 6,
            PalConeFill = 7, PalConeEdge = 8, PalConeArc = 9, PalBack = 10, PalOther = 11, PalRoute = 12, PalRouteFar = 13,
            PalHurt = 14, PalHurtBad = 15;
        const float BackAlpha = 0.5f;           // the dark bar behind each text on the map (so it reads on bright ground)
        public const int PaletteSlots = 32;
        public const float ConeMinPx = 30f;     // the shortest view cone on the map, pixels
        public const float ArrowReach = 10f;    // a heading arrow's tip stands this far out from the edge of the dot
        // text styles
        const byte StyleName = 0, StyleLeader = 1, StylePlace = 2, StyleChrome = 3;

        public const string Font = "Kenshi_StandardFont_Medium";

        // ---- settings (commands) ----
        public static bool On;
        public static string PlacesError;               // a failure there switches off only the towns layer
        public static int LastGamePlaces;
        public static string Error;
        public static float TextScale = 1f;
        public static int Layers = MapLayers.All;      // topcam layer <name> on|off
        public static double ConeReach = ViewCone.DefaultReach;      // topcam cone reach <x>
        public static string ConeError, ArrowError, RouteError;     // a failure there switches off only that layer

        static readonly Dictionary<string, string> Captions = new Dictionary<string, string>(StringComparer.Ordinal);

        // ---- what the shader draws (render hook only) ----
        public static readonly float[] Shapes = new float[MaxShapes * 8];
        public static int ShapeCount;
        public static readonly float[] Palette = MakePalette();
        public static int PaletteVersion;      // a colour changed ('cone fill'): the blit sends the palette again
        public static int LastSquad, LastOthers, LastHostiles, LastTexts, LastArrows, LastRoutes, LastRoutesSkipped, LastHurt;

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
                0.74f, 0.78f, 0.84f, 0.95f,   // 11 the player's people in other squads: grey-blue
                0.50f, 0.86f, 1.00f, 0.95f,   // 12 a route (dotted) and its end
                0.50f, 0.86f, 1.00f, 0.45f,   // 13 a route's provisional, long-range part
                1.00f, 0.58f, 0.12f, 1f,      // 14 hurt: orange ring
                0.95f, 0.13f, 0.10f, 1f,      // 15 badly hurt: red ring
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
        static bool _round;                 // this frame's map is round: texts stay inside its circle
        static float _rw, _rh, _radius;

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
        static readonly PlaceLayout PlaceSpots = new PlaceLayout();      // where the place labels' texts go (round your people)
        static readonly RoutePlan Route = new RoutePlan();
        static readonly string[] _placeText = new string[PlaceLayout.Max];
        static readonly Dictionary<string, string> PlaceCaptions = new Dictionary<string, string>(StringComparer.Ordinal);
        public static int LastPlacesShown, LastPlacesDropped;
        static bool _sqNames, _sqArrows, _sqHurt;      // the people's layers of this frame (PeopleLayout -> PeopleDraw)
        static double _scaleMetres = -1;
        static string _scaleText = "";

        // the people drawn this frame (PeopleLayout): pack index, dot, and which of them carry a name (in the label layout)
        static int _people;
        static readonly int[] _packOf = new int[MaxPeople], _labelOf = new int[MaxPeople], _hurtOf = new int[MaxPeople];
        static readonly float[] _dotX = new float[MaxPeople], _dotY = new float[MaxPeople], _dotR = new float[MaxPeople];
        static readonly string[] _captionOf = new string[LabelLayout.Max];

        // the view cone (its outline on the ground and on the picture) and the heading arrows
        static readonly double[] _coneX = new double[18], _coneZ = new double[18];
        static readonly float[] _conePx = new float[18], _conePy = new float[18];
        static bool _coneDrawn;
        static double _coneAx, _coneAz, _coneYaw, _coneHalf, _coneLen;
        static readonly Heading Walk = new Heading();
        static readonly bool[] _walking = new bool[MaxPeople];
        static readonly double[] _walkX = new double[MaxPeople], _walkZ = new double[MaxPeople];
        static int _limit = MaxShapes;      // the shapes the layers drawn before your people may use (theirs are kept free)

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
        /// game clock as the frame draws it (the heading arrows); <paramref name="round"/>: the map is a circle (the shader
        /// masks the shapes, the texts outside it are left out here). Each layer is drawn only while its switch is on (Layers);
        /// the cone goes under everything; the layers before your people leave their own shapes free (they never run short).</summary>
        public static void Build(GameHost host, TerrainRenderer terrain, in MapView v, float rx, float ry, float rw, float rh, float alpha,
            in CamPose cam, double gameSeconds, bool round)
        {
            lock (Gate)
            {
                ShapeCount = 0;
                _backCount = 0;
                _terrain = terrain;
                _alpha = (byte)Math.Clamp((int)MathF.Round(alpha * 100f), 0, 100);
                (_round, _rw, _rh, _radius) = (round, rw, rh, MapShape.Radius(rw, rh));
                _coneDrawn = false;
                LastArrows = LastRoutes = LastRoutesSkipped = LastHurt = 0;
                if (v.Valid && rw >= 16 && rh >= 16 && v.Span > 0 && v.SpanZ > 0)
                {
                    int lay = Layers;
                    int people = Math.Min(Pack.Count, MaxPeople);
                    int each = 4 + (MapLayers.On(lay, MapLayers.Arrows) ? 2 : 0) + (MapLayers.On(lay, MapLayers.Hurt) ? 1 : 0);
                    _limit = MaxShapes - Math.Min(people * each, MaxShapes / 2);
                    PeopleLayout(v, rw, rh, lay, gameSeconds);      // first, so the place labels can keep off the names and dots
                    if (MapLayers.On(lay, MapLayers.Cone)) ConeLayer(cam, v, rw, rh);
                    if (MapLayers.On(lay, MapLayers.Route)) RouteLayer(v, rw, rh);
                    if (MapLayers.On(lay, MapLayers.Scale)) ScaleBar(v, rw, rh);
                    if (MapLayers.On(lay, MapLayers.North)) North(v, rw, rh);
                    Places(host, v, rw, rh, lay);
                    if (MapLayers.On(lay, MapLayers.Foes)) Foes(host, v, rw, rh);
                    else LastHostiles = 0;
                    _limit = MaxShapes;
                    PeopleDraw(v, rw, rh);
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

        /// <summary>Whether a text box can be shown: on a round map only wholly inside its circle.</summary>
        static bool Fits(float x, float y, float w, float h) =>
            !_round || MapShape.BoxInCircle(x, y, x + w, y + h, _rw * 0.5f, _rh * 0.5f, _radius - 1f);

        static void Text(string text, float x, float y, float w, float h, byte style)
        {
            if (_backCount >= MaxText || string.IsNullOrEmpty(text) || !Fits(x, y, w, h)) return;
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

        /// <summary>Where the scale bar starts and stands: the bottom left; on a round map the bottom middle (bar and text
        /// centred together).</summary>
        static void ScaleSpot(float rw, float rh, float len, float textW, out float x0, out float y)
        {
            if (_round)
            {
                (x0, y) = (rw * 0.5f - (len + 7f + textW) * 0.5f, rh * 0.5f + _radius * 0.80f);
                return;
            }
            (x0, y) = (12f, rh - 13f);
        }

        /// <summary>Where the N arrow stands: the top right; on a round map on the rim, 40 degrees right of the top.</summary>
        static void NorthSpot(float rw, float rh, out float cx, out float cy)
        {
            if (_round)
            {
                float r = _radius - 20f, a = 40f * MathF.PI / 180f;
                (cx, cy) = (rw * 0.5f + MathF.Sin(a) * r, rh * 0.5f - MathF.Cos(a) * r);
                return;
            }
            (cx, cy) = (rw - 17f, 19f);
        }

        static void ScaleBar(in MapView v, float rw, float rh)
        {
            double unitsPerPixel = v.Span / rw;
            double metres = MapMath.ScaleMetres(unitsPerPixel, rw * 0.24);
            if (!(metres > 0)) return;
            float len = (float)(metres * MapMath.UnitsPerMetre / unitsPerPixel);
            if (metres != _scaleMetres)
            {
                _scaleMetres = metres;
                _scaleText = MapMath.ScaleText(metres);
            }
            Size(_scaleText, out float tw, out float th);
            ScaleSpot(rw, rh, len, tw, out float x0, out float y);
            Shape(Line, x0, y, x0 + len, y, 2.4f, PalChrome, 1f);
            Shape(Line, x0, y - 5f, x0, y + 1f, 2f, PalChrome, 1f);
            Shape(Line, x0 + len, y - 5f, x0 + len, y + 1f, 2f, PalChrome, 1f);
            Text(_scaleText, x0 + len + 7f, y - th * 0.5f - 2f, tw, th, StyleChrome);
        }

        static void North(in MapView v, float rw, float rh)
        {
            v.DirToPixel(0, -1, rw, rh, out float nx, out float ny);
            float len = MathF.Sqrt(nx * nx + ny * ny);
            if (len > 1e-6f) (nx, ny) = (nx / len, ny / len);
            else (nx, ny) = (0f, -1f);
            NorthSpot(rw, rh, out float cx, out float cy);
            Shape(Triangle, cx + nx * 8f, cy + ny * 8f, nx, ny, 15f, PalChrome, 5.5f);
            Size("N", out float tw, out float th);
            Text("N", cx - nx * 15f - tw * 0.5f + 1f, cy - ny * 15f - th * 0.5f, tw, th, StyleChrome);
        }

        /// <summary>The place labels (the "towns" layer): the game's own discovered towns and places (GamePlaces), each a
        /// diamond with its name. The texts are laid out by PlaceLayout round your people's names and dots (laid out first,
        /// this frame), the scale bar, the N, each other and the other labels' marks: above the mark where there is room,
        /// else the nearest free spot with a thin leader line, else dropped (small places before towns); a dark bar behind each.
        /// On a round map every text stays inside the circle.</summary>
        static void Places(GameHost host, in MapView v, float rw, float rh, int lay)
        {
            var pl = PlaceSpots;
            pl.ClearObstacles();
            pl.CircleR = _round ? _radius : 0f;
            var layout = Layout;
            for (int j = 0; j < _people; j++)
            {
                float r = _dotR[j] + 2f;
                pl.AddObstacle(_dotX[j] - r, _dotY[j] - r, _dotX[j] + r, _dotY[j] + r);
            }
            if (_sqNames)
                for (int j = 0; j < layout.Count; j++)
                    if (layout.W[j] > 0) pl.AddObstacle(layout.X[j], layout.Y[j], layout.X[j] + layout.W[j], layout.Y[j] + layout.H[j]);
            if (MapLayers.On(lay, MapLayers.Scale))
            {
                if (_round) pl.AddObstacle(rw * 0.2f, rh * 0.5f + _radius * 0.80f - 18f, rw * 0.8f, rh);
                else pl.AddObstacle(0f, rh - 32f, rw * 0.42f, rh);
            }
            if (MapLayers.On(lay, MapLayers.North))
            {
                NorthSpot(rw, rh, out float nx, out float ny);
                pl.AddObstacle(nx - 23f, ny - 25f, nx + 23f, ny + 25f);
            }
            int n = MapLayers.On(lay, MapLayers.Towns) ? GamePlacesLayer(host, v, rw, rh) : 0;
            if (n == 0) LastGamePlaces = 0;
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

        /// <summary>The game's own towns and places on the map: a failure in there switches off only the towns layer
        /// (PlacesError says why; 'topcam places on' tries again). Returns how many labels the layout now holds.</summary>
        static int GamePlacesLayer(GameHost host, in MapView v, float rw, float rh)
        {
            try
            {
                return GamePlaces(host, v, rw, rh);
            }
            catch (Exception e)
            {
                Layers = MapLayers.Set(Layers, MapLayers.Towns, false);
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
            if (!(w > 0) || !(h > 0) || !Fits(x, y, w, h)) return;
            float r = h * 0.42f, cy = y + h * 0.5f;
            Shape(Line, x + r * 0.6f, cy, x + w - r * 0.6f, cy, h * 0.86f, PalBack, 0f);
        }

        /// <summary>A red dot on every character the game counts as hostile to the player or fighting the player, awake
        /// and alive, on the picture.</summary>
        static void Foes(GameHost host, in MapView v, float rw, float rh)
        {
            int drawn = 0;
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
                Layers = MapLayers.Set(Layers, MapLayers.Cone, false);
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

        /// <summary>The routes (the "route" layer): where each selected man walks, as the game plans it (the path from his next
        /// waypoint, which the game hands over for selected people only): dotted legs from where he stands to the end, its far,
        /// provisional part fainter, and a ring on the end. A group walking together gets one line (RoutePlan). At most
        /// <see cref="MaxRouteShapes"/> shapes. A failure here switches off only the routes.</summary>
        static void RouteLayer(in MapView v, float rw, float rh)
        {
            try
            {
                Routes(v, rw, rh);
            }
            catch (Exception e)
            {
                Layers = MapLayers.Set(Layers, MapLayers.Route, false);
                RouteError = e.GetType().Name + ": " + e.Message;
                Log.Error("topcam: the routes switched off after an error (the other marks stay): " + e);
            }
        }

        static void Routes(in MapView v, float rw, float rh)
        {
            var plan = Route;
            plan.BeginFrame();
            int limit = _limit;
            _limit = Math.Min(limit, ShapeCount + MaxRouteShapes);
            try
            {
                for (int i = 0; i < Pack.Count && ShapeCount + 3 <= _limit; i++)
                {
                    var c = Pack.Who[i];
                    if (c == null || !c.Selected) continue;
                    var path = c.Path;
                    if (path == null || path.Count == 0) continue;
                    v.ToPixel(Pack.X[i], Pack.Y[i], Pack.Z[i], rw, rh, out float sx, out float sy);
                    plan.Start(sx, sy);
                    int exact = c.PathExact;
                    for (int j = 0; j < path.Count; j++)
                    {
                        var p = path[j];
                        double y = float.IsFinite(p.Y) ? p.Y : Ground(v, p.X, p.Z);
                        v.ToPixel(p.X, y, p.Z, rw, rh, out float px, out float py);
                        plan.Add(px, py, j >= exact);
                    }
                    if (!plan.Finish()) continue;
                    // the dots are counted from the end, so they stand still on the ground while he walks
                    float along = 0f;
                    for (int j = plan.Count - 1; j >= 1; j--)
                    {
                        float x0 = plan.X[j - 1], y0 = plan.Y[j - 1], x1 = plan.X[j], y1 = plan.Y[j];
                        Shape(Dots, x0, y0, x1, y1, 2f, plan.Far[j] ? PalRouteFar : PalRoute, along);
                        along += MathF.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0));
                    }
                    float ex = plan.X[plan.Count - 1], ey = plan.Y[plan.Count - 1];
                    Shape(Ring, ex, ey, 5.5f, 1.8f, 0f, PalRoute, 0f);
                    Shape(Disc, ex, ey, 1.8f, 0f, 0f, PalRoute, 0f);
                    LastRoutes++;
                }
            }
            finally
            {
                _limit = limit;
            }
            LastRoutesSkipped = plan.Skipped;
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

        /// <summary>Your people's layout (drawn later by PeopleDraw): dots always (the active squad's white, its leader gold, the
        /// other squads' grey-blue and a little smaller); names (with their leader lines) for the active squad and anyone
        /// selected while the names layer is on; heading arrows while the arrows layer is on (a failure there switches off only
        /// the arrows); how hurt each is while the hurt layer is on.</summary>
        static void PeopleLayout(in MapView v, float rw, float rh, int lay, double gameSeconds)
        {
            var layout = Layout;
            bool names = MapLayers.On(lay, MapLayers.Names), arrows = MapLayers.On(lay, MapLayers.Arrows), hurt = MapLayers.On(lay, MapLayers.Hurt);
            int n = Math.Min(Pack.Count, MaxPeople), k = 0, labels = 0;
            bool squads = Pack.Squad > 0;
            if (arrows)
            {
                try { Walk.BeginFrame(); }
                catch (Exception e) { arrows = ArrowsFail(e); }
            }
            int others = 0;
            for (int i = 0; i < n; i++)
            {
                var c = Pack.Who[i];
                if (c == null) continue;
                bool walking = false;
                double hx = 0, hz = 0;
                if (arrows)
                {
                    try { walking = Walk.Step(Pack.KeyOf(c), Pack.X[i], Pack.Z[i], gameSeconds, out hx, out hz); }      // everyone, on the picture or not
                    catch (Exception e) { arrows = ArrowsFail(e); }
                }
                v.ToPixel(Pack.X[i], Pack.Y[i], Pack.Z[i], rw, rh, out float px, out float py);
                if (!Inside(px, py, rw, rh, -8f)) continue;
                bool inSquad = !squads || i < Pack.Squad;
                if (!inSquad) others++;
                _packOf[k] = i;
                (_dotX[k], _dotY[k], _dotR[k]) = (px, py, i == 0 ? 6f : inSquad ? 5f : 4f);
                (_walking[k], _walkX[k], _walkZ[k]) = (walking, hx, hz);
                _hurtOf[k] = hurt && c.Body is { } body ? MapShape.HurtLevel(body.Blood, body.WorstPart) : 0;
                _labelOf[k] = -1;
                if (names && (inSquad || c.Selected) && labels < LabelLayout.Max)
                {
                    string caption = Caption(c.Name);
                    Size(caption, out float w, out float h);
                    _captionOf[labels] = caption;
                    layout.Key[labels] = Pack.KeyOf(c);
                    (layout.DotX[labels], layout.DotY[labels], layout.DotR[labels], layout.W[labels], layout.H[labels]) = (px, py, _dotR[k], w, h);
                    _labelOf[k] = labels++;
                }
                k++;
            }
            if (arrows)
            {
                try { Walk.EndFrame(); }
                catch (Exception e) { arrows = ArrowsFail(e); }
            }
            _people = k;
            layout.Count = labels;
            if (labels > 0) layout.Solve(rw, rh);
            LastSquad = k - others;
            LastOthers = others;
            (_sqNames, _sqArrows, _sqHurt) = (names && labels > 0, arrows, hurt);
        }

        /// <summary>Your people as PeopleLayout placed them (the layout comes first so the place labels can keep off it):
        /// leader lines, arrows, hurt rings and selection rings first, then the dots with each name on a dark bar. The dots and
        /// the names are what matter most: the others stop short of the shapes they need.</summary>
        static void PeopleDraw(in MapView v, float rw, float rh)
        {
            var layout = Layout;
            bool names = _sqNames, arrows = _sqArrows, hurt = _sqHurt;
            int k = _people;
            int must = k + (names ? layout.Count : 0);
            _limit = Math.Max(ShapeCount, MaxShapes - must);

            if (names)
                for (int j = 0; j < layout.Count; j++)      // leader lines under everything else
                {
                    if (!layout.Stacked[j]) continue;
                    float dx = layout.LeadX[j] - layout.DotX[j], dy = layout.LeadY[j] - layout.DotY[j], len = MathF.Sqrt(dx * dx + dy * dy);
                    float from = layout.DotR[j] + 1.5f;
                    if (len <= from + 2f) continue;
                    if (!Fits(layout.X[j], layout.Y[j], layout.W[j], layout.H[j])) continue;      // its name is not shown
                    Shape(Line, layout.DotX[j] + dx / len * from, layout.DotY[j] + dy / len * from, layout.LeadX[j], layout.LeadY[j], 1.3f, PalLead, 0f);
                }
            if (arrows)
                for (int j = 0; j < k; j++)      // under the dots
                    if (_walking[j]) Arrow(v, _dotX[j], _dotY[j], _dotR[j], _walkX[j], _walkZ[j], DotPalette(j), rw, rh);
            for (int j = 0; j < k; j++)
            {
                int level = hurt ? _hurtOf[j] : 0;
                if (level > 0)
                {
                    Shape(Ring, _dotX[j], _dotY[j], _dotR[j] + 3f, 2.2f, 0f, level == 2 ? PalHurtBad : PalHurt, 0f);
                    LastHurt++;
                }
                if (Pack.Who[_packOf[j]].Selected)
                    Shape(Ring, _dotX[j], _dotY[j], _dotR[j] + (level > 0 ? 7f : 4.5f), 2f, 0f, PalRing, 0f);
            }
            _limit = MaxShapes;
            for (int j = k - 1; j >= 0; j--)      // the first (the leader) last: on top
            {
                var c = Pack.Who[_packOf[j]];
                bool out_ = c.Body is { } body && (body.Unconscious || body.Coma);
                Shape(out_ ? Hollow : Disc, _dotX[j], _dotY[j], _dotR[j], 2.2f, 0f, DotPalette(j), 0f);
                int l = _labelOf[j];
                if (names && l >= 0 && !string.IsNullOrEmpty(_captionOf[l]))
                {
                    Backing(layout.X[l], layout.Y[l], layout.W[l], layout.H[l]);
                    Text(_captionOf[l], layout.X[l], layout.Y[l], layout.W[l], layout.H[l], IsLeader(j) ? StyleLeader : StyleName);
                }
            }
        }

        static bool IsLeader(int j) => _packOf[j] == 0;      // the active squad's leader (the first of everybody when no squad is named)

        static float DotPalette(int j) => IsLeader(j) ? PalLeader : Pack.Squad == 0 || _packOf[j] < Pack.Squad ? PalSquad : PalOther;

        static bool ArrowsFail(Exception e)
        {
            Layers = MapLayers.Set(Layers, MapLayers.Arrows, false);
            ArrowError = e.GetType().Name + ": " + e.Message;
            Log.Error("topcam: the heading arrows switched off after an error (the other marks stay): " + e);
            try { Walk.Clear(); } catch { }
            return false;
        }

        /// <summary>A name as a caption (CaptionText.Escape), one object per name so a still map hands the HUD the same texts
        /// every frame.</summary>
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

        /// <summary>Switches one layer, clearing the error that switched it off before when it comes back on.</summary>
        public static void SetLayer(int layer, bool on)
        {
            if (on && !MapLayers.On(Layers, layer))
            {
                if (layer == MapLayers.Cone) ConeError = null;
                if (layer == MapLayers.Arrows) ArrowError = null;
                if (layer == MapLayers.Route) RouteError = null;
            }
            if (on && layer == MapLayers.Towns) PlacesError = null;      // 'places on' always tries again
            Layers = MapLayers.Set(Layers, layer, on);
        }

        /// <summary>The marks' commands (a[0] is the word after "topcam"). Null: not one of them; "": done; else the refusal.</summary>
        public static string Command(IReadOnlyList<string> a)
        {
            switch (a[0].ToLowerInvariant())
            {
                case "marks":
                {
                    if (a.Count != 2 || !MapPrefs.OnOff(a[1], out bool on)) return "usage: topcam marks on|off";
                    if (on)
                    {
                        Error = null;
                        Layout.Forget();
                        PlaceSpots.Forget();
                        On = true;
                    }
                    else
                    {
                        On = false;
                        ShapeCount = 0;
                        lock (Gate) _frontCount = 0;
                    }
                    return "";
                }
                case "hostiles":
                {
                    // the same switch as 'topcam layer foes'
                    if (a.Count != 2 || !MapPrefs.OnOff(a[1], out bool on)) return "usage: topcam hostiles on|off   (the red dots of characters hostile to you; = topcam layer foes)";
                    SetLayer(MapLayers.Foes, on);
                    return "";
                }
                case "places":
                {
                    // the same switch as 'topcam layer towns'
                    if (a.Count != 2 || !MapPrefs.OnOff(a[1], out bool on))
                        return "usage: topcam places on|off   (the names of the towns and places you have discovered; = topcam layer towns)";
                    SetLayer(MapLayers.Towns, on);
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
                    // the point it looks at; the picture's top edge on the ground caps it) and how strong its inside is
                    const string usage = "usage: topcam cone [on|off] [reach <0.5..8>] [fill <0..1>]   (the view cone; also: topcam layer cone on|off)";
                    int from = 1;
                    bool? coneOn = null;
                    if (a.Count >= 2 && MapPrefs.OnOff(a[1], out bool switched))
                    {
                        coneOn = switched;
                        from = 2;
                    }
                    if ((a.Count - from) % 2 != 0) return usage;
                    double reach = ConeReach, fill = Palette[(int)PalConeFill * 4 + 3];
                    for (int i = from; i < a.Count; i += 2)
                    {
                        if (!Num(a, i + 1, out double val)) return usage;
                        if (a[i].Equals("reach", StringComparison.OrdinalIgnoreCase) && val >= ViewCone.MinReach && val <= ViewCone.MaxReach) reach = val;
                        else if (a[i].Equals("fill", StringComparison.OrdinalIgnoreCase) && val >= 0 && val <= 1) fill = val;
                        else return usage;
                    }
                    if (coneOn is bool c) SetLayer(MapLayers.Cone, c);
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
                    for (int layer = 0; layer < MapLayers.Count; layer++) SetLayer(layer, MapLayers.On(mask, layer));
                    return "";
                }
                default:
                    return null;
            }
        }

        public static void Status(StringBuilder sb)
        {
            sb.Append("{\"on\":").Append(On ? "true" : "false");
            sb.Append(",\"squad\":").Append(LastSquad).Append(",\"others\":").Append(LastOthers).Append(",\"hostiles\":").Append(LastHostiles);
            sb.Append(",\"shapes\":").Append(ShapeCount).Append(",\"texts\":").Append(LastTexts);
            sb.Append(",\"textScale\":").Append(MapMath.Num(TextScale, "0.##"));
            // the layers, the cone of the last frame, the arrows, routes and hurt rings drawn in it
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
            sb.Append(",\"error\":").Append(MapMath.JsonText(ConeError)).Append('}');
            // the place labels of the last frame: drawn, and dropped for want of room round your people
            sb.Append(",\"placeTexts\":{\"shown\":").Append(LastPlacesShown).Append(",\"dropped\":").Append(LastPlacesDropped).Append('}');
            // the game's own towns and places: how many were on the last picture, and why the layer went off if it did
            sb.Append(",\"places\":{\"shown\":").Append(LastGamePlaces).Append(",\"error\":").Append(MapMath.JsonText(PlacesError)).Append('}');
            sb.Append(",\"arrows\":{\"drawn\":").Append(LastArrows).Append(",\"tracked\":").Append(Walk.Count)
              .Append(",\"error\":").Append(MapMath.JsonText(ArrowError)).Append('}');
            sb.Append(",\"routes\":{\"drawn\":").Append(LastRoutes).Append(",\"same\":").Append(LastRoutesSkipped)
              .Append(",\"error\":").Append(MapMath.JsonText(RouteError)).Append('}');
            sb.Append(",\"hurt\":").Append(LastHurt);
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
