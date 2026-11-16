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
        var list = MapPrefs.Commands("towns = off\nfoes = off\nroute = off\nwaypoints = off", out faults);
        string f = Flat(list);
        Check(faults.Count == 0 && f.Contains("|layer towns off|") && f.Contains("|layer foes off|") && f.Contains("|layer route off|"),
            "the keys' other words: towns, foes, route, waypoints: " + f);
        Check(Flat(MapPrefs.CommandsOf("key_big", "Shift+N", false)) == "bind big Shift+N" && MapPrefs.CommandsOf("rect", "1 2 30 40", false).Count == 0
              && Flat(MapPrefs.CommandsOf("hostiles", "off", false)) == "layer foes off" && Flat(MapPrefs.CommandsOf("places", "on", false)) == "layer towns on",
            "single options as commands (the settings page's changes)");
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
    }
}
