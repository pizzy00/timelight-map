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
        public static double CentreX, CentreY, CentreZ, Spread;

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
            Spread = 0;
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
            Spread = 0;
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
                    if (HasCentre) Spread = MapMath.Spread(_x, _z, m, anchor, Near, Far, CentreX, CentreZ);
                    return;
                }
            }
            Followed = Squad > 0 ? Squad : Count;
            HasCentre = MapMath.Centroid(X, Y, Z, Followed, 0, Near, Far, out CentreX, out CentreY, out CentreZ);
            if (HasCentre) Spread = MapMath.Spread(X, Z, Followed, 0, Near, Far, CentreX, CentreZ);
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
        public const int MaxShapes = 256, MaxPeople = 48, MaxHostiles = 48, MaxTraders = 24, MaxText = 96, MaxRouteShapes = 72;

        // shape kinds (the shader's; it also knows 7, a plain filled triangle): 8 is a triangle that fades away from its first corner
        const float Disc = 0, Hollow = 1, Ring = 2, Diamond = 3, Line = 4, Dots = 5, Triangle = 6, TriFade = 8;
        // palette slots (the shader's palette has 32; the rest stay empty)
        const float PalSquad = 0, PalLeader = 1, PalHostile = 2, PalRing = 3, PalLead = 4, PalChrome = 5, PalPlace = 6,
            PalConeFill = 7, PalConeEdge = 8, PalConeArc = 9, PalBack = 10, PalOther = 11, PalRoute = 12, PalRouteFar = 13,
            PalHurt = 14, PalHurtBad = 15, PalTrader = 16;
        const float BackAlpha = 0.74f;          // the dark pill behind each text on the map (so it reads on bright ground)
        public const int PaletteSlots = 32;
        public const float ConeMinPx = 30f;     // the shortest view cone on the map, pixels
        public const float ArrowReach = 10f;    // a heading arrow's tip stands this far out from the edge of the dot
        // text styles
        const byte StyleName = 0, StyleLeader = 1, StylePlace = 2, StyleChrome = 3, StyleTrader = 4, StyleFoe = 5;

        public const string Font = "Kenshi_StandardFont_Medium";

        // ---- settings (commands) ----
        public static bool On;
        public static string PlacesError;               // a failure there switches off only the towns layer
        public static int LastGamePlaces;
        public static string TradersError;              // a failure there switches off only the traders layer
        public static int LastTraders;
        public static FoeLabelMode FoeLabels = FoeLabelMode.Auto;
        public static int LastFoeLabels;
        public static bool ShowSquadBars, ShowClock, ShowCats, ShowSpeed;
        public static string HudError;
        public static string Error;
        public static float TextScale = 1f;
        public static int Layers = MapLayers.All;      // topcam layer <name> on|off
        public static double ConeReach = ViewCone.DefaultReach;      // topcam cone reach <x>
        public static string ConeError, ArrowError, RouteError;     // a failure there switches off only that layer

        static readonly Dictionary<string, string> Captions = new Dictionary<string, string>(StringComparer.Ordinal);

        // ---- 'topcam waypoints': a route handed in by command (world x z per point, the end last), drawn on the route layer
        public const int MaxWaypoints = 400, MaxWaypointShapes = 120, MaxWaypointDots = 56;
        sealed class WaypointSet
        {
            public double[] X, Z;
            public string Name;
            public long Stamp;
        }
        static volatile WaypointSet _waypoints;      // swapped whole by the command; the frame reads one reference
        static long _waypointStamp;
        static readonly float[] _wpX = new float[MaxWaypoints + 1], _wpY = new float[MaxWaypoints + 1];
        public static int LastWaypointsDrawn, LastWaypointDots;
        public static string WaypointError;

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
                0.90f, 0.93f, 0.98f, 0.40f,   // 4 leader lines (subtle so crowded clusters stay readable)
                1.00f, 1.00f, 1.00f, 0.92f,   // 5 scale bar, north
                0.98f, 0.92f, 0.74f, 0.95f,   // 6 place marks
                1.00f, 0.82f, 0.48f, 0.18f,   // 7 the camera's view cone, inside (it fades to a third of this at its far edge)
                1.00f, 0.82f, 0.48f, 0.95f,   // 8 its two sides (dotted)
                1.00f, 0.82f, 0.48f, 0.70f,   // 9 its far edge (dotted)
                0.02f, 0.02f, 0.03f, BackAlpha, // 10 the dark pill behind a text
                0.74f, 0.78f, 0.84f, 0.95f,   // 11 the player's people in other squads: grey-blue
                0.50f, 0.86f, 1.00f, 0.95f,   // 12 a route (dotted) and its end
                0.50f, 0.86f, 1.00f, 0.45f,   // 13 a route's provisional, long-range part
                1.00f, 0.58f, 0.12f, 1f,      // 14 hurt: orange ring
                0.95f, 0.13f, 0.10f, 1f,      // 15 badly hurt: red ring
                0.24f, 0.94f, 0.58f, 1f,      // 16 moving trader / trade caravan: emerald green
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
        static readonly Dictionary<string, string> EmptyProps = new Dictionary<string, string>();
        static readonly Dictionary<(int Alpha, string Colour, string Align), Dictionary<string, string>> BarStyles =
            new Dictionary<(int, string, string), Dictionary<string, string>>();
        static TerrainRenderer _terrain;
        static byte _alpha = 100;
        static bool _round;                 // this frame's map is round: texts stay inside its circle
        static float _rw, _rh, _radius;

        struct SquadBarsState
        {
            public bool Active, HasOrganic;
            public float HpPct, FedPct, RawHunger, RawHudFed;
            public int DownCount, MemberCount;
        }
        static SquadBarsState _bars;

        /// <summary>The text styles at an alpha (hundredths): name, leader, place, scale and N, moving trader, hostile group.</summary>
        static Dictionary<string, string>[] StylesAt(int alpha)
        {
            alpha = Math.Clamp(alpha, 0, 100);
            return StylesByAlpha[alpha] ??= new[]
            {
                Style("0.98 0.98 0.96", "Center", alpha),
                Style("1 0.85 0.40", "Center", alpha),
                Style("1 0.93 0.68", "Center", alpha),
                Style("1 1 1", "Center", alpha),
                Style("0.45 1 0.72", "Center", alpha),
                Style("1 0.44 0.38", "Center", alpha),
            };
        }

        static Dictionary<string, string> BarStyle(string colour, string align, int alpha)
        {
            alpha = Math.Clamp(alpha, 0, 100);
            var key = (alpha, colour, align);
            if (BarStyles.TryGetValue(key, out var s)) return s;
            if (BarStyles.Count > 256) BarStyles.Clear();
            return BarStyles[key] = Style(colour, align, alpha);
        }

        static readonly LabelLayout Layout = new LabelLayout();
        static readonly PlaceLayout PlaceSpots = new PlaceLayout();      // where the place, trader and foe labels' texts go (round your people)
        static readonly FoeClusterer FoeGroups = new FoeClusterer();
        static readonly float[] _foeX = new float[MaxHostiles], _foeY = new float[MaxHostiles];
        static readonly bool[] _foeFight = new bool[MaxHostiles];
        static int _foeCount;
        static readonly RoutePlan Route = new RoutePlan();
        static readonly string[] _placeText = new string[PlaceLayout.Max];
        static readonly byte[] _placeStyle = new byte[PlaceLayout.Max];
        static readonly Dictionary<string, string> PlaceCaptions = new Dictionary<string, string>(StringComparer.Ordinal);
        static readonly Heading TraderWalk = new Heading();
        static readonly HashSet<int> _seenTraderSquads = new HashSet<int>();
        static readonly HashSet<int> _hostileSquads = new HashSet<int>();
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
                LastArrows = LastRoutes = LastRoutesSkipped = LastHurt = LastWaypointsDrawn = LastWaypointDots = 0;
                if (v.Valid && rw >= 16 && rh >= 16 && v.Span > 0 && v.SpanZ > 0)
                {
                    int lay = Layers;
                    int people = Math.Min(Pack.Count, MaxPeople);
                    int each = 4 + (MapLayers.On(lay, MapLayers.Arrows) ? 2 : 0) + (MapLayers.On(lay, MapLayers.Hurt) ? 1 : 0);
                    _limit = MaxShapes - Math.Min(people * each, MaxShapes / 2);
                    PeopleLayout(v, rw, rh, lay, gameSeconds);      // first, so the place labels can keep off the names and dots
                    if (MapLayers.On(lay, MapLayers.Cone)) ConeLayer(cam, v, rw, rh);
                    if (MapLayers.On(lay, MapLayers.Route)) RouteLayer(v, rw, rh);
                    if (MapLayers.On(lay, MapLayers.Route) && _waypoints != null && WaypointError == null) WaypointLayer(v, rw, rh);
                    if (MapLayers.On(lay, MapLayers.Scale)) ScaleBar(v, rw, rh);
                    if (MapLayers.On(lay, MapLayers.North)) North(v, rw, rh);
                    if (ShowClock || ShowCats || ShowSpeed) HudInfoLayer(host, rw, rh, lay);
                    if (MapLayers.On(lay, MapLayers.Foes)) FoesCollect(host, v, rw, rh);
                    else { _foeCount = 0; LastHostiles = 0; LastFoeLabels = 0; FoeGroups.Clear(); }
                    Places(host, v, rw, rh, lay, gameSeconds);
                    if (MapLayers.On(lay, MapLayers.Foes)) FoesDraw();
                    _limit = MaxShapes;
                    PeopleDraw(v, rw, rh);
                    if (ShowSquadBars) ReadSquadBarsLayer(host);
                    else _bars.Active = false;
                }
                else _bars.Active = false;
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

        static bool Inside(float px, float py, float rw, float rh, float inset)
        {
            if (px < inset || py < inset || px > rw - inset || py > rh - inset) return false;
            if (!_round) return true;
            float r = _radius - inset;
            if (!(r > 0f)) return false;
            float dx = px - rw * 0.5f, dy = py - rh * 0.5f;
            return dx * dx + dy * dy <= r * r;
        }

        /// <summary>Where the scale bar starts and stands: the bottom left; on a round map the bottom middle (bar and text
        /// centred together, nudged up slightly when Game speed sits underneath it).</summary>
        static void ScaleSpot(float rw, float rh, float len, float textW, out float x0, out float y)
        {
            if (_round)
            {
                float frac = ShowSpeed ? 0.74f : 0.80f;
                (x0, y) = (rw * 0.5f - (len + 7f + textW) * 0.5f, rh * 0.5f + _radius * frac);
                return;
            }
            (x0, y) = (12f, rh - 13f);
        }

        /// <summary>Reserves the bottom chrome band (scale bar, optional clock/cats/speed) in both squad and place layouts.</summary>
        static void AddBottomChromeObstacle(Action<float, float, float, float> add, float rw, float rh, int lay)
        {
            bool scale = MapLayers.On(lay, MapLayers.Scale);
            bool info = ShowClock || ShowCats;
            if (_round)
            {
                if (!scale && !info && !ShowSpeed) return;
                float frac = ShowSpeed ? 0.74f : 0.80f;
                float topY = rh * 0.5f + _radius * frac - 20f;
                if (info) topY -= _lineGuess + 6f;
                add(rw * 0.12f, topY, rw * 0.88f, rh);
                return;
            }
            if (scale || info)
            {
                float topY = info ? rh - (scale ? 34f : 12f) - _lineGuess - 6f : rh - 34f;
                float maxX = info ? rw * 0.84f : rw * 0.42f;
                add(0f, topY, maxX, rh);
            }
            if (ShowSpeed) add(rw * 0.64f, rh - 34f, rw, rh);
        }

        /// <summary>Where the N arrow stands and points: the top right; on a round map on the rim, 40 degrees right of the top.
        /// Shifted inward only if a non-zero yaw would push the "N" text off the edge or out of the circle.</summary>
        static void NorthSpot(in MapView v, float rw, float rh, out float cx, out float cy, out float nx, out float ny)
        {
            v.DirToPixel(0, -1, rw, rh, out nx, out ny);
            float len = MathF.Sqrt(nx * nx + ny * ny);
            if (len > 1e-6f) (nx, ny) = (nx / len, ny / len);
            else (nx, ny) = (0f, -1f);
            if (_round)
            {
                float r = MathF.Max(0f, _radius - 20f), a = 40f * MathF.PI / 180f;
                (cx, cy) = (rw * 0.5f + MathF.Sin(a) * r, rh * 0.5f - MathF.Cos(a) * r);
            }
            else (cx, cy) = (rw - 17f, 19f);

            Size("N", out float tw, out float th);
            float tx = cx - nx * 15f - tw * 0.5f, ty = cy - ny * 15f - th * 0.5f;
            if (_round)
            {
                if (!Fits(tx, ty, tw, th))
                {
                    float r = MathF.Max(0f, _radius - 32f - MathF.Max(tw, th) * 0.5f), a = 40f * MathF.PI / 180f;
                    (cx, cy) = (rw * 0.5f + MathF.Sin(a) * r, rh * 0.5f - MathF.Cos(a) * r);
                }
            }
            else
            {
                if (tx < 2f) cx += 2f - tx;
                else if (tx + tw > rw - 2f) cx -= tx + tw - (rw - 2f);
                if (ty < 2f) cy += 2f - ty;
                else if (ty + th > rh - 2f) cy -= ty + th - (rh - 2f);
            }
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
            float tx = x0 + len + 7f, ty = y - th * 0.5f - 2f;
            Backing(tx, ty, tw, th);
            Text(_scaleText, tx, ty, tw, th, StyleChrome);
        }

        static void North(in MapView v, float rw, float rh)
        {
            NorthSpot(v, rw, rh, out float cx, out float cy, out float nx, out float ny);
            Shape(Triangle, cx + nx * 8f, cy + ny * 8f, nx, ny, 15f, PalChrome, 5.5f);
            Size("N", out float tw, out float th);
            float tx = cx - nx * 15f - tw * 0.5f, ty = cy - ny * 15f - th * 0.5f;
            Backing(tx, ty, tw, th);
            Text("N", tx, ty, tw, th, StyleChrome);
        }

        /// <summary>Optional show-style map widgets: Day &amp; Clock, Cats, and Game Speed. Never breaks other marks.</summary>
        static void HudInfoLayer(GameHost host, float rw, float rh, int lay)
        {
            try
            {
                HudInfo(host, rw, rh, lay);
            }
            catch (Exception e)
            {
                HudError = e.GetType().Name + ": " + e.Message;
                ShowClock = ShowCats = ShowSpeed = false;
                Log.Error("topcam: map HUD info (clock/cats/speed) switched off after an error (the other marks stay): " + e);
            }
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        static void HudInfo(GameHost host, float rw, float rh, int lay)
        {
            var session = host.Session;
            var world = session?.World;
            bool scaleOn = MapLayers.On(lay, MapLayers.Scale);
            float scaleY = _round ? rh * 0.5f + _radius * (ShowSpeed ? 0.74f : 0.80f) : rh - 13f;

            if (ShowClock || ShowCats)
            {
                string clockLong = "", clockShort = "";
                if (ShowClock)
                {
                    if (session?.Clock is { } gc)
                    {
                        clockLong = MapMath.FormatClock(gc.Day, gc.Hour, gc.Minute, includeTimeWord: true);
                        clockShort = MapMath.FormatClock(gc.Day, gc.Hour, gc.Minute, includeTimeWord: false);
                    }
                    else if (world != null)
                    {
                        clockLong = MapMath.FormatClock(world.GameHours, includeTimeWord: true);
                        clockShort = MapMath.FormatClock(world.GameHours, includeTimeWord: false);
                    }
                }
                string catsText = ShowCats && world?.Player != null ? MapMath.FormatCats(world.Player.Money) : "";
                string line = (!string.IsNullOrEmpty(clockLong) && !string.IsNullOrEmpty(catsText))
                    ? clockLong + "  " + catsText
                    : !string.IsNullOrEmpty(clockLong) ? clockLong : catsText;
                if (!string.IsNullOrEmpty(line))
                {
                    Size(line, out float tw, out float th);
                    tw += 14f;
                    float ty = scaleOn ? scaleY - th - 10f : (_round ? scaleY - th * 0.5f : rh - th - 6f);
                    float tx = _round ? rw * 0.5f - tw * 0.5f : 12f;
                    // On narrow round maps, drop the word "TIME" if the combined Day + Time + Cats pill would clip the rim.
                    if (_round && !Fits(tx, ty, tw, th) && !string.IsNullOrEmpty(clockShort))
                    {
                        string compact = !string.IsNullOrEmpty(catsText) ? clockShort + "  " + catsText : clockShort;
                        Size(compact, out float cw, out float ch);
                        cw += 14f;
                        float cx = rw * 0.5f - cw * 0.5f;
                        if (Fits(cx, ty, cw, ch)) (line, tx, tw, th) = (compact, cx, cw, ch);
                    }
                    if (Fits(tx, ty, tw, th))
                    {
                        Backing(tx, ty, tw, th);
                        Text(line, tx, ty, tw, th, StyleChrome);
                    }
                }
            }

            if (ShowSpeed && session?.Simulation is { } sim)
            {
                string sp = MapMath.FormatSpeed(sim.IsPaused, sim.Speed);
                if (!string.IsNullOrEmpty(sp))
                {
                    Size(sp, out float sw, out float sh);
                    sw += 10f;
                    float sx = _round ? rw * 0.5f - sw * 0.5f : rw - 12f - sw;
                    float sy = _round ? scaleY + (scaleOn ? 8f : -sh * 0.5f) : rh - 13f - sh * 0.5f - 2f;
                    if (Fits(sx, sy, sw, sh))
                    {
                        Backing(sx, sy, sw, sh);
                        Text(sp, sx, sy, sw, sh, StyleChrome);
                    }
                }
            }
        }

        /// <summary>Computes the active squad's average HP %, Down count, and organic Fed % for the docked bar panel.</summary>
        static void ReadSquadBarsLayer(GameHost host)
        {
            try
            {
                ReadSquadBars(host);
            }
            catch (Exception e)
            {
                HudError = e.GetType().Name + ": " + e.Message;
                ShowSquadBars = false;
                _bars.Active = false;
                Log.Error("topcam: Squad HP & Fed bars switched off after an error (the other marks stay): " + e);
            }
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        static void ReadSquadBars(GameHost host)
        {
            int count = Pack.Squad > 0 ? Pack.Squad : Pack.Count;
            if (count <= 0)
            {
                _bars.Active = false;
                return;
            }
            var chars = host?.Session?.World?.Characters;
            float hpSum = 0f, fedSum = 0f;
            float firstHunger = float.NaN, firstHudFed = float.NaN;
            int hpCount = 0, fedCount = 0, down = 0;
            for (int i = 0; i < count; i++)
            {
                var c = Pack.Who[i];
                if (c == null) continue;
                var body = c.Body;
                if (body is { Unconscious: true } or { Coma: true }) down++;

                // Prefer full limb HP from World.Characters.Cold(id).Medical.Parts (available for every squad member,
                // not only the currently selected one), falling back to CharacterSnapshot.HealthParts -> BodyStatus.
                float memberHp = -1f;
                bool isRobot = c.Hud is { Robot: true };
                float hunger = body is { } bh ? bh.Hunger : float.NaN;
                if (chars != null)
                {
                    try
                    {
                        if (chars.IsAlive(c.Id))
                        {
                            var cold = chars.Cold(c.Id.Slot);
                            var med = cold?.Medical;
                            if (med != null)
                            {
                                var medParts = med.Parts;
                                if (medParts != null && medParts.Count > 0)
                                {
                                    float cur = 0f, max = 0f;
                                    for (int p = 0; p < medParts.Count; p++)
                                    {
                                        var part = medParts[p];
                                        if (part == null || !(part.MaxHp > 0f) || !float.IsFinite(part.MaxHp)) continue;
                                        float fl = float.IsFinite(part.Flesh) ? MathF.Min(part.Flesh, part.MaxHp) : 0f;
                                        cur += fl;
                                        max += part.MaxHp;
                                    }
                                    if (max > 0f) memberHp = Math.Clamp(cur / max * 100f, 0f, 100f);
                                }
                                if (!float.IsFinite(hunger) && float.IsFinite(med.Hunger)) hunger = med.Hunger;
                            }
                            if (!isRobot && cold?.Race != null)
                            {
                                string rname = cold.Race.Name;
                                if (!string.IsNullOrEmpty(rname) && rname.IndexOf("skeleton", StringComparison.OrdinalIgnoreCase) >= 0)
                                    isRobot = true;
                            }
                        }
                    }
                    catch { }
                }

                if (memberHp < 0f)
                {
                    var parts = c.HealthParts;
                    if (parts != null && parts.Count > 0)
                    {
                        float cur = 0f, max = 0f;
                        for (int p = 0; p < parts.Count; p++)
                        {
                            var part = parts[p];
                            if (!(part.Maximum > 0f) || !float.IsFinite(part.Maximum)) continue;
                            float fl = float.IsFinite(part.Flesh) ? MathF.Min(part.Flesh, part.Maximum) : 0f;
                            cur += fl;
                            max += part.Maximum;
                        }
                        if (max > 0f) memberHp = Math.Clamp(cur / max * 100f, 0f, 100f);
                    }
                }
                if (memberHp < 0f)
                {
                    if (body is { } b)
                    {
                        float b1 = float.IsFinite(b.Blood) ? b.Blood : 1f;
                        float b2 = float.IsFinite(b.WorstPart) ? b.WorstPart : 1f;
                        memberHp = Math.Clamp(MathF.Min(b1, b2), 0f, 1f) * 100f;
                    }
                    else memberHp = 100f;
                }
                hpSum += memberHp;
                hpCount++;

                if (!isRobot)
                {
                    var hud = c.Hud;
                    float hudFed = hud is { } hv ? hv.Fed : float.NaN;
                    if (!float.IsFinite(firstHunger) && float.IsFinite(hunger)) firstHunger = hunger;
                    if (!float.IsFinite(firstHudFed) && float.IsFinite(hudFed)) firstHudFed = hudFed;
                    float pct = MapMath.FedPercent(hunger);
                    if (pct < 0f && float.IsFinite(hudFed))
                        pct = hudFed <= 1.05f ? Math.Clamp(hudFed * 100f, 0f, 100f) : MapMath.FedPercent(hudFed);
                    if (pct >= 0f)
                    {
                        fedSum += pct;
                        fedCount++;
                    }
                }
            }
            if (hpCount == 0)
            {
                _bars.Active = false;
                return;
            }
            _bars = new SquadBarsState
            {
                Active = true,
                MemberCount = hpCount,
                DownCount = down,
                HpPct = Math.Clamp(hpSum / hpCount, 0f, 100f),
                HasOrganic = fedCount > 0,
                FedPct = fedCount > 0 ? Math.Clamp(fedSum / fedCount, 0f, 100f) : 100f,
                RawHunger = firstHunger,
                RawHudFed = firstHudFed,
            };
        }

        /// <summary>The place labels ("towns") and moving trade caravans ("traders"): discovered towns/places as cream
        /// diamonds, roaming trade caravans as emerald ringed diamonds (with heading arrows while moving). All texts are laid
        /// out together by PlaceLayout round your people's names, dots and leader lines, the scale bar, the N, each other and
        /// the marks: above the mark where there is room, else the nearest free spot with a thin leader line, else dropped; a
        /// dark pill behind each. On a round map every text stays inside the circle.</summary>
        static void Places(GameHost host, in MapView v, float rw, float rh, int lay, double gameSeconds)
        {
            var pl = PlaceSpots;
            pl.ClearObstacles();
            pl.CircleR = _round ? _radius : 0f;
            var layout = Layout;
            for (int j = 0; j < _people; j++)
            {
                float r = _dotR[j] + 3f;
                pl.AddObstacle(_dotX[j] - r, _dotY[j] - r, _dotX[j] + r, _dotY[j] + r);
            }
            if (_sqNames)
                for (int j = 0; j < layout.Count; j++)
                {
                    if (!(layout.W[j] > 0) || !Fits(layout.X[j], layout.Y[j], layout.W[j], layout.H[j])) continue;
                    // include the dark pill's horizontal caps so place labels never graze a squad name's pill
                    pl.AddObstacle(layout.X[j] - 4f, layout.Y[j], layout.X[j] + layout.W[j] + 4f, layout.Y[j] + layout.H[j]);
                    if (layout.Stacked[j])
                    {
                        float dx = layout.LeadX[j] - layout.DotX[j], dy = layout.LeadY[j] - layout.DotY[j], len = MathF.Sqrt(dx * dx + dy * dy);
                        float from = layout.DotR[j] + 1.5f;
                        if (len > from + 2f)
                            pl.AddLineObstacle(layout.DotX[j] + dx / len * from, layout.DotY[j] + dy / len * from, layout.LeadX[j], layout.LeadY[j]);
                    }
                }
            AddBottomChromeObstacle(pl.AddObstacle, rw, rh, lay);
            if (MapLayers.On(lay, MapLayers.North))
            {
                NorthSpot(v, rw, rh, out float nx, out float ny, out _, out _);
                pl.AddObstacle(nx - 24f, ny - 26f, nx + 24f, ny + 26f);
            }
            int n = MapLayers.On(lay, MapLayers.Towns) ? GamePlacesLayer(host, v, rw, rh) : 0;
            if (n == 0) LastGamePlaces = 0;
            if (MapLayers.On(lay, MapLayers.Traders)) TradersLayer(host, v, rw, rh, gameSeconds, MapLayers.On(lay, MapLayers.Arrows), ref n);
            else LastTraders = 0;
            if (MapLayers.On(lay, MapLayers.Foes) && FoeLabels != FoeLabelMode.Off) AddFoeLabels(ref n);
            else LastFoeLabels = 0;
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
                Text(_placeText[k], pl.X[k], pl.Y[k], pl.W[k], pl.H[k], _placeStyle[k]);
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
            _placeStyle[n] = StylePlace;
            n++;
        }

        /// <summary>Moving trade caravans (the "traders" layer): a failure here switches off only the traders layer
        /// (TradersError says why; 'topcam traders on' tries again).</summary>
        static void TradersLayer(GameHost host, in MapView v, float rw, float rh, double gameSeconds, bool arrows, ref int labelCount)
        {
            try
            {
                Traders(host, v, rw, rh, gameSeconds, arrows, ref labelCount);
            }
            catch (Exception e)
            {
                Layers = MapLayers.Set(Layers, MapLayers.Traders, false);
                LastTraders = 0;
                TradersError = e.GetType().Name + ": " + e.Message;
                Log.Error("topcam: the moving traders switched off after an error (the other marks stay): " + e);
                try { TraderWalk.Clear(); } catch { }
            }
        }

        /// <summary>Roaming / moving traders (trade caravans, nomad animal traders, faction caravans) on the map, excluding
        /// stationary town shopkeepers (whose squad is town-owned or tied to a shop building) and hostile squads. Draws a
        /// bright emerald ringed diamond, a heading arrow while walking, and its label in PlaceSpots. Never inlined: reads
        /// World.Squads, FactionRelations and CharacterSnapshot.IsTrader inside TradersLayer's try.</summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        static void Traders(GameHost host, in MapView v, float rw, float rh, double gameSeconds, bool arrows, ref int labelCount)
        {
            int drawn = 0;
            _seenTraderSquads.Clear();
            _hostileSquads.Clear();
            TraderWalk.BeginFrame();
            try
            {
                var session = host.Session;
                var snapshot = session?.CurrentSnapshot;
                var squads = session?.World?.Squads;
                var all = snapshot?.Characters;
                if (all != null)
                {
                    for (int i = 0; i < all.Count; i++)
                    {
                        var c = all[i];
                        if (c == null || c.IsPlayer) continue;
                        if (c.HostileToPlayer || c.FightsPlayer)
                        {
                            if (c.SquadId != 0) _hostileSquads.Add(c.SquadId);
                            continue;
                        }
                        if (drawn >= MaxTraders || !c.IsTrader) continue;
                        if (c.Body is { } body && (body.Dead || body.Unconscious)) continue;

                        Meitou.Simulation.Factions.Squad sq = null;
                        if (squads != null && c.SquadId != 0)
                        {
                            try { sq = squads.Find(c.SquadId); } catch { sq = null; }
                        }
                        if (sq != null)
                        {
                            // Stationary town shopkeepers belong to a town or have a home/shop building; roaming caravans do not.
                            if (sq.TownOwn || sq.Town >= 0 || sq.HomeBuilding != null
                                || !string.IsNullOrEmpty(sq.AssignedHome) || sq.Shop?.Building != null)
                                continue;
                        }
                        else
                        {
                            // Without squad metadata, only treat as a moving trader if actively moving.
                            var task = c.Task;
                            if (task != Meitou.Simulation.Movement.CharacterTask.GoTo
                                && task != Meitou.Simulation.Movement.CharacterTask.Follow
                                && task != Meitou.Simulation.Movement.CharacterTask.Wander)
                                continue;
                        }

                        var at = c.Position;
                        if (!float.IsFinite(at.X) || !float.IsFinite(at.Z)) continue;
                        v.ToPixel(at.X, at.Y, at.Z, rw, rh, out float px, out float py);
                        if (!Inside(px, py, rw, rh, -60f)) continue;
                        at = host.DrawnPosition(c);
                        if (!float.IsFinite(at.X) || !float.IsFinite(at.Z)) continue;
                        v.ToPixel(at.X, float.IsFinite(at.Y) ? at.Y : Ground(v, at.X, at.Z), at.Z, rw, rh, out px, out py);
                        if (!Inside(px, py, rw, rh, 2f)) continue;

                        if (c.SquadId != 0 && !_seenTraderSquads.Add(c.SquadId)) continue;
                        string name = CaptionText.TraderLabel(c.Name, sq?.Name);
                        long trackKey = c.SquadId != 0 ? (long)c.SquadId + 1_000_000_000L : Pack.KeyOf(c);
                        AddTraderMarker(v, rw, rh, px, py, at.X, at.Z, trackKey, name, gameSeconds, arrows, ref labelCount);
                        drawn++;
                    }
                }

                // Distant roaming trade caravan squads (visible when zoomed out beyond character load distance)
                if (squads != null && drawn < MaxTraders)
                {
                    var pop = host.PopData;
                    int playerFaction = pop != null ? pop.PlayerFaction : -1;
                    var relations = pop?.Relations;
                    try
                    {
                        foreach (var sq in squads.All)
                        {
                            if (drawn >= MaxTraders) break;
                            if (sq == null || sq.Dead || sq.Wild || sq.TownOwn || sq.Town >= 0 || sq.Shop == null || sq.HomeBuilding != null)
                                continue;
                            if (sq.Faction == playerFaction || _seenTraderSquads.Contains(sq.Id) || _hostileSquads.Contains(sq.Id))
                                continue;
                            if (sq.Shop.Building != null || !string.IsNullOrEmpty(sq.AssignedHome)) continue;
                            if (sq.Members == null || sq.Members.Count == 0) continue;
                            if (relations != null && playerFaction >= 0 && sq.Faction >= 0 && relations.IsHostile(sq.Faction, playerFaction))
                                continue;
                            var at = sq.Position;
                            if (!float.IsFinite(at.X) || !float.IsFinite(at.Z) || !KnownPlaces.Near(v, at.X, at.Z)) continue;
                            v.ToPixel(at.X, Ground(v, at.X, at.Z), at.Z, rw, rh, out float px, out float py);
                            if (!Inside(px, py, rw, rh, 2f)) continue;
                            _seenTraderSquads.Add(sq.Id);
                            string name = CaptionText.TraderLabel(null, sq.Name);
                            AddTraderMarker(v, rw, rh, px, py, at.X, at.Z, (long)sq.Id + 1_000_000_000L, name, gameSeconds, arrows, ref labelCount);
                            drawn++;
                        }
                    }
                    catch { }
                }
            }
            finally
            {
                TraderWalk.EndFrame();
            }
            LastTraders = drawn;
        }

        static void AddTraderMarker(in MapView v, float rw, float rh, float px, float py, double wx, double wz, long trackKey,
            string name, double gameSeconds, bool arrows, ref int labelCount)
        {
            if (TraderWalk.Step(trackKey, wx, wz, gameSeconds, out double dx, out double dz) && arrows)
                Arrow(v, px, py, 7.5f, dx, dz, PalTrader, rw, rh);
            Shape(Ring, px, py, 7.2f, 1.6f, 0f, PalTrader, 0f);
            Shape(Diamond, px, py, 5.0f, 0f, 0f, PalTrader, 0f);
            var pl = PlaceSpots;
            pl.AddObstacle(px - 8.5f, py - 8.5f, px + 8.5f, py + 8.5f);
            if (labelCount >= PlaceLayout.Max || string.IsNullOrEmpty(name)) return;
            if (!PlaceCaptions.TryGetValue(name, out string caption))
            {
                if (PlaceCaptions.Count > 512) PlaceCaptions.Clear();
                PlaceCaptions[name] = caption = CaptionText.Escape(name);
            }
            Size(caption, out float tw, out float th);
            // Stable key by squad/character id so the label does not jump around while the caravan walks
            pl.Key[labelCount] = unchecked(trackKey * 1099511628211L ^ 0x545241444552L);
            (pl.AnchorX[labelCount], pl.AnchorY[labelCount], pl.Above[labelCount], pl.W[labelCount], pl.H[labelCount], pl.Priority[labelCount]) =
                (px, py, 10f, tw, th, 2);
            _placeText[labelCount] = caption;
            _placeStyle[labelCount] = StyleTrader;
            labelCount++;
        }

        /// <summary>A dark rounded pill behind a text (a wide line of the shader), so it reads on bright ground.</summary>
        static void Backing(float x, float y, float w, float h)
        {
            if (!(w > 0) || !(h > 0) || !Fits(x, y, w, h)) return;
            float thick = h * 0.94f, inset = Math.Clamp(thick * 0.5f - 4f, 0f, w * 0.5f), cy = y + h * 0.5f;
            Shape(Line, x + inset, cy, x + w - inset, cy, thick, PalBack, 0f);
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        static string ReadFactionName(GameHost host, int factionId)
        {
            var factions = host.PopData?.Factions;
            if (factions == null || factionId < 0 || factionId >= factions.Count) return null;
            return factions[factionId]?.Name;
        }

        /// <summary>Collects every visible character hostile to or fighting the player (active fighters first so large
        /// crowds never hide attackers) and feeds FoeGroups so clustered red faction/type labels can be laid out with
        /// Places before FoesDraw renders the red dots on top.</summary>
        static void FoesCollect(GameHost host, in MapView v, float rw, float rh)
        {
            int drawn = 0;
            FoeGroups.Clear();
            bool wantLabels = FoeLabels != FoeLabelMode.Off;
            var all = host.Session?.CurrentSnapshot?.Characters;
            if (all != null)
            {
                for (int pass = 0; pass < 2 && drawn < MaxHostiles; pass++)
                {
                    bool fightersOnly = pass == 0;
                    for (int i = 0; i < all.Count && drawn < MaxHostiles; i++)
                    {
                        var c = all[i];
                        if (c == null || c.IsPlayer) continue;
                        if (fightersOnly ? !c.FightsPlayer : (!c.HostileToPlayer || c.FightsPlayer)) continue;
                        if (c.Body is { } body && (body.Dead || body.Unconscious)) continue;
                        var at = c.Position;      // the tick's position first: most are far off the picture
                        if (!float.IsFinite(at.X) || !float.IsFinite(at.Z)) continue;
                        v.ToPixel(at.X, at.Y, at.Z, rw, rh, out float px, out float py);
                        if (!Inside(px, py, rw, rh, -40f)) continue;
                        at = host.DrawnPosition(c);
                        if (!float.IsFinite(at.X) || !float.IsFinite(at.Z)) continue;
                        v.ToPixel(at.X, float.IsFinite(at.Y) ? at.Y : Ground(v, at.X, at.Z), at.Z, rw, rh, out px, out py);
                        if (!Inside(px, py, rw, rh, -5f)) continue;
                        _foeX[drawn] = px;
                        _foeY[drawn] = py;
                        _foeFight[drawn] = c.FightsPlayer;
                        if (wantLabels)
                        {
                            string fname = null;
                            try { fname = ReadFactionName(host, c.Faction); } catch { }
                            FoeGroups.Add(px, py, c.Faction, fname, c.Name, c.FightsPlayer);
                        }
                        drawn++;
                    }
                }
            }
            _foeCount = drawn;
            LastHostiles = drawn;
        }

        static void AddFoeLabels(ref int labelCount)
        {
            FoeGroups.Build(FoeLabels);
            int added = 0;
            var pl = PlaceSpots;
            for (int i = 0; i < FoeGroups.ClusterCount && labelCount < PlaceLayout.Max; i++)
            {
                string raw = FoeGroups.Label[i];
                if (string.IsNullOrEmpty(raw)) continue;
                if (!PlaceCaptions.TryGetValue(raw, out string caption))
                {
                    if (PlaceCaptions.Count > 512) PlaceCaptions.Clear();
                    PlaceCaptions[raw] = caption = CaptionText.Escape(raw);
                }
                Size(caption, out float tw, out float th);
                float cx = FoeGroups.CX[i], cy = FoeGroups.CY[i];
                pl.AddObstacle(cx - 6f, cy - 6f, cx + 6f, cy + 6f);
                pl.Key[labelCount] = FoeGroups.Key[i];
                (pl.AnchorX[labelCount], pl.AnchorY[labelCount], pl.Above[labelCount], pl.W[labelCount], pl.H[labelCount], pl.Priority[labelCount]) =
                    (cx, cy, 9f, tw, th, FoeGroups.Fighting[i] ? 3 : 1);
                _placeText[labelCount] = caption;
                _placeStyle[labelCount] = StyleFoe;
                labelCount++;
                added++;
            }
            LastFoeLabels = added;
        }

        static void FoesDraw()
        {
            for (int i = _foeCount - 1; i >= 0; i--)
                Shape(Disc, _foeX[i], _foeY[i], _foeFight[i] ? 5.0f : 4.3f, 0f, 0f, PalHostile, 0f);
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

        /// <summary>The route handed in by 'topcam waypoints': a dotted line from the middle of your people through every
        /// waypoint to the end, a dot on each waypoint, a ring and the name (if given) on the end.</summary>
        static void WaypointLayer(in MapView v, float rw, float rh)
        {
            var w = _waypoints;
            if (w == null || w.X == null || w.X.Length == 0) return;
            int limit = _limit;
            _limit = Math.Min(limit, ShapeCount + MaxWaypointShapes);
            try
            {
                int n = 0;
                if (Pack.HasCentre)
                {
                    v.ToPixel(Pack.CentreX, Pack.CentreY, Pack.CentreZ, rw, rh, out _wpX[0], out _wpY[0]);
                    n = 1;
                }
                int start = n;
                for (int i = 0; i < w.X.Length && n <= MaxWaypoints; i++)
                {
                    v.ToPixel(w.X[i], Ground(v, w.X[i], w.Z[i]), w.Z[i], rw, rh, out float px, out float py);
                    if (!float.IsFinite(px) || !float.IsFinite(py)) continue;
                    _wpX[n] = px;
                    _wpY[n] = py;
                    n++;
                }
                if (n - start <= 0) return;
                float gap = 6f;
                int kept;
                while (true)
                {
                    kept = Thin(n, start, gap, dryRun: true);
                    if (kept <= MaxWaypointDots || gap > 400f) break;
                    gap *= 1.5f;
                }
                n = Thin(n, start, gap, dryRun: false);
                float along = 0f;
                for (int j = n - 1; j >= 1; j--)
                {
                    float x0 = _wpX[j - 1], y0 = _wpY[j - 1], x1 = _wpX[j], y1 = _wpY[j];
                    if (!(MathF.Max(x0, x1) < -16f || MathF.Min(x0, x1) > rw + 16f || MathF.Max(y0, y1) < -16f || MathF.Min(y0, y1) > rh + 16f))
                        Shape(Dots, x0, y0, x1, y1, 2.5f, PalRoute, along);
                    along += MathF.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0));
                }
                for (int j = Math.Max(start, 0); j < n - 1; j++)
                {
                    if (!Inside(_wpX[j], _wpY[j], rw, rh, -4f)) continue;
                    Shape(Disc, _wpX[j], _wpY[j], 2.6f, 0f, 0f, PalRoute, 0f);
                    LastWaypointDots++;
                }
                float ex = _wpX[n - 1], ey = _wpY[n - 1];
                if (Inside(ex, ey, rw, rh, -12f))
                {
                    Shape(Ring, ex, ey, 8f, 2.2f, 0f, PalRoute, 0f);
                    Shape(Disc, ex, ey, 3.2f, 0f, 0f, PalRoute, 0f);
                    if (!string.IsNullOrEmpty(w.Name))
                    {
                        string caption = CaptionText.Escape(w.Name);
                        Size(caption, out float tw, out float th);
                        float tx = ex - tw * 0.5f, ty = ey - 10f - th;
                        Backing(tx, ty, tw, th);
                        Text(caption, tx, ty, tw, th, StylePlace);
                    }
                }
                LastWaypointsDrawn = n - start;
            }
            catch (Exception e)
            {
                WaypointError = e.GetType().Name + ": " + e.Message;
                Log.Error("topcam: the waypoint route switched off after an error (the other marks stay): " + e);
            }
            finally
            {
                _limit = limit;
            }
        }

        static int Thin(int n, int start, float gap, bool dryRun)
        {
            float g2 = gap * gap;
            int k = 0;
            float lx = 0, ly = 0;
            for (int i = 0; i < n; i++)
            {
                bool keep = i < start || i == n - 1 || k == 0;
                if (!keep)
                {
                    float dx = _wpX[i] - lx, dy = _wpY[i] - ly;
                    keep = dx * dx + dy * dy >= g2;
                }
                if (!keep) continue;
                (lx, ly) = (_wpX[i], _wpY[i]);
                if (!dryRun) (_wpX[k], _wpY[k]) = (_wpX[i], _wpY[i]);
                k++;
            }
            return k;
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
                    float ex = plan.X[plan.Count - 1], ey = plan.Y[plan.Count - 1];
                    bool endOn = Inside(ex, ey, rw, rh, -8f);
                    int before = ShapeCount;
                    int savedLimit = _limit;
                    if (endOn) _limit = Math.Max(ShapeCount, savedLimit - 2);      // keep room for the destination ring and dot
                    // the dots are counted from the end, so they stand still on the ground while he walks
                    float along = 0f;
                    for (int j = plan.Count - 1; j >= 1; j--)
                    {
                        float x0 = plan.X[j - 1], y0 = plan.Y[j - 1], x1 = plan.X[j], y1 = plan.Y[j];
                        if (MathF.Max(x0, x1) >= -4f && MathF.Min(x0, x1) <= rw + 4f && MathF.Max(y0, y1) >= -4f && MathF.Min(y0, y1) <= rh + 4f)
                            Shape(Dots, x0, y0, x1, y1, 2f, plan.Far[j] ? PalRouteFar : PalRoute, along);
                        along += MathF.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0));
                    }
                    _limit = savedLimit;
                    if (endOn)
                    {
                        Shape(Ring, ex, ey, 5.5f, 1.8f, 0f, PalRoute, 0f);
                        Shape(Disc, ex, ey, 1.8f, 0f, 0f, PalRoute, 0f);
                    }
                    if (ShapeCount > before) LastRoutes++;
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
            layout.CircleR = _round ? _radius : 0f;
            layout.ClearObstacles();
            AddBottomChromeObstacle(layout.AddObstacle, rw, rh, lay);
            if (MapLayers.On(lay, MapLayers.North))
            {
                NorthSpot(v, rw, rh, out float nx, out float ny, out _, out _);
                layout.AddObstacle(nx - 24f, ny - 26f, nx + 24f, ny + 26f);
            }
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
                    Shape(Line, layout.DotX[j] + dx / len * from, layout.DotY[j] + dy / len * from, layout.LeadX[j], layout.LeadY[j], 1.0f, PalLead, 0f);
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
                            float pad = t.Text.IndexOf('#') >= 0 ? 14f : 4f;
                            size = new Vector2(MathF.Ceiling(measured.Width) + pad, MathF.Ceiling(measured.Height) + 2f);
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
                    if (ShowSquadBars && _bars.Active && _alpha > 0)
                        AddSquadBarControls(controls, _alpha / 100f, player.Width, player.Height);
                }
            }
            catch (Exception e)
            {
                Fail("labels", e);
            }
        }

        /// <summary>Emits the docked Squad HP and Squad Fed bar card directly below (or above) the minimap.</summary>
        static void AddSquadBarControls(List<UiControl> controls, float alpha, float winW, float winH)
        {
            if (!(winW > 64f) || !(winH > 64f)) return;
            int aByte = Math.Clamp((int)MathF.Round(alpha * 100f), 0, 100);
            float rowH = MathF.Max(18f, MathF.Round(_lineGuess * 0.94f));
            float padX = 10f, padY = 6f, rowGap = 4f;
            bool showDown = _bars.DownCount > 0;
            float downH = showDown ? MathF.Max(16f, MathF.Round(rowH * 0.86f)) : 0f;
            float panelW = MathF.Round(Math.Clamp(_clipW * 0.90f, 240f, MathF.Max(260f, _clipW)));
            if (panelW > winW - 8f) panelW = MathF.Max(180f, winW - 8f);
            float panelH = padY * 2f + rowH * 2f + rowGap + (showDown ? rowGap + downH : 0f);

            float px = MathF.Round(Math.Clamp(_clipX + (_clipW - panelW) * 0.5f, 4f, MathF.Max(4f, winW - panelW - 4f)));
            float pyBelow = MathF.Round(_clipY + _clipH + 8f);
            float pyAbove = MathF.Round(_clipY - panelH - 8f);
            float py = (pyBelow + panelH <= winH - 4f || pyAbove < 4f)
                ? MathF.Min(pyBelow, MathF.Max(4f, winH - panelH - 4f))
                : pyAbove;

            // Subtle outer border and dark translucent card
            controls.Add(new UiControl("topcam-sb-border", "Widget", new UiRect(px - 1f, py - 1f, panelW + 2f, panelH + 2f),
                "", "", "", true, EmptyProps, new Vector4(0.36f, 0.32f, 0.24f, 0.55f * alpha), Pickable: false));
            controls.Add(new UiControl("topcam-sb-bg", "Widget", new UiRect(px, py, panelW, panelH),
                "", "", "", true, EmptyProps, new Vector4(0.05f, 0.05f, 0.07f, 0.86f * alpha), Pickable: false));

            float labelW = MathF.Round(Math.Clamp(panelW * 0.31f, 70f, 96f));
            float valW = MathF.Round(Math.Clamp(panelW * 0.18f, 40f, 58f));
            float trackX = px + padX + labelW + 6f;
            float trackW = MathF.Max(48f, panelW - padX * 2f - labelW - valW - 12f);
            float trackH = MathF.Max(8f, MathF.Round(rowH * 0.54f));

            AddMeterRow(controls, "hp", "SQUAD HP", _bars.HpPct,
                MathF.Round(_bars.HpPct).ToString("0", Ci) + "%", MapMath.MeterTextColour(_bars.HpPct),
                px + padX, py + padY, labelW, trackX, trackW, trackH, valW, rowH, alpha, aByte);

            string fedVal = _bars.HasOrganic ? MathF.Round(_bars.FedPct).ToString("0", Ci) + "%" : "ROBOTS";
            string fedCol = _bars.HasOrganic ? MapMath.MeterTextColour(_bars.FedPct) : "0.68 0.78 0.92";
            AddMeterRow(controls, "fed", "SQUAD FED", _bars.HasOrganic ? _bars.FedPct : 100f,
                fedVal, fedCol,
                px + padX, py + padY + rowH + rowGap, labelW, trackX, trackW, trackH, valW, rowH, alpha, aByte);

            if (showDown)
            {
                float dy = py + padY + rowH * 2f + rowGap * 2f;
                controls.Add(new UiControl("topcam-sb-downbg", "Widget", new UiRect(px + padX, dy, panelW - padX * 2f, downH),
                    "", "", "", true, EmptyProps, new Vector4(0.36f, 0.07f, 0.07f, 0.85f * alpha), Pickable: false));
                string downText = _bars.DownCount.ToString(Ci) + " DOWN";
                controls.Add(new UiControl("topcam-sb-downtxt", "TextBox", new UiRect(px + padX, dy, panelW - padX * 2f, downH),
                    downText, "", "", true, BarStyle("1 0.42 0.36", "Center", aByte), Vector4.Zero, Pickable: false, TextScale: TextScale));
            }
        }

        static void AddMeterRow(List<UiControl> controls, string id, string label, float pct, string valText, string textColour,
            float labelX, float rowY, float labelW, float trackX, float trackW, float trackH, float valW, float rowH, float alpha, int aByte)
        {
            controls.Add(new UiControl("topcam-sb-" + id + "-lbl", "TextBox", new UiRect(labelX, rowY, labelW, rowH),
                label, "", "", true, BarStyle(textColour, "Left", aByte), Vector4.Zero, Pickable: false, TextScale: TextScale));

            float ty = rowY + MathF.Round((rowH - trackH) * 0.5f);
            controls.Add(new UiControl("topcam-sb-" + id + "-trk", "Widget", new UiRect(trackX, ty, trackW, trackH),
                "", "", "", true, EmptyProps, new Vector4(0.11f, 0.14f, 0.11f, 0.92f * alpha), Pickable: false));

            float fillW = Math.Clamp(pct, 0f, 100f) / 100f * trackW;
            if (fillW >= 1f)
            {
                const int slices = 8;
                float sliceW = trackW / slices;
                for (int s = 0; s < slices; s++)
                {
                    float x0 = s * sliceW;
                    if (x0 >= fillW) break;
                    float x1 = MathF.Min((s + 1) * sliceW, fillW);
                    float w = MathF.Ceiling(x1) - MathF.Floor(x0);
                    if (w < 1f) w = 1f;
                    MapMath.BarRampRgb((s + 0.5f) / slices, out float r, out float g, out float b);
                    controls.Add(new UiControl("topcam-sb-" + id + "-f" + s, "Widget", new UiRect(trackX + MathF.Floor(x0), ty, w, trackH),
                        "", "", "", true, EmptyProps, new Vector4(r, g, b, 0.96f * alpha), Pickable: false));
                }
                if (fillW < trackW - 1f)
                {
                    controls.Add(new UiControl("topcam-sb-" + id + "-cap", "Widget", new UiRect(trackX + MathF.Round(fillW) - 1f, ty, 2f, trackH),
                        "", "", "", true, EmptyProps, new Vector4(0.96f, 0.98f, 0.96f, 0.95f * alpha), Pickable: false));
                }
            }

            controls.Add(new UiControl("topcam-sb-" + id + "-val", "TextBox", new UiRect(trackX + trackW + 6f, rowY, valW, rowH),
                valText, "", "", true, BarStyle(textColour, "Right", aByte), Vector4.Zero, Pickable: false, TextScale: TextScale));
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

        /// <summary>Resets console-only mark tuning and transient layer errors back to the defaults ('topcam reset').</summary>
        public static void ResetDefaults()
        {
            ConeReach = ViewCone.DefaultReach;
            if (Palette[(int)PalConeFill * 4 + 3] != 0.18f)
            {
                Palette[(int)PalConeFill * 4 + 3] = 0.18f;
                PaletteVersion++;
            }
            PlacesError = TradersError = ConeError = ArrowError = RouteError = WaypointError = HudError = null;
            Walk.Clear();
            TraderWalk.Clear();
            Layout.Forget();
            PlaceSpots.Forget();
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
            if (on && layer == MapLayers.Traders) TradersError = null;   // 'traders on' always tries again
            Layers = MapLayers.Set(Layers, layer, on);
        }

        /// <summary>The marks' commands (a[0] is the word after "topcam"). Null: not one of them; "": done; else the refusal.</summary>
        public static string Command(IReadOnlyList<string> a)
        {
            switch (a[0].ToLowerInvariant())
            {
                case "waypoints":
                {
                    const string use = "usage: topcam waypoints <x> <z> [<x> <z> ...] [--name <text>]   (a route in world units, the end last; "
                        + "drawn on the route layer from your people through every point)  |  topcam waypoints clear";
                    if (a.Count == 2 && a[1].Equals("clear", StringComparison.OrdinalIgnoreCase))
                    {
                        _waypoints = null;
                        return "";
                    }
                    int end = a.Count;
                    string name = null;
                    for (int i = 1; i < a.Count; i++)
                        if (a[i].Equals("--name", StringComparison.OrdinalIgnoreCase))
                        {
                            end = i;
                            var nsb = new StringBuilder();
                            for (int k = i + 1; k < a.Count; k++)
                            {
                                if (k > i + 1) nsb.Append(' ');
                                nsb.Append(a[k]);
                            }
                            name = nsb.ToString().Trim();
                            if (name.Length > 40) name = name.Substring(0, 40);
                            break;
                        }
                    int count = end - 1;
                    if (count < 2 || count % 2 != 0 || count / 2 > MaxWaypoints) return use;
                    var xs = new double[count / 2];
                    var zs = new double[count / 2];
                    for (int i = 0; i < xs.Length; i++)
                    {
                        if (!Num(a, 1 + 2 * i, out xs[i]) || !Num(a, 2 + 2 * i, out zs[i])) return use;
                        if (Math.Abs(xs[i]) > 1e7 || Math.Abs(zs[i]) > 1e7) return use;
                    }
                    WaypointError = null;
                    _waypoints = new WaypointSet { X = xs, Z = zs, Name = string.IsNullOrEmpty(name) ? null : name, Stamp = ++_waypointStamp };
                    return "";
                }
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
                case "foelabels":
                case "hostilelabels":
                case "enemylabels":
                {
                    if (a.Count != 2 || !MapPrefs.FoeLabels(a[1], out var mode))
                        return "usage: topcam foelabels auto|on|off   (auto: labels enemies attacking your squad; on: all visible hostiles; off: red dots only)";
                    FoeLabels = mode;
                    return "";
                }
                case "squadbars":
                case "bars":
                case "squadstats":
                case "meters":
                {
                    if (a.Count != 2 || !MapPrefs.OnOff(a[1], out bool on))
                        return "usage: topcam squadbars on|off   (docked Squad HP and Squad Fed bars beside the map)";
                    ShowSquadBars = on;
                    if (on) HudError = null;
                    else _bars.Active = false;
                    return "";
                }
                case "clock":
                case "time":
                {
                    if (a.Count != 2 || !MapPrefs.OnOff(a[1], out bool on))
                        return "usage: topcam clock on|off   (in-game day and clock time on the map)";
                    ShowClock = on;
                    if (on) HudError = null;
                    return "";
                }
                case "cats":
                case "money":
                {
                    if (a.Count != 2 || !MapPrefs.OnOff(a[1], out bool on))
                        return "usage: topcam cats on|off   (your Cats on the map)";
                    ShowCats = on;
                    if (on) HudError = null;
                    return "";
                }
                case "speed":
                case "gamespeed":
                {
                    if (a.Count != 2 || !MapPrefs.OnOff(a[1], out bool on))
                        return "usage: topcam speed on|off   (game speed or PAUSED on the map)";
                    ShowSpeed = on;
                    if (on) HudError = null;
                    return "";
                }
                case "traders":
                case "caravans":
                {
                    // the same switch as 'topcam layer traders'
                    if (a.Count != 2 || !MapPrefs.OnOff(a[1], out bool on)) return "usage: topcam traders on|off   (green markers on moving trade caravans; = topcam layer traders)";
                    SetLayer(MapLayers.Traders, on);
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
            sb.Append(",\"traders\":{\"shown\":").Append(LastTraders).Append(",\"tracked\":").Append(TraderWalk.Count)
              .Append(",\"error\":").Append(MapMath.JsonText(TradersError)).Append('}');
            var wp = _waypoints;
            sb.Append(",\"waypoints\":{\"n\":").Append(wp?.X?.Length ?? 0).Append(",\"name\":").Append(MapMath.JsonText(wp?.Name))
              .Append(",\"drawn\":").Append(LastWaypointsDrawn).Append(",\"dots\":").Append(LastWaypointDots)
              .Append(",\"error\":").Append(MapMath.JsonText(WaypointError)).Append('}');
            sb.Append(",\"arrows\":{\"drawn\":").Append(LastArrows).Append(",\"tracked\":").Append(Walk.Count)
              .Append(",\"error\":").Append(MapMath.JsonText(ArrowError)).Append('}');
            sb.Append(",\"routes\":{\"drawn\":").Append(LastRoutes).Append(",\"same\":").Append(LastRoutesSkipped)
              .Append(",\"error\":").Append(MapMath.JsonText(RouteError)).Append('}');
            sb.Append(",\"hurt\":").Append(LastHurt);
            sb.Append(",\"foeLabels\":{\"mode\":").Append(MapMath.JsonText(MapPrefs.FoeLabelsWord(FoeLabels))).Append(",\"shown\":").Append(LastFoeLabels).Append('}');
            sb.Append(",\"hud\":{\"squadBars\":").Append(ShowSquadBars ? "true" : "false")
              .Append(",\"clock\":").Append(ShowClock ? "true" : "false")
              .Append(",\"cats\":").Append(ShowCats ? "true" : "false")
              .Append(",\"speed\":").Append(ShowSpeed ? "true" : "false");
            if (_bars.Active)
                sb.Append(",\"hp\":").Append(MapMath.Num(_bars.HpPct, "0.#"))
                  .Append(",\"fed\":").Append(MapMath.Num(_bars.FedPct, "0.#"))
                  .Append(",\"down\":").Append(_bars.DownCount)
                  .Append(",\"organic\":").Append(_bars.HasOrganic ? "true" : "false")
                  .Append(",\"rawHunger\":").Append(MapMath.Num(_bars.RawHunger, "0.###"))
                  .Append(",\"rawHudFed\":").Append(MapMath.Num(_bars.RawHudFed, "0.###"));
            sb.Append(",\"error\":").Append(MapMath.JsonText(HudError)).Append('}');
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
