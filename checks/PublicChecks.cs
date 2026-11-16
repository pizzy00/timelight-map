// Console checks of what the public build added to the pure code: the game's place names (KnownPlaces), the cold start of
// a follow target (a cut to the squad, never a sweep from the origin), and the shipped sample file against MapPrefs.Sample.
using System;
using System.IO;
using TimeLightMap;

static class PublicChecks
{
    public static void Run(Action<bool, string> Check)
    {
        // ---- KnownPlaces: which name, when ----
        Check(KnownPlaces.Name(false, false, "The Hub", "Unknown town") == null, "an undiscovered town has no label");
        Check(KnownPlaces.Name(false, true, "The Hub", "") == null, "explored but not discovered: still none (the game's own map rule)");
        Check(KnownPlaces.Name(true, false, "The Hub", "Unknown town") == "Unknown town", "discovered, not explored: the unexplored name");
        Check(KnownPlaces.Name(true, false, "The Hub", "") == "The Hub", "discovered, no unexplored name: its name");
        Check(KnownPlaces.Name(true, false, "The Hub", null) == "The Hub", "a null unexplored name: its name");
        Check(KnownPlaces.Name(true, true, "The Hub", "Unknown town") == "The Hub", "explored: its name");
        Check(KnownPlaces.Name(true, true, "", "") == null && KnownPlaces.Name(true, true, null, null) == null && KnownPlaces.Name(true, true, "   ", null) == null,
            "no name: no label");
        Check(KnownPlaces.Name(true, true, " Squin ", null) == "Squin", "a name is trimmed");
        string same = "Stack";
        Check(ReferenceEquals(KnownPlaces.Name(true, true, same, null), same), "a clean name comes back as the same object (no garbage a frame)");
        Check(KnownPlaces.Shows(true, 50000) && KnownPlaces.Shows(true, 1500), "a town is named at every zoom");
        Check(KnownPlaces.Shows(false, 1500) && KnownPlaces.Shows(false, KnownPlaces.TownsOnlySpan) && !KnownPlaces.Shows(false, KnownPlaces.TownsOnlySpan + 1),
            "a small place is named up to 2 km across, not beyond");

        // ---- KnownPlaces.Near never throws out a place that is on the picture ----
        var rng = new Random(11);
        bool kept = true;
        int onPicture = 0;
        for (int i = 0; i < 20000; i++)
        {
            var v = new MapView { Cx = rng.NextDouble() * 80000 - 40000, Cz = rng.NextDouble() * 80000 - 40000, Cy = rng.NextDouble() * 400, Valid = true };
            double aspect = 0.3 + rng.NextDouble() * 3;
            v.Span = 100 + rng.NextDouble() * 40000;
            v.SpanZ = v.Span / aspect;
            v.Angles(rng.NextDouble() * 360, 35 + rng.NextDouble() * 55);
            const float w = 700;
            float h = (float)(w / aspect);
            // a place somewhere around the view, at the height the view looks at
            double x = v.Cx + (rng.NextDouble() * 4 - 2) * v.Span, z = v.Cz + (rng.NextDouble() * 4 - 2) * v.Span;
            v.ToPixel(x, v.Cy, z, w, h, out float px, out float py);
            bool inside = px >= 0 && py >= 0 && px <= w && py <= h;
            if (inside) onPicture++;
            if (inside && !KnownPlaces.Near(v, x, z)) kept = false;
        }
        Check(kept && onPicture > 500, "20000 random views (any turn, tilt, shape): every place on the picture passes the cheap test (" + onPicture + " were on it)");
        var near = new MapView { Cx = 0, Cz = 0, Span = 1500, SpanZ = 1050, Valid = true };
        near.Angles(0, 90);
        Check(!KnownPlaces.Near(near, 5000, 0) && !KnownPlaces.Near(near, 0, -5000) && KnownPlaces.Near(near, 700, 500), "and a far place is thrown out");

        // ---- a follow target begun cold (the options run before anything is drawn) ----
        const double dt = 1 / 60.0;
        var t = new ViewTarget { Mode = TargetMode.Follow, FollowSpan = 1500, Ease = 0.5 };
        t.Step(dt, true, 43000, -18000);
        Check(Math.Abs(t.Cx - 43000) < 1e-6 && Math.Abs(t.Cz + 18000) < 1e-6 && Math.Abs(t.Span - 1500) < 1e-6 && !t.WaitingForPoint,
            "cold follow with a squad: the first frame is on the squad at the asked span (" + t.Cx + ", " + t.Cz + ", " + t.Span + ")");
        // nobody to follow at first: it waits, then cuts to the first point (no sweep from the origin)
        var wait = new ViewTarget { Mode = TargetMode.Follow, FollowSpan = 1500, Ease = 0.5 };
        for (int i = 0; i < 30; i++) wait.Step(dt, false, 0, 0);
        Check(wait.WaitingForPoint && wait.Started, "cold follow with nobody: waiting for a point");
        wait.Step(dt, true, 43000, -18000);
        Check(Math.Abs(wait.Cx - 43000) < 1e-6 && Math.Abs(wait.Cz + 18000) < 1e-6 && !wait.WaitingForPoint, "the first point after the wait is a cut (" + wait.Cx + ")");
        // from then on it is an ordinary follow: a walking squad is trailed smoothly, no further cut
        double px2 = 43000, biggest = 0, last = wait.Cx;
        for (int i = 0; i < 600; i++)
        {
            px2 += 50 * dt;
            wait.Step(dt, true, px2, -18000);
            biggest = Math.Max(biggest, Math.Abs(wait.Cx - last));
            last = wait.Cx;
        }
        Check(biggest < 50 * dt * 1.5 && px2 - wait.Cx > 5, "after the cut the view trails the walking squad smoothly (" + biggest.ToString("0.000") + " a frame at most)");
        // a started target with no point (the squad is gone for a moment) is not 'waiting': the view holds, and the squad's return is eased
        var held = new ViewTarget { Mode = TargetMode.Follow, FollowSpan = 1500, Ease = 0.5 };
        held.Start(1000, 1000, 1500, 0);
        held.Step(dt, false, 0, 0);
        Check(!held.WaitingForPoint && Math.Abs(held.Cx - 1000) < 1e-9, "a started target without a point holds and does not wait");
        held.Step(dt, true, 1400, 1000);
        Check(held.Cx < 1100, "and its first point is eased, not a cut (" + held.Cx.ToString("0.0") + ")");
        // Stop clears the wait
        wait = new ViewTarget { Mode = TargetMode.Follow, FollowSpan = 1500 };
        wait.Step(dt, false, 0, 0);
        wait.Stop();
        Check(!wait.WaitingForPoint && !wait.Started, "Stop ends the wait");

        // ---- the sample file shipped with the mod is MapPrefs.Sample, byte for byte ----
        string shipped = Path.Combine(AppContext.BaseDirectory, "timelight-map.cfg.sample");
        Check(File.Exists(shipped) && File.ReadAllText(shipped) == MapPrefs.Sample, "package\\timelight-map.cfg.sample equals MapPrefs.Sample");
    }
}
