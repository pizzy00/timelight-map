// Console checks: the view cone (ViewCone, against the matrices the game draws its main view with), the heading arrows
// (Heading), the layer switches (MapLayers) and the coloured labels (LabelSpec, against a port of the game's own caption tag
// reader, Meitou.Data.Ui.MyGuiTextTags.Parse, written from its IL).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Text;
using TimeLightMap;

static class MapLayerChecks
{
    static Action<bool, string> _check;

    static void Check(bool ok, string what) => _check(ok, what);

    public static void Run(Action<bool, string> check)
    {
        _check = check;
        Cone();
        ConeAgainstTheCamera();
        Walking();
        Layers();
        CaptionTags();
    }

    static CamPose Camera(double tx, double ty, double tz, double yawDeg, double pitchDeg, double distance, double fovDeg = 50, double aspect = 16.0 / 9)
    {
        // WorldCamera: Back = (cos P sin Y, sin P, cos P cos Y); Eye = Target + Back * Distance
        double y = yawDeg * Math.PI / 180, p = pitchDeg * Math.PI / 180;
        return new CamPose
        {
            TargetX = tx, TargetY = ty, TargetZ = tz,
            EyeX = tx + Math.Cos(p) * Math.Sin(y) * distance, EyeY = ty + Math.Sin(p) * distance, EyeZ = tz + Math.Cos(p) * Math.Cos(y) * distance,
            Yaw = y, Pitch = p, Fov = fovDeg * Math.PI / 180, Aspect = aspect, Valid = true,
        };
    }

    static void Cone()
    {
        // yaw 0 looks north (-Z), the apex is the eye's ground point
        var c = Camera(1000, 50, 2000, 0, 45, 1000);
        Check(ViewCone.Make(c, ViewCone.DefaultReach, 10, 1e6, out double ax, out double az, out double dx, out double dz, out double half, out double len), "cone: made");
        Check(Math.Abs(ax - c.EyeX) < 1e-9 && Math.Abs(az - c.EyeZ) < 1e-9, "cone: the apex is the eye's ground point");
        Check(Math.Abs(dx) < 1e-12 && Math.Abs(dz + 1) < 1e-12, "cone: yaw 0 looks north (-Z)");
        double hfov = Math.Atan(Math.Tan(25 * Math.PI / 180) * 16 / 9);
        Check(Math.Abs(half - hfov) < 1e-12, "cone: half the opening = atan(tan(fov/2) x aspect) = " + (half * 180 / Math.PI).ToString("0.00") + " deg");
        double flat = Math.Cos(Math.PI / 4) * 1000, height = Math.Sin(Math.PI / 4) * 1000, far = height / Math.Tan(20 * Math.PI / 180);
        // the default reach is 1.5 x the ground distance to the view's centre, so here the reach caps it, not the top edge
        Check(Math.Abs(len - Math.Min(far, 1.5 * flat)) < 1e-6 && ViewCone.DefaultReach == 1.5,
            "cone: pitch 45, fov 50, reach 1.5: min(top edge on the ground, 1.5 x flat) (" + len.ToString("0.0") + " vs " + Math.Min(far, 1.5 * flat).ToString("0.0") + ")");
        Check(ViewCone.Make(c, 3.0, 10, 1e6, out _, out _, out _, out _, out _, out double len3) && Math.Abs(len3 - far) < 1e-6,
            "cone: with reach 3 the picture's top edge on the ground caps it (" + len3.ToString("0.0") + " vs " + far.ToString("0.0") + ")");
        Check(len > flat && len <= ViewCone.DefaultReach * flat + 1e-9, "cone: it reaches past the point the camera looks at, and no further than Reach x its ground distance");

        // yaw 90: the eye stands east of the target and looks west (-X); yaw 180 looks south; yaw -90 looks east
        foreach (var (yaw, ex, ez, word) in new[] { (90.0, -1.0, 0.0, "west"), (180.0, 0.0, 1.0, "south"), (-90.0, 1.0, 0.0, "east") })
        {
            var cy = Camera(0, 0, 0, yaw, 40, 800);
            ViewCone.Make(cy, ViewCone.DefaultReach, 10, 1e6, out _, out _, out double ddx, out double ddz, out _, out _);
            Check(Math.Abs(ddx - ex) < 1e-9 && Math.Abs(ddz - ez) < 1e-9, "cone: yaw " + yaw + " looks " + word);
            Check((cy.TargetX - cy.EyeX) * ddx + (cy.TargetZ - cy.EyeZ) * ddz > 0, "cone: yaw " + yaw + ": the target lies ahead of the eye along the axis");
        }

        // the axis passes through the target's ground point (any yaw, any pitch below straight down)
        double worst = 0;
        for (double yaw = -180; yaw < 180; yaw += 17)
            for (double pitch = 5; pitch <= 85; pitch += 8)
            {
                var cc = Camera(-15000, 1300, -8700, yaw, pitch, 2500);
                ViewCone.Make(cc, ViewCone.DefaultReach, 10, 1e6, out double qx, out double qz, out double ux, out double uz, out _, out _);
                double tx = cc.TargetX - qx, tz = cc.TargetZ - qz, tl = Math.Sqrt(tx * tx + tz * tz);
                worst = Math.Max(worst, Math.Abs(tx * uz - tz * ux) / tl);
            }
        Check(worst < 1e-9, "cone: the axis passes through the point the camera looks at, every yaw and pitch (" + worst.ToString("E1") + ")");

        // the top edge above the horizon: the cone ends at Reach x the eye's ground distance
        var low = Camera(0, 0, 0, 30, 15, 300);
        ViewCone.Make(low, ViewCone.DefaultReach, 1, 1e6, out _, out _, out _, out _, out _, out double lowLen);
        Check(Math.Abs(lowLen - ViewCone.DefaultReach * Math.Cos(15 * Math.PI / 180) * 300) < 1e-6, "cone: a low camera that sees the horizon: Reach x its ground distance (" + lowLen.ToString("0.0") + ")");
        // 'topcam cone reach <x>': the same low camera with reach 1.5, and a reach out of range held to 0.5..8
        ViewCone.Make(low, 1.5, 1, 1e6, out _, out _, out _, out _, out _, out double lowLen15);
        Check(Math.Abs(lowLen15 - 1.5 * Math.Cos(15 * Math.PI / 180) * 300) < 1e-6, "cone: reach 1.5 -> 1.5 x the ground distance");
        ViewCone.Make(low, 100, 1, 1e6, out _, out _, out _, out _, out _, out double lowLenBig);
        ViewCone.Make(low, double.NaN, 1, 1e6, out _, out _, out _, out _, out _, out double lowLenNaN);
        Check(Math.Abs(lowLenBig - ViewCone.MaxReach * Math.Cos(15 * Math.PI / 180) * 300) < 1e-6 && Math.Abs(lowLenNaN - lowLen) < 1e-9,
            "cone: a reach of 100 is held to 8, no reach is the default");
        // the picture's top edge still caps a long reach (pitch 45: 1942.6 of 1000 out)
        ViewCone.Make(c, 8, 10, 1e6, out _, out _, out _, out _, out _, out double capped);
        Check(Math.Abs(capped - far) < 1e-6, "cone: reach 8 is still capped where the picture's top edge meets the ground");
        // straight down: no ground distance, the shortest the map shows
        var down = Camera(0, 0, 0, 0, 89.9, 3000);
        ViewCone.Make(down, ViewCone.DefaultReach, 120, 1e6, out _, out _, out _, out _, out _, out double downLen);
        Check(Math.Abs(downLen - 120) < 1e-9, "cone: a camera looking straight down: the shortest length (" + downLen.ToString("0.0") + ")");
        // held to the longest
        var high = Camera(0, 0, 0, 0, 30, 60000);
        ViewCone.Make(high, ViewCone.DefaultReach, 1, 5000, out _, out _, out _, out _, out _, out double highLen);
        Check(highLen == 5000, "cone: held to the longest the map can show");
        // a camera under the point it looks at (pitch below 0): Reach x its ground distance
        var under = Camera(0, 0, 0, 0, -20, 500);
        Check(ViewCone.Make(under, ViewCone.DefaultReach, 1, 1e6, out _, out _, out _, out _, out _, out double underLen) && Math.Abs(underLen - ViewCone.DefaultReach * Math.Cos(20 * Math.PI / 180) * 500) < 1e-6,
            "cone: a camera below the point it looks at: Reach x its ground distance");
        // nothing usable
        Check(!ViewCone.Make(default, ViewCone.DefaultReach, 1, 100, out _, out _, out _, out _, out _, out _), "cone: no camera, no cone");
        var nan = c;
        nan.Yaw = double.NaN;
        Check(!ViewCone.Make(nan, ViewCone.DefaultReach, 1, 100, out _, out _, out _, out _, out _, out _), "cone: a camera with no yaw, no cone");
        var nofov = c;
        nofov.Fov = 0;
        Check(!ViewCone.Make(nofov, ViewCone.DefaultReach, 1, 100, out _, out _, out _, out _, out _, out _), "cone: a camera with no field of view, no cone");
        // a wide or narrow lens is held to 5..80 degrees a side
        var wide = Camera(0, 0, 0, 0, 40, 500, 170, 4);
        ViewCone.Make(wide, ViewCone.DefaultReach, 1, 1e6, out _, out _, out _, out _, out double wideHalf, out _);
        Check(Math.Abs(wideHalf - ViewCone.MaxHalf) < 1e-12, "cone: a very wide lens is held to 80 degrees a side");
        var narrow = Camera(0, 0, 0, 0, 40, 500, 2, 1);
        ViewCone.Make(narrow, ViewCone.DefaultReach, 1, 1e6, out _, out _, out _, out _, out double narrowHalf, out _);
        Check(Math.Abs(narrowHalf - ViewCone.MinHalf) < 1e-12, "cone: a very narrow lens is held to 5 degrees a side");

        // the outline: the apex, then n + 1 points on the far edge, symmetric, all at the length
        int n = ViewCone.Segments(half);
        Check(n == (int)Math.Ceiling(2 * half / ViewCone.SegmentAngle), "cone: " + n + " pieces for a " + (2 * half * 180 / Math.PI).ToString("0") + " degree cone");
        Check(ViewCone.Segments(ViewCone.MaxHalf) <= 16 && ViewCone.Segments(ViewCone.MinHalf) >= 3, "cone: 3 to 16 pieces (the arrays hold 18 points)");
        var xs = new double[18];
        var zs = new double[18];
        ViewCone.Points(ax, az, dx, dz, half, len, n, xs, zs);
        Check(xs[0] == ax && zs[0] == az, "cone outline: [0] is the apex");
        bool allAtLen = true, sym = true;
        for (int i = 1; i <= n + 1; i++)
        {
            double ox = xs[i] - ax, oz = zs[i] - az;
            allAtLen &= Math.Abs(Math.Sqrt(ox * ox + oz * oz) - len) < 1e-6;
            int j = n + 2 - i;
            double px = xs[j] - ax, pz = zs[j] - az;
            double along1 = ox * dx + oz * dz, along2 = px * dx + pz * dz, side1 = ox * -dz + oz * dx, side2 = px * -dz + pz * dx;
            sym &= Math.Abs(along1 - along2) < 1e-6 && Math.Abs(side1 + side2) < 1e-6;
        }
        Check(allAtLen, "cone outline: the far edge lies at the cone's length");
        Check(sym, "cone outline: symmetric about the axis");
        double e0x = xs[1] - ax, e0z = zs[1] - az;
        double edgeAngle = Math.Acos((e0x * dx + e0z * dz) / len);
        Check(Math.Abs(edgeAngle - half) < 1e-9, "cone outline: its sides open half the angle each way");
    }

    /// <summary>The cone against the main view itself: Matrix4x4.CreateLookAt(Eye, Target, Y) x CreatePerspectiveFieldOfView(fov,
    /// aspect), the matrices WorldCamera.View and DepthConvention.Perspective build. A level camera (pitch 0): a point on a side of
    /// the cone at the eye's height is on the picture's left or right edge, and a point on the axis is in its middle.</summary>
    static void ConeAgainstTheCamera()
    {
        double worst = 0, worstMid = 0;
        foreach (double aspect in new[] { 16.0 / 9, 4.0 / 3, 21.0 / 9 })
            foreach (double fov in new[] { 35.0, 50.0, 70.0 })
                for (double yaw = -170; yaw < 180; yaw += 23)
                {
                    var c = Camera(-15000, 1200, -8700, yaw, 0.0, 900, fov, aspect);
                    ViewCone.Make(c, ViewCone.DefaultReach, 1, 1e6, out double ax, out double az, out double dx, out double dz, out double half, out double len);
                    var view = Matrix4x4.CreateLookAt(new Vector3((float)c.EyeX, (float)c.EyeY, (float)c.EyeZ), new Vector3((float)c.TargetX, (float)c.TargetY, (float)c.TargetZ), Vector3.UnitY);
                    var proj = Matrix4x4.CreatePerspectiveFieldOfView((float)c.Fov, (float)aspect, 1f, 100000f);
                    var vp = view * proj;
                    foreach (int side in new[] { -1, 1 })
                    {
                        double a = side * half, cs = Math.Cos(a), sn = Math.Sin(a);
                        double px = ax + (dx * cs - dz * sn) * 2000, pz = az + (dx * sn + dz * cs) * 2000;
                        var clip = Vector4.Transform(new Vector4((float)px, (float)c.EyeY, (float)pz, 1f), vp);
                        worst = Math.Max(worst, Math.Abs(Math.Abs(clip.X / clip.W) - 1));
                    }
                    var mid = Vector4.Transform(new Vector4((float)(ax + dx * 2000), (float)c.EyeY, (float)(az + dz * 2000), 1f), vp);
                    worstMid = Math.Max(worstMid, Math.Abs(mid.X / mid.W));
                }
        Check(worst < 2e-4, "cone vs the main view's matrices: its sides are the picture's left and right edges (worst " + worst.ToString("0.000000") + " in NDC)");
        Check(worstMid < 2e-4, "cone vs the main view's matrices: its axis is the picture's middle (worst " + worstMid.ToString("0.000000") + ")");

        // pitched down: the target (the picture's middle) is inside the cone, and so are the ground points at the middle of the
        // picture's bottom and top edges where they meet the ground
        int inside = 0, total = 0;
        for (double pitch = 20; pitch <= 80; pitch += 10)
            for (double yaw = -150; yaw < 180; yaw += 50)
            {
                var c = Camera(0, 0, 0, yaw, pitch, 1500);
                ViewCone.Make(c, ViewCone.DefaultReach, 1, 1e6, out double ax, out double az, out double dx, out double dz, out double half, out double len);
                total++;
                double tx = c.TargetX - ax, tz = c.TargetZ - az, tl = Math.Sqrt(tx * tx + tz * tz);
                bool ok = tl <= len + 1e-6 && (tl < 1e-9 || Math.Acos(Math.Clamp((tx * dx + tz * dz) / tl, -1, 1)) <= half + 1e-9);
                if (ok) inside++;
            }
        Check(inside == total, "cone: the point the camera looks at is inside its cone (" + inside + " of " + total + " cameras)");
    }

    // ------------------------------------------------------------------------------------------------ the heading arrows

    static void Walking()
    {
        const double dt = 1.0 / 60;
        // standing still: never an arrow
        var h = new Heading();
        bool any = false;
        for (int f = 0; f < 600; f++)
        {
            h.BeginFrame();
            any |= h.Step(1, 100, 100, 1000 + f * dt, out _, out _);
            h.EndFrame();
        }
        Check(!any, "arrows: a man who stands has none");

        // walking north at 2.5 m/s: the arrow comes within half a second and points north
        h = new Heading();
        double t = 1000, x = 0, z = 0, on = -1, dirErr = 0;
        for (int f = 0; f < 240; f++)
        {
            t += dt;
            z -= 25 * dt;
            h.BeginFrame();
            bool w = h.Step(7, x, z, t, out double dx, out double dz);
            if (w && on < 0) on = (f + 1) * dt;
            if (w && f > 120) dirErr = Math.Max(dirErr, Math.Abs(dx) + Math.Abs(dz + 1));
            h.EndFrame();
        }
        Check(on > 0 && on <= 0.6, "arrows: walking at 2.5 m/s shows the arrow after " + on.ToString("0.00") + " s (at most 0.6)");
        Check(dirErr < 1e-6, "arrows: walking north points north");

        // he stops: the arrow goes within 1.2 s
        double off = -1;
        for (int f = 0; f < 240; f++)
        {
            t += dt;
            h.BeginFrame();
            bool w = h.Step(7, x, z, t, out _, out _);
            if (!w && off < 0) off = (f + 1) * dt;
            h.EndFrame();
        }
        Check(off > 0 && off <= 1.2, "arrows: stopped, the arrow goes after " + off.ToString("0.00") + " s (at most 1.2)");

        // a slow walk (1.2 m/s) also counts
        h = new Heading();
        bool slow = false;
        for (int f = 0; f < 180; f++)
        {
            t += dt;
            x += 12 * dt;
            h.BeginFrame();
            slow |= h.Step(8, x, 0, t, out _, out _);
        }
        Check(slow, "arrows: a slow walk (1.2 m/s) shows one");

        // a fight's back and forth: +-0.6 m, 0.5 to 2 times a second: never an arrow
        foreach (double hz in new[] { 0.5, 1.0, 2.0 })
            foreach (double amp in new[] { 3.0, 6.0 })
            {
                h = new Heading();
                bool shuffle = false;
                for (int f = 0; f < 600; f++)
                {
                    double tt = f * dt;
                    h.BeginFrame();
                    shuffle |= h.Step(9, 500 + amp * Math.Sin(2 * Math.PI * hz * tt), 500 + amp * 0.5 * Math.Cos(2 * Math.PI * hz * tt), 2000 + tt, out _, out _);
                }
                Check(!shuffle, "arrows: a fight's back and forth (" + (amp / 10).ToString("0.0") + " m, " + hz + " a second) shows none");
            }

        // paused: no game time passes, the arrow stays as it stood (direction too)
        h = new Heading();
        t = 5000;
        x = z = 0;
        for (int f = 0; f < 120; f++)
        {
            t += dt;
            x += 30 * dt;
            h.BeginFrame();
            h.Step(3, x, z, t, out _, out _);
        }
        bool kept = true;
        for (int f = 0; f < 600; f++)
        {
            h.BeginFrame();
            kept &= h.Step(3, x, z, t, out double dx, out double dz) && Math.Abs(dx - 1) < 1e-6 && Math.Abs(dz) < 1e-6;
        }
        Check(kept, "arrows: a paused game keeps the arrow and its direction (10 s of frames, no game time)");

        // the game at 5x: the same walk in game time gives the same answer (a frame covers 5 times the game time)
        double onFast = -1;
        h = new Heading();
        t = 0;
        x = 0;
        for (int f = 0; f < 60; f++)
        {
            t += dt * 5;
            x += 25 * dt * 5;
            h.BeginFrame();
            if (h.Step(4, x, 0, t, out _, out _) && onFast < 0) onFast = t;
        }
        Check(onFast > 0 && onFast <= 0.6 + 5 * dt + 1e-9, "arrows: at 5x speed the arrow comes after " + onFast.ToString("0.00")
                                                          + " game seconds (the same rule in game time, give or take the one frame of 1/12 game s)");

        // a jump (a load, a teleport): no arrow from it
        bool jumpArrow = h.Step(4, x + 5000, 0, t + dt, out _, out _);
        Check(!jumpArrow, "arrows: a jump of 500 m between two frames shows no arrow");
        // a gap in the looks (the map was faded out): learnt again
        h = new Heading();
        for (int f = 0; f < 60; f++) h.Step(5, f * 0.5, 0, f * dt, out _, out _);
        bool gap = h.Step(5, 40, 0, 60 * dt + 10, out _, out _);
        Check(!gap, "arrows: after a gap of 10 game seconds he is learnt again (no arrow at once)");

        // a turn: east, then north; the arrow follows within a second
        h = new Heading();
        t = 0;
        x = z = 0;
        for (int f = 0; f < 120; f++)
        {
            t += dt;
            x += 25 * dt;
            h.Step(6, x, z, t, out _, out _);
        }
        double turned = -1;
        for (int f = 0; f < 120; f++)
        {
            t += dt;
            z -= 25 * dt;
            if (h.Step(6, x, z, t, out double dx, out double dz) && dz < -0.95 && turned < 0) turned = (f + 1) * dt;
        }
        Check(turned > 0 && turned <= 1.0, "arrows: a turn from east to north is followed in " + turned.ToString("0.00") + " s");

        // men not seen for a while are forgotten
        h = new Heading();
        for (int f = 0; f < 10; f++)
        {
            h.BeginFrame();
            h.Step(100 + f, 0, 0, f, out _, out _);
            h.EndFrame();
        }
        for (int f = 0; f < Heading.ForgetFrames + 120; f++)
        {
            h.BeginFrame();
            h.Step(1, 0, 0, 100 + f * dt, out _, out _);
            h.EndFrame();
        }
        Check(h.Count == 1, "arrows: men not seen for " + Heading.ForgetFrames + " frames are forgotten (" + h.Count + " kept)");
        Check(!h.Step(2, double.NaN, 0, 1, out _, out _) && !h.Step(2, 0, 0, double.NaN, out _, out _), "arrows: no position or no clock: no arrow");
    }

    // ------------------------------------------------------------------------------------------------ the layers

    static List<string> W(string line)
    {
        var l = new List<string>();
        foreach (string w in line.Split(' ', StringSplitOptions.RemoveEmptyEntries)) l.Add(w);
        return l;
    }

    static void Layers()
    {
        Check(MapLayers.Count == 10 && MapLayers.Words.Length == MapLayers.Count && MapLayers.All == (1 << MapLayers.Count) - 1, "layers: ten of them");
        Check(MapLayers.WordList == "names|foes|towns|cone|arrows|scale|north|route|hurt|traders" && MapLayers.WordList == string.Join("|", MapLayers.Words), "layers: the usage text names them all");
        Check(MapLayers.Index("hostiles") == MapLayers.Foes && MapLayers.Index("Places") == MapLayers.Towns && MapLayers.Index("routes") == MapLayers.Route
              && MapLayers.Index("waypoints") == MapLayers.Route && MapLayers.Index("health") == MapLayers.Hurt
              && MapLayers.Index("trader") == MapLayers.Traders && MapLayers.Index("caravans") == MapLayers.Traders && MapLayers.Index("merchants") == MapLayers.Traders,
            "layers: the other words (hostiles, places, routes, waypoints, health, caravans, merchants)");
        Check(MapLayers.Set(0, MapLayers.Hurt, true) == 1 << MapLayers.Hurt && MapLayers.Set(MapLayers.All, MapLayers.Route, false) == (MapLayers.All & ~(1 << MapLayers.Route)),
            "layers: Set switches one bit");
        bool idx = true;
        for (int i = 0; i < MapLayers.Count; i++)
            idx &= MapLayers.Index(MapLayers.Words[i]) == i && MapLayers.Index(MapLayers.Words[i].ToUpperInvariant()) == i;
        Check(idx, "layers: every name, any case");
        Check(MapLayers.Index("name") == MapLayers.Names && MapLayers.Index("foe") == MapLayers.Foes
              && MapLayers.Index("town") == MapLayers.Towns && MapLayers.Index("arrow") == MapLayers.Arrows, "layers: the singular names too");
        Check(MapLayers.Index("cones") == -1 && MapLayers.Index("") == -1 && MapLayers.Index(null) == -1, "layers: nothing else");
        foreach (string gone in new[] { "labels", "dots", "text", "everything", "colours", "markers", "pins" })
            Check(MapLayers.Index(gone) == -1 && MapLayers.Apply(W("layer " + gone + " off"), 1, MapLayers.All, out _) is string no && no.Contains(gone),
                "layers: '" + gone + "' is not a layer");

        Check(MapLayers.Apply(W("layer cone off"), 1, MapLayers.All, out int m) == null && m == (MapLayers.All & ~(1 << MapLayers.Cone)), "layer cone off");
        Check(MapLayers.Apply(W("layer cone on"), 1, m, out m) == null && m == MapLayers.All, "layer cone on");
        Check(MapLayers.Apply(W("layer all off names on foes on"), 1, MapLayers.All, out m) == null && m == ((1 << MapLayers.Names) | (1 << MapLayers.Foes)),
            "layer all off names on foes on: pairs in order");
        Check(MapLayers.Apply(W("layer ALL ON"), 1, 0, out m) == null && m == MapLayers.All, "layer all on (any case)");
        int before = 0x55;
        Check(MapLayers.Apply(W("layer cone off foes"), 1, before, out m) != null && m == before, "layer: an odd count is refused, nothing changes");
        Check(MapLayers.Apply(W("layer cone off bogus on"), 1, before, out m) is string e1 && e1.Contains("bogus") && m == before, "layer: an unknown name is refused, nothing of the line applies");
        Check(MapLayers.Apply(W("layer cone maybe"), 1, before, out m) is string e2 && e2.Contains("maybe") && m == before, "layer: on|off only");
        Check(MapLayers.Apply(W("layer cone false route 0 hurt yes"), 1, MapLayers.All, out m) == null
              && m == (MapLayers.All & ~(1 << MapLayers.Cone) & ~(1 << MapLayers.Route)), "layer: true/false, 1/0, yes/no as on/off");
        Check(MapLayers.Apply(W("layer"), 1, before, out m) is string e3 && e3.StartsWith("usage: topcam layer") && m == before, "layer: no pair (the caller answers the state)");

        var sb = new StringBuilder();
        MapLayers.Json(sb, MapLayers.All & ~(1 << MapLayers.Cone) & ~(1 << MapLayers.Foes));
        string j = sb.ToString();
        Check(j == "{\"names\":true,\"foes\":false,\"towns\":true,\"cone\":false,\"arrows\":true,\"scale\":true,\"north\":true,\"route\":true,\"hurt\":true,\"traders\":true}", "layers: the status object " + j);
        bool on = true;
        for (int i = 0; i < MapLayers.Count; i++) on &= MapLayers.On(MapLayers.All, i) && !MapLayers.On(0, i);
        Check(on, "layers: On() reads the bits");
        Check(CaptionText.TraderLabel("Hive Caravan Boss", "Western Hive") == "Hive Caravan Boss"
              && CaptionText.TraderLabel("Amanaka", "Trade Caravan") == "Trade Caravan"
              && CaptionText.TraderLabel("Amanaka", "Traders Guild") == "Traders Guild"
              && CaptionText.TraderLabel("Amanaka", null) == "Amanaka (Trader)"
              && CaptionText.TraderLabel(null, null) == "Trade Caravan",
            "trader labels: descriptive caravan names kept, personal names tagged");

        // hostile faction/type labels and clustering (v1.6.0)
        Check(CaptionText.FoeLabel("Dust Bandit", "Dust Bandits", 6) == "Dust Bandits x6"
              && CaptionText.FoeLabel("Beak Thing", "Gutters", 3) == "Beak Thing x3"
              && CaptionText.FoeLabel("Beak Thing", "Gutters", 1) == "Beak Thing"
              && CaptionText.FoeLabel("Dust Boss", "Dust Bandits", 1) == "Dust Boss (Dust Bandits)"
              && CaptionText.FoeLabel("Dust Bandit", "Dust Bandits", 1) == "Dust Bandit"
              && CaptionText.FoeLabel("Fogman", "Fogmen", 1) == "Fogman"
              && CaptionText.FoeLabel("Fogman", "Fogmen", 4) == "Fogmen x4"
              && CaptionText.FoeLabel(null, null, 2) == "Hostiles x2"
              && CaptionText.FoeLabel(null, null, 1) == "Hostile",
            "foe labels: faction groups, wildlife species, single leader vs redundant stem");

        var fc = new FoeClusterer();
        fc.Add(100, 100, 12, "Dust Bandits", "Dust Bandit", false);
        fc.Add(120, 110, 12, "Dust Bandits", "Dust Boss", true);
        fc.Add(130, 105, 12, "Dust Bandits", "Dust Bandit", false);
        fc.Add(260, 260, 19, "Hungry Bandits", "Hungry Bandit", false);
        fc.Add(270, 265, 19, "Hungry Bandits", "Hungry Bandit", false);
        fc.Add(40, 200, 7, "Gutters", "Beak Thing", true);
        fc.Add(50, 205, 7, "Gutters", "Bonedog", true);
        fc.Build(FoeLabelMode.Auto);
        Check(fc.ClusterCount == 3 && fc.Label[0] == "Dust Bandits x3" && fc.Fighting[0]
              && fc.Label[1] == "Beak Thing" && fc.Label[2] == "Bonedog",
            "foe clusters (auto): labels attacking groups, omits passive bandits, keeps wildlife species separate");

        fc.Clear();
        fc.Add(100, 100, 12, "Dust Bandits", "Dust Bandit", false);
        fc.Add(120, 110, 12, "Dust Bandits", "Dust Bandit", false);
        fc.Build(FoeLabelMode.On);
        Check(fc.ClusterCount == 1 && fc.Label[0] == "Dust Bandits x2" && !fc.Fighting[0],
            "foe clusters (on): labels passive hostiles too");
        fc.Clear();
        fc.Add(100, 100, 12, "Dust Bandits", "Dust Bandit", true);
        fc.Build(FoeLabelMode.Off);
        Check(fc.ClusterCount == 0, "foe clusters (off): no labels");

        // optional HUD widgets formatting (v1.6.0)
        string clk = MapMath.FormatClock(579 * 24 + 17.75);
        Check(clk == "#E8B84BDAY #FFFFFF580  #E8B84BTIME #FFFFFF5:45 PM"
              && CaptionText.ShownLength(clk) == "DAY 580  TIME 5:45 PM".Length,
            "clock formatting and colour-tag shown length: " + clk);
        Check(MapMath.FormatClock(0.0, includeTimeWord: false) == "#E8B84BDAY #FFFFFF1  #FFFFFF12:00 AM"
              && MapMath.FormatClock(double.NaN) == "",
            "clock midnight day 1 and NaN guard");
        Check(MapMath.FormatCats(2642) == "#E8B84BCATS #FFFFFF2,642" && MapMath.FormatCats(0) == "#E8B84BCATS #FFFFFF0",
            "cats formatting");
        Check(MapMath.FormatSpeed(true, 2) == "#FF5E52PAUSED" && MapMath.FormatSpeed(false, 2) == "#E8B84BSPEED #FFFFFF2x"
              && MapMath.FormatSpeed(false, 5) == "#E8B84BSPEED #FFFFFF5x",
            "speed formatting");
        Check(Math.Abs(MapMath.FedPercent(3f) - 100f) < 0.01f && Math.Abs(MapMath.FedPercent(1.5f) - 50f) < 0.01f
              && Math.Abs(MapMath.FedPercent(150f) - 50f) < 0.01f && MapMath.FedPercent(float.NaN) < 0f,
            "fed percent handles 0..3 and 0..300 scales");
        MapMath.BarRampRgb(0f, out float r0, out float g0, out _);
        MapMath.BarRampRgb(1f, out float r1, out float g1, out _);
        Check(r0 > g0 && g1 > r1 && MapMath.MeterTextColour(85f).StartsWith("0.54") && MapMath.MeterTextColour(20f).StartsWith("0.98"),
            "bar colour ramp goes red to green");
    }

    // ------------------------------------------------------------------------------------------------ the captions

    /// <summary>Meitou.Data.Ui.MyGuiTextTags.Parse, as the engine reads a caption:
    /// "##" -> '#'; '#' + six hex digits -> a colour run from here; any other '#' prints.</summary>
    static (string Text, List<(int Start, int Rgb)> Runs) MyGuiParse(string text)
    {
        var runs = new List<(int, int)>();
        if (!text.Contains('#')) return (text, runs);
        var sb = new StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] != '#')
            {
                sb.Append(text[i]);
                continue;
            }
            if (i + 1 < text.Length && text[i + 1] == '#')
            {
                sb.Append('#');
                i++;
                continue;
            }
            if (i + 7 <= text.Length && int.TryParse(text.AsSpan(i + 1, 6), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out int v))
            {
                if (runs.Count > 0 && runs[^1].Item1 == sb.Length) runs[^1] = (sb.Length, v);
                else runs.Add((sb.Length, v));
                i += 6;
                continue;
            }
            sb.Append('#');
        }
        return (sb.ToString(), runs);
    }

    /// <summary>A name is shown as it is: whatever it holds, the HUD prints the same characters, in no colour of its own,
    /// and the size guess counts the characters shown.</summary>
    static void CaptionTags()
    {
        foreach (string text in new[] { "Hub", "The Hub", "a #1 place", "#ff0000", "##", "#", "x#", "50% #abcdef off", "#12345", "A#B#C", "" })
        {
            string caption = CaptionText.Escape(text);
            var (shown, runs) = MyGuiParse(caption);
            Check(shown == text && runs.Count == 0, "caption: '" + text + "' is shown as it is, with no colour run ('" + caption + "' -> '" + shown + "')");
            Check(CaptionText.ShownLength(caption) == text.Length, "caption: the size guess counts the shown characters of '" + text + "'");
        }
        string plain = "Plain name";
        Check(ReferenceEquals(CaptionText.Escape(plain), plain), "caption: a text without '#' is the same object");
        Check(CaptionText.Escape(null) == "" && CaptionText.ShownLength(null) == 0, "caption: no text, no caption");
    }
}
