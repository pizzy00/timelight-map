// Console checks of what 1.3.0 added to the pure code: the strict on/off words, the shape, quality, zoom step and hotkey
// words, the round and the big map's rectangles, the config file written back and read again, the routes (RoutePlan), the
// hurt rings' levels and the round map's circle (MapShape), the place labels kept inside a round map, and the big map's
// wider follow (ViewTarget.SpanFactor).
using System;
using System.Collections.Generic;
using System.Linq;
using TimeLightMap;

static class V130Checks
{
    static Action<bool, string> _check;

    static void Check(bool ok, string what) => _check(ok, what);

    public static void Run(Action<bool, string> check)
    {
        _check = check;
        Words();
        Keys();
        Rects();
        FileRoundTrip();
        Routes();
        Shapes();
        RoundPlaces();
        BigFollow();
    }

    static void Words()
    {
        bool ok = true;
        foreach (var (w, v) in new[] { ("on", true), ("OFF", false), ("true", true), ("False", false), ("1", true), ("0", false), ("yes", true), ("No", false), (" on ", true) })
            ok &= MapPrefs.OnOff(w, out bool on) && on == v;
        Check(ok, "on/off: on off true false 1 0 yes no, any case");
        Check(!MapPrefs.OnOff("maybe", out bool o1) && !o1 && !MapPrefs.OnOff("", out _) && !MapPrefs.OnOff(null, out _) && !MapPrefs.OnOff("onn", out _),
            "on/off: anything else is refused (1.2.0 took any word as off)");
        Check(MapPrefs.Shape("square", out bool r) && !r && MapPrefs.Shape("Round", out r) && r && MapPrefs.Shape("circle", out r) && r && !MapPrefs.Shape("oval", out _),
            "shape words");
        Check(MapPrefs.PictureLayers("tofc", out string l) && l == "tofc" && MapPrefs.PictureLayers("CT", out l) && l == "tc" && MapPrefs.PictureLayers("cot", out l) && l == "toc",
            "picture layers: any order and case, kept as t o f c");
        Check(!MapPrefs.PictureLayers("", out _) && !MapPrefs.PictureLayers("tx", out _) && !MapPrefs.PictureLayers("tt", out _) && !MapPrefs.PictureLayers("tofcx", out _)
              && !MapPrefs.PictureLayers(null, out _), "picture layers: no other letter, none twice (1.2.0 took anything)");
        Check(MapPrefs.Flip("none", out bool fx, out bool fy) && !fx && !fy && MapPrefs.Flip("x", out fx, out fy) && fx && !fy && MapPrefs.Flip("Y", out fx, out fy) && !fx && fy
              && MapPrefs.Flip("yx", out fx, out fy) && fx && fy && !MapPrefs.Flip("z", out _, out _) && !MapPrefs.Flip("", out _, out _), "flip words (1.2.0 took anything)");

        Check(MapPrefs.Quality("high", out float s, out float f, out float m, out string ql) && s == 1f && f == 30f && m == 0f && ql == "tofc",
            "quality high is what 1.2.0 drew");
        Check(MapPrefs.Quality("medium", out s, out f, out m, out ql) && s < 1f && f < 30f && m > 0f && MapPrefs.Quality("LOW", out s, out f, out m, out ql) && ql == "toc"
              && !MapPrefs.Quality("ultra", out _, out _, out _, out _), "quality medium and low cost less; nothing else");
        foreach (string q in new[] { "high", "medium", "low" })
        {
            MapPrefs.Quality(q, out s, out f, out m, out ql);
            Check(MapPrefs.QualityName(s, f, m, ql) == q, "quality " + q + " is named back");
        }
        Check(MapPrefs.QualityName(1f, 25f, 0f, "tofc") == "custom", "quality: settings of no preset are 'custom'");

        Check(MapPrefs.ZoomedMetres(150, true) == 100 && MapPrefs.ZoomedMetres(100, false) == 150 && MapPrefs.ZoomedMetres(100, true) == 66.7,
            "zoom in and out: steps of 1.5 (" + MapPrefs.ZoomedMetres(100, true) + ")");
        Check(MapPrefs.ZoomedMetres(10, true) == MapPrefs.MinZoom && MapPrefs.ZoomedMetres(5000, false) == MapPrefs.MaxZoom
              && MapPrefs.ZoomedMetres(double.NaN, true) == 100, "zoom steps stay within 10..5000; no zoom: from the default");
        double z = 150;
        for (int i = 0; i < 30; i++) z = MapPrefs.ZoomedMetres(z, i < 15);
        Check(z >= MapPrefs.MinZoom && z <= MapPrefs.MaxZoom, "30 zoom steps stay within the limits");
    }

    static void Keys()
    {
        Check(MapPrefs.KeySpec("N", out bool c, out bool s, out bool a, out string n) && !c && !s && !a && n == "N", "key N");
        Check(MapPrefs.KeySpec("shift+n", out c, out s, out a, out n) && !c && s && !a && n == "N", "key shift+n");
        Check(MapPrefs.KeySpec("Ctrl+Shift+EQUAL", out c, out s, out a, out n) && c && s && !a && n == "Equal", "key Ctrl+Shift+EQUAL: " + n);
        Check(MapPrefs.KeySpec("=", out c, out s, out a, out n) && n == "Equal" && MapPrefs.KeySpec("-", out _, out _, out _, out n) && n == "Minus", "keys = and -");
        Check(MapPrefs.KeySpec("ctrl+-", out c, out s, out a, out n) && c && n == "Minus", "key ctrl+-");
        Check(MapPrefs.KeySpec("Alt+F5", out c, out s, out a, out n) && a && n == "F5", "key Alt+F5");
        Check(MapPrefs.KeySpec("off", out _, out _, out _, out n) && n == null && MapPrefs.KeySpec("None", out _, out _, out _, out n) && n == null, "key off: no key");
        Check(!MapPrefs.KeySpec("", out _, out _, out _, out _) && !MapPrefs.KeySpec("hyper+N", out _, out _, out _, out _) && !MapPrefs.KeySpec("Shift+", out _, out _, out _, out _)
              && !MapPrefs.KeySpec("N N", out _, out _, out _, out _) && !MapPrefs.KeySpec(null, out _, out _, out _, out _), "keys: nothing else");
        Check(MapPrefs.KeyText(true, true, false, "N") == "Ctrl+Shift+N" && MapPrefs.KeyText(false, false, false, null) == "off", "key texts");
        bool round = MapPrefs.KeyActions.Length == 4 && MapPrefs.KeyDefaults.Length == 4;
        foreach (string k in MapPrefs.KeyDefaults)
            round &= MapPrefs.KeySpec(k, out c, out s, out a, out n) && MapPrefs.KeyText(c, s, a, n) == k;
        Check(round, "the default keys read back as written");
        Check(MapPrefs.KeyDefaults.SequenceEqual(new[] { "N", "Shift+N", "Equal", "Minus" }), "the default keys: N, Shift+N, =, -");
    }

    static void Rects()
    {
        MapPrefs.Rect(2, MapPrefs.Medium, 24, 2560, 1440, out int x, out int y, out int w, out int h, round: true);
        Check(w == 498 && h == 498 && x == 2560 - 24 - 498 && y == 24, "round, medium, top right: square " + x + "," + y + " " + w + "x" + h);
        MapPrefs.FreeSquare(1832, 63, 712, 499, 2560, 1440, out x, out y, out int s);
        Check(s == 499 && x == 2544 - 499 && y == 63, "round in a wide free rect at the right: the circle keeps to the right edge: " + x + "," + y + " " + s);
        MapPrefs.FreeSquare(16, 63, 712, 499, 2560, 1440, out x, out y, out s);
        Check(s == 499 && x == 16 && y == 63, "round in a wide free rect at the left: keeps to the left edge");
        MapPrefs.FreeSquare(924, 900, 712, 499, 2560, 1440, out x, out y, out s);
        Check(s == 499 && x == 924 + 106 && y == 900, "round in a wide free rect in the middle: the middle (" + x + ")");
        MapPrefs.FreeSquare(40, 600, 300, 800, 2560, 1440, out x, out y, out s);
        Check(s == 300 && x == 40 && y == 600 + 500, "round in a tall free rect at the bottom: keeps to the bottom edge");
        MapPrefs.FreeSquare(100, 100, 400, 400, 2560, 1440, out x, out y, out s);
        Check((x, y, s) == (100, 100, 400), "round in a square free rect: unchanged");
        MapPrefs.BigRect(2560, 1440, out x, out y, out w, out h);
        Check(h == 1037 && Math.Abs(w - h * MapPrefs.Aspect) <= 1 && Math.Abs(2 * x + w - 2560) <= 1 && Math.Abs(2 * y + h - 1440) <= 1,
            "big map, 2560x1440: 72% high, in the middle: " + x + "," + y + " " + w + "x" + h);
        MapPrefs.BigRect(2560, 1440, out x, out y, out w, out h, round: true);
        Check(w == h && h == 1037 && Math.Abs(2 * x + w - 2560) <= 1, "big round map: square, in the middle");
        MapPrefs.BigRect(800, 1440, out x, out y, out w, out h);
        Check(w <= 752 && x >= 0 && y >= 0 && y + h <= 1440, "big map in a narrow window: never wider than 94% of it: " + w);
        Check(MapPrefs.RectText("10 20 300 200", out x, out y, out w, out h) && (x, y, w, h) == (10, 20, 300, 200)
              && MapPrefs.RectText("10,20, 300,200", out x, out y, out w, out h) && w == 300, "rect text: spaces or commas");
        Check(!MapPrefs.RectText("1 2 3", out _, out _, out _, out _) && !MapPrefs.RectText("0 0 10 10", out _, out _, out _, out _)
              && !MapPrefs.RectText("-1 0 100 100", out _, out _, out _, out _) && !MapPrefs.RectText("a b c d", out _, out _, out _, out _), "rect text: refused when bad");
    }

    static string Flat(List<string[]> list) => string.Join("|", list.Select(l => string.Join(" ", l)));

    static void FileRoundTrip()
    {
        // what 'topcam save file' writes reads back as the same options, with no fault
        var v = MapPrefs.Defaults();
        v["corner"] = "bottom-left"; v["size"] = "28"; v["shape"] = "round"; v["frame"] = "on"; v["follow"] = "selected"; v["zoom"] = "320.5";
        v["pitch"] = "60"; v["yaw"] = "45"; v["alpha"] = "0.8"; v["quality"] = "medium"; v["routes"] = "off"; v["hurt"] = "off";
        v["key_toggle"] = "Ctrl+N"; v["key_big"] = "off"; v["rect"] = "30 40 500 350";
        string text = MapPrefs.Render(v, "# a header\n");
        var back = MapPrefs.Parse(text, out var faults);
        bool same = faults.Count == 0 && text.StartsWith("# a header\n");
        foreach (string key in MapPrefs.Keys) same &= back[key] == v[key];
        Check(same, "a written config file reads back as the same options (" + string.Join("; ", faults) + ")");
        Check(MapPrefs.Sample == MapPrefs.Render(MapPrefs.Defaults()), "the sample file is the defaults, written");
        // the other words of the file's keys
        var list = MapPrefs.Commands("towns = off\nfoes = off\nhostilelabels = on\ncaravans = off\nroute = off\nwaypoints = off\nbars = on\ntime = on\nmoney = on\ngamespeed = on", out faults);
        string f = Flat(list);
        Check(faults.Count == 0 && f.Contains("|layer towns off|") && f.Contains("|layer foes off|") && f.Contains("|foelabels on|")
              && f.Contains("|layer traders off|") && f.Contains("|layer route off|") && f.Contains("|squadbars on|")
              && f.Contains("|clock on|") && f.Contains("|cats on|") && f.Contains("|speed on|"),
            "the keys' other words: towns, foes, hostilelabels, caravans, route, waypoints, bars, time, money, gamespeed: " + f);
        Check(MapPrefs.FoeLabels("auto", out var flm) && flm == FoeLabelMode.Auto
              && MapPrefs.FoeLabels("smart", out flm) && flm == FoeLabelMode.Auto
              && MapPrefs.FoeLabels("on", out flm) && flm == FoeLabelMode.On
              && MapPrefs.FoeLabels("all", out flm) && flm == FoeLabelMode.On
              && MapPrefs.FoeLabels("off", out flm) && flm == FoeLabelMode.Off
              && !MapPrefs.FoeLabels("maybe", out _),
            "foelabels words: auto/smart, on/all, off; bad refused");
        Check(Flat(MapPrefs.CommandsOf("key_big", "Shift+N", false)) == "bind big Shift+N" && MapPrefs.CommandsOf("rect", "1 2 30 40", false).Count == 0
              && Flat(MapPrefs.CommandsOf("hostiles", "off", false)) == "layer foes off" && Flat(MapPrefs.CommandsOf("places", "on", false)) == "layer towns on",
            "single options as commands (the settings page's changes)");
        // map = true/false/yes/no/1/0 normalise to on/off so TopCam.Command accepts them
        bool mapNorm = true;
        foreach (var (word, want) in new[] { ("true", "on"), ("false", "off"), ("yes", "on"), ("no", "off"), ("1", "on"), ("0", "off") })
        {
            var cmds = MapPrefs.Commands("map = " + word, out var mf);
            mapNorm &= mf.Count == 0 && cmds[^1].Length == 1 && cmds[^1][0] == want;
            var one = MapPrefs.CommandsOf("map", word, false);
            mapNorm &= one.Count == 1 && one[0].Length == 1 && one[0][0] == want;
        }
        Check(mapNorm, "map = true/false/yes/no/1/0 becomes the command 'on' or 'off'");
        Check(MapPrefs.Commands(MapPrefs.Defaults()).Count == MapPrefs.Commands(null, out _).Count, "the options as values and as a file give the same commands");
    }

    static void Routes()
    {
        var plan = new RoutePlan();
        plan.BeginFrame();
        plan.Start(0, 0);
        for (int i = 1; i <= 50; i++) plan.Add(i, 0, false);
        bool drawn = plan.Finish();
        bool legs = true;
        for (int j = 1; j < plan.Count - 1; j++) legs &= plan.X[j] - plan.X[j - 1] >= RoutePlan.MinStep;
        Check(drawn && plan.Count == 10 && plan.X[plan.Count - 1] == 50 && legs, "a route: legs of at least 6 pixels, the end kept: " + plan.Count);

        // the same way from near the same spot: one line
        plan.Start(10, 0);
        plan.Add(30, 2, false);
        plan.Add(55, 3, false);
        Check(!plan.Finish() && plan.Skipped == 1, "a man going the same way from close by: not drawn again");
        plan.Start(100, 0);
        plan.Add(50, 0, false);
        Check(plan.Finish(), "the same end from far off: its own route");
        plan.Start(0, 0);
        plan.Add(0, 100, false);
        Check(plan.Finish(), "the same start, another end: its own route");

        // a long path keeps its end
        plan.BeginFrame();
        plan.Start(0, 0);
        for (int i = 1; i <= 200; i++) plan.Add(i * 10, 0, i > 150);
        Check(plan.Finish() && plan.Count == RoutePlan.MaxLegs + 1 && plan.X[plan.Count - 1] == 2000 && plan.Far[plan.Count - 1], "a long path: 24 legs, its end kept, the far part marked");

        // short, empty, broken
        plan.Start(0, 0);
        plan.Add(2, 0, false);
        Check(plan.Finish() && plan.Count == 2, "a path that ends within a step: one short leg to its end");
        plan.Start(500, 500);
        Check(!plan.Finish(), "no path: nothing drawn");
        plan.Start(700, 700);
        plan.Add(float.NaN, 3, false);
        plan.Add(760, 700, false);
        Check(plan.Finish() && plan.Count == 2, "a broken point is left out");
        plan.Start(float.NaN, 0);
        plan.Add(10, 10, false);
        Check(!plan.Finish(), "no start: nothing drawn");
        plan.BeginFrame();
        Check(plan.Routes == 0 && plan.Skipped == 0, "a new frame forgets the routes");
        for (int r = 0; r < RoutePlan.MaxRoutes + 4; r++)
        {
            plan.Start(r * 100, 0);
            plan.Add(r * 100, 50, false);
            plan.Finish();
        }
        Check(plan.Routes == RoutePlan.MaxRoutes, "more routes than it remembers: no overflow");
    }

    static void Shapes()
    {
        Check(MapShape.HurtLevel(1, 1) == 0 && MapShape.HurtLevel(0.5, 1) == 1 && MapShape.HurtLevel(1, 0.2) == 2 && MapShape.HurtLevel(0.6, 0.6) == 0
              && MapShape.HurtLevel(0.3, 1) == 1 && MapShape.HurtLevel(1, -3) == 2 && MapShape.HurtLevel(double.NaN, 0.7) == 0 && MapShape.HurtLevel(double.NaN, double.NaN) == 0,
            "hurt: under 0.6 orange, under 0.3 red, the worse of blood and the worst part; unknown = whole");
        Check(MapShape.Radius(300, 200) == 99.5f && MapShape.Radius(0, 0) == 0f, "the round map's radius");
        Check(MapShape.BoxInCircle(140, 140, 160, 160, 150, 150, 50) && !MapShape.BoxInCircle(100, 100, 200, 120, 150, 150, 50)
              && !MapShape.BoxInCircle(149, 149, 151, 151, 150, 150, 0), "a box inside the circle, one across its edge, no circle");
    }

    static void RoundPlaces()
    {
        var rng = new Random(13);
        bool clean = true;
        int shown = 0;
        for (int frame = 0; frame < 300; frame++)
        {
            var p = new PlaceLayout { CircleR = 150f };
            int n = rng.Next(1, 12);
            for (int i = 0; i < n; i++)
            {
                float x = (float)rng.NextDouble() * 300, y = (float)rng.NextDouble() * 300;
                p.Key[i] = i;
                (p.AnchorX[i], p.AnchorY[i], p.Above[i], p.W[i], p.H[i], p.Priority[i]) = (x, y, 7f, 40f + (float)rng.NextDouble() * 80f, 20f, rng.Next(2));
            }
            p.Count = n;
            p.Solve(300, 300);
            for (int i = 0; i < n; i++)
            {
                if (!p.Shown[i]) continue;
                shown++;
                if (!MapShape.BoxInCircle(p.X[i], p.Y[i], p.X[i] + p.W[i], p.Y[i] + p.H[i], 150, 150, 150)) clean = false;
            }
        }
        Check(clean && shown > 300, "a round map: 300 random frames, every shown place name inside the circle (" + shown + " shown)");

        // round-map squad labels: a dot near the curved right rim flips its name left to stay inside the circle
        var lbl = new LabelLayout { CircleR = 149.5f, Count = 1 };
        (lbl.Key[0], lbl.DotX[0], lbl.DotY[0], lbl.DotR[0], lbl.W[0], lbl.H[0]) = (1, 225, 70, 5, 65, 20);
        lbl.Solve(300, 300);
        Check(lbl.Left[0] && MapShape.BoxInCircle(lbl.X[0], lbl.Y[0], lbl.X[0] + lbl.W[0], lbl.Y[0] + lbl.H[0], 150, 150, 148.5f),
            "round map: a name near the curved right rim goes left to stay inside the circle");

        // round-map stacked column near the curved right rim also flips left
        lbl.Forget();
        lbl.Count = 2;
        (lbl.Key[0], lbl.DotX[0], lbl.DotY[0], lbl.DotR[0], lbl.W[0], lbl.H[0]) = (1, 220, 75, 5, 65, 20);
        (lbl.Key[1], lbl.DotX[1], lbl.DotY[1], lbl.DotR[1], lbl.W[1], lbl.H[1]) = (2, 224, 80, 5, 65, 20);
        lbl.Solve(300, 300);
        Check(lbl.Stacked[0] && lbl.Left[0] && MapShape.BoxInCircle(lbl.X[0], lbl.Y[0], lbl.X[0] + lbl.W[0], lbl.Y[0] + lbl.H[0], 150, 150, 148.5f)
              && MapShape.BoxInCircle(lbl.X[1], lbl.Y[1], lbl.X[1] + lbl.W[1], lbl.Y[1] + lbl.H[1], 150, 150, 148.5f),
            "round map: a crowded column near the curved right rim goes left inside the circle");

        // Spring ignores non-finite dt or target (never poisons to NaN)
        var sp = new Spring();
        sp.Reset(42);
        sp.Step(100, 0.5, double.PositiveInfinity);
        sp.Step(double.NaN, 0.5, 0.016);
        Check(sp.Value == 42 && sp.Velocity == 0, "spring: non-finite dt or target leaves state intact");

        // KnownPlaces.Near keeps an elevated/valley town visible on a tilted zoomed-in view
        var tilted = new MapView { Cx = 0, Cy = 0, Cz = 0, Span = 300, SpanZ = 210, Valid = true };
        tilted.Angles(0, 35);
        tilted.ToPixel(0, -300, -400, 300, 210, out float tpx, out float tpy);
        Check(tpx >= 0 && tpx <= 300 && tpy >= 0 && tpy <= 210 && KnownPlaces.Near(tilted, 0, -400),
            "tilted zoomed-in view: a valley town visible on screen passes KnownPlaces.Near");

        // 11-person squad in a 320x320 round map with Scale bar, North arrow, and Catun place label:
        // splits into two balanced wings, stays inside the circle and clear of chrome, and Catun never crosses a leader line
        const float dim = 320f, rad = 159.5f;
        var squad = new LabelLayout { CircleR = rad, Count = 11 };
        float[] widths = { 58, 44, 34, 44, 44, 44, 44, 44, 48, 80, 72 };
        for (int i = 0; i < 11; i++)
            (squad.Key[i], squad.DotX[i], squad.DotY[i], squad.DotR[i], squad.W[i], squad.H[i]) =
                (i + 1, 158f + (i % 3) * 3f, 158f + (i / 3) * 2f, i == 0 ? 6f : 5f, widths[i], 20f);
        float scaleY0 = dim * 0.5f + rad * 0.80f - 20f;
        squad.AddObstacle(dim * 0.15f, scaleY0, dim * 0.85f, dim);
        squad.AddObstacle(225f, 12f, 273f, 64f);
        squad.Solve(dim, dim);
        int leftWing = 0, rightWing = 0;
        bool allIn = true, clearChrome = true, noLabelOverlap = true;
        for (int i = 0; i < 11; i++)
        {
            if (squad.Left[i]) leftWing++; else rightWing++;
            allIn &= MapShape.BoxInCircle(squad.X[i], squad.Y[i], squad.X[i] + squad.W[i], squad.Y[i] + squad.H[i], dim * 0.5f, dim * 0.5f, rad - 1f);
            clearChrome &= squad.Y[i] + squad.H[i] <= scaleY0;
            for (int j = i + 1; j < 11; j++)
                if (squad.X[i] < squad.X[j] + squad.W[j] && squad.X[j] < squad.X[i] + squad.W[i]
                    && squad.Y[i] < squad.Y[j] + squad.H[j] && squad.Y[j] < squad.Y[i] + squad.H[i]) noLabelOverlap = false;
        }
        Check(squad.Clusters == 1 && leftWing == 5 && rightWing == 6 && allIn && clearChrome && noLabelOverlap,
            "11-person round map: balanced 6+5 wings, all inside circle, clear of scale bar, zero overlaps");

        // PlaceLayout avoids squad leader lines (Catun near the squad knot never has leader lines slashing through it)
        var pl = new PlaceLayout { CircleR = rad, Count = 1 };
        pl.AddObstacle(dim * 0.15f, scaleY0, dim * 0.85f, dim);
        pl.AddObstacle(225f, 12f, 273f, 64f);
        for (int i = 0; i < 11; i++)
        {
            pl.AddObstacle(squad.DotX[i] - 8f, squad.DotY[i] - 8f, squad.DotX[i] + 8f, squad.DotY[i] + 8f);
            pl.AddObstacle(squad.X[i] - 4f, squad.Y[i], squad.X[i] + squad.W[i] + 4f, squad.Y[i] + squad.H[i]);
            pl.AddLineObstacle(squad.DotX[i], squad.DotY[i], squad.LeadX[i], squad.LeadY[i]);
        }
        pl.Key[0] = PlaceLayout.KeyOf("Catun", 0, 0);
        (pl.AnchorX[0], pl.AnchorY[0], pl.Above[0], pl.W[0], pl.H[0], pl.Priority[0]) = (160f, 165f, 7f, 52f, 20f, 1);
        pl.Solve(dim, dim);
        Check(pl.Shown[0] && MapShape.BoxInCircle(pl.X[0], pl.Y[0], pl.X[0] + pl.W[0], pl.Y[0] + pl.H[0], dim * 0.5f, dim * 0.5f, rad - 1f),
            "Catun beside 11-person cluster: shown inside circle, clear of names, dots and leader lines");

        // 16-person squad with wide names in a 374x374 round map: circular-chord tucking keeps all 16 inside the circle with 0 overlaps
        const float bigDim = 374f, bigRad = 186.5f;
        var bigSq = new LabelLayout { CircleR = bigRad, Count = 16 };
        float[] bigW = { 104, 62, 54, 64, 58, 60, 56, 98, 108, 96, 52, 50, 58, 62, 66, 102 };
        for (int i = 0; i < 16; i++)
            (bigSq.Key[i], bigSq.DotX[i], bigSq.DotY[i], bigSq.DotR[i], bigSq.W[i], bigSq.H[i]) =
                (100 + i, 185f + (i % 4) * 3f, 185f + (i / 4) * 2f, i == 0 ? 6f : 5f, bigW[i], 20f);
        float bigScaleY0 = bigDim * 0.5f + bigRad * 0.80f - 20f;
        bigSq.AddObstacle(bigDim * 0.15f, bigScaleY0, bigDim * 0.85f, bigDim);
        bigSq.AddObstacle(265f, 12f, 313f, 64f);
        bigSq.Solve(bigDim, bigDim);
        bool all16In = true, no16Overlap = true;
        for (int i = 0; i < 16; i++)
        {
            all16In &= MapShape.BoxInCircle(bigSq.X[i], bigSq.Y[i], bigSq.X[i] + bigSq.W[i], bigSq.Y[i] + bigSq.H[i], bigDim * 0.5f, bigDim * 0.5f, bigRad - 1f);
            for (int j = i + 1; j < 16; j++)
                if (bigSq.X[i] < bigSq.X[j] + bigSq.W[j] && bigSq.X[j] < bigSq.X[i] + bigSq.W[i]
                    && bigSq.Y[i] < bigSq.Y[j] + bigSq.H[j] && bigSq.Y[j] < bigSq.Y[i] + bigSq.H[i]) no16Overlap = false;
        }
        Check(bigSq.Clusters == 1 && all16In && no16Overlap,
            "16-person round map with wide rim names: circular-chord tucking keeps all 16 inside circle, zero overlaps");
    }

    static void BigFollow()
    {
        var t = new ViewTarget { Mode = TargetMode.Follow, FollowSpan = 1000, Ease = 0.3 };
        for (int i = 0; i < 300; i++) t.Step(1 / 60.0, true, 0, 0);
        Check(Math.Abs(t.Span - 1000) < 0.5, "following at 1000 units");
        t.SpanFactor = 3;
        t.Step(1 / 60.0, true, 0, 0);
        Check(t.AimSpan == 3000 && t.Span > 1000 && t.Span < 3000, "the big map: the span glides out (" + t.Span.ToString("0") + "), no cut");
        for (int i = 0; i < 300; i++) t.Step(1 / 60.0, true, 0, 0);
        Check(Math.Abs(t.Span - 3000) < 1, "the big map: three times as wide");
        t.SpanFactor = 1;
        for (int i = 0; i < 300; i++) t.Step(1 / 60.0, true, 0, 0);
        Check(Math.Abs(t.Span - 1000) < 0.5 && t.FollowSpan == 1000, "back from the big map: the span it had");

        // auto-zoom: tight huddle zooms in to AutoMinSpan (50 m), spreads smoothly out, ignores a lone straggler, big map overrides
        var xs = new double[] { 0, 15, -15, 5000 };
        var zs = new double[] { 0, 10, -10, 0 };
        double spread = MapMath.Spread(xs, zs, 4, 0, 1200, 1800, 0, 0);
        Check(spread > 15 && spread < 20, "spread ignores a straggler 500 m away (" + spread.ToString("0.0") + ")");
        var az = new ViewTarget { Mode = TargetMode.Follow, FollowSpan = 1500, AutoZoom = true, AutoMinSpan = 500, Ease = 0.5 };
        az.Step(1 / 60.0, true, 100, 200, 18);
        Check(Math.Abs(az.Span - 500) < 1 && Math.Abs(az.AimSpan - 500) < 1, "auto-zoom cold start on tight group: 50 m across (" + az.Span.ToString("0") + ")");
        // minor shuffle stays within deadband (no pumping)
        az.Step(1 / 60.0, true, 100, 200, 50);
        Check(Math.Abs(az.AimSpan - 500) < 1, "auto-zoom deadband holds steady on small shuffle");
        // group spreads out to 20 m radius: glides out to ~89 m (890 units)
        for (int i = 0; i < 600; i++) az.Step(1 / 60.0, true, 100, 200, 200);
        Check(Math.Abs(az.Span - 890) < 2, "auto-zoom spreads out smoothly to 89 m (" + az.Span.ToString("0") + ")");
        // big map overrides auto-zoom to FollowSpan x 3 = 450 m, then closing glides back to tight group
        az.SpanFactor = 3;
        for (int i = 0; i < 600; i++) az.Step(1 / 60.0, true, 100, 200, 18);
        Check(Math.Abs(az.Span - 4500) < 5, "big map overrides auto-zoom to 3x max zoom (" + az.Span.ToString("0") + ")");
        az.SpanFactor = 1;
        for (int i = 0; i < 600; i++) az.Step(1 / 60.0, true, 100, 200, 18);
        Check(Math.Abs(az.Span - 500) < 2, "closing big map glides back into tight 50 m group (" + az.Span.ToString("0") + ")");
        // AutoMinSpan > FollowSpan clamps cleanly to FollowSpan; NaN spread holds steady
        var inv = new ViewTarget { Mode = TargetMode.Follow, FollowSpan = 800, AutoZoom = true, AutoMinSpan = 2000, Ease = 0.5 };
        inv.Step(1 / 60.0, true, 0, 0, 10);
        Check(Math.Abs(inv.Span - 800) < 1 && Math.Abs(inv.AimSpan - 800) < 1, "auto-zoom clamps AutoMinSpan > FollowSpan to FollowSpan");
        inv.Step(1 / 60.0, true, 0, 0, double.NaN);
        Check(double.IsFinite(inv.Span) && Math.Abs(inv.AimSpan - 800) < 1, "auto-zoom handles NaN spread safely");
    }
}
