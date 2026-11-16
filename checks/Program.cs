// Console checks of the mod's pure code (MapMath.cs, MapPrefs.cs: the map's arithmetic and options). No game needed.
using System;
using System.Numerics;
using TimeLightMap;

static class Program
{
    static int _failed, _passed;

    static void Check(bool ok, string what)
    {
        if (ok) _passed++;
        else
        {
            _failed++;
            Console.WriteLine("FAIL  " + what);
        }
    }

    static void Near(double a, double b, double tol, string what) => Check(Math.Abs(a - b) <= tol, what + ": " + a + " vs " + b + " (tol " + tol + ")");

    static int Main(string[] args)
    {
        // dotnet run -c Release -- --write-sample ../package/timelight-map.cfg.sample   writes the sample config file from the code
        if (args.Length == 2 && args[0] == "--write-sample")
        {
            System.IO.File.WriteAllText(args[1], MapPrefs.Sample);
            Console.WriteLine("wrote " + args[1]);
            return 0;
        }
        Springs();
        Projection();
        ProjectionAgainstTheDrawMatrix();
        ScaleBar();
        Texts();
        PackCentre();
        TargetFollow();
        Angles();
        Labels();
        Console.WriteLine("map math, eased view, angles, squad names: " + _passed);
        int at = _passed;
        MapLayerChecks.Run(Check);
        Console.WriteLine("cone, arrows, layers, label words: " + (_passed - at)); at = _passed;
        PlaceChecks.Run(Check);
        Console.WriteLine("place label layout: " + (_passed - at)); at = _passed;
        PrefsChecks.Run(Check);
        Console.WriteLine("options and config file: " + (_passed - at)); at = _passed;
        PublicChecks.Run(Check);
        Console.WriteLine("new in the public build (place names, cold start, sample file): " + (_passed - at));
        at = _passed;
        V130Checks.Run(Check);
        Console.WriteLine("new in 1.3.0 (words, keys, round and big map, config file round trip, routes, hurt, round labels, big follow): " + (_passed - at));
        Console.WriteLine(_failed == 0 ? "ALL " + _passed + " CHECKS PASSED" : _failed + " FAILED, " + _passed + " passed");
        return _failed == 0 ? 0 : 1;
    }

    // ---------------------------------------------------------------------------------------------------------

    static void Springs()
    {
        // reaches the target, never past it (critically damped), from rest
        var s = new Spring();
        s.Reset(0);
        double max = 0;
        for (int i = 0; i < 600; i++)
        {
            s.Step(100, 0.5, 1 / 120.0);
            max = Math.Max(max, s.Value);
        }
        Near(s.Value, 100, 1e-3, "spring reaches its target in 5 s");
        Check(max <= 100 + 1e-9, "spring does not overshoot from rest");

        // the same answer at any frame time (the step is exact for a still target)
        var a = new Spring();
        var b = new Spring();
        a.Reset(0);
        b.Reset(0);
        for (int i = 0; i < 30; i++) a.Step(10, 0.5, 1 / 30.0);
        for (int i = 0; i < 240; i++) b.Step(10, 0.5, 1 / 240.0);
        Near(a.Value, b.Value, 1e-9, "spring is frame-rate independent (value)");
        Near(a.Velocity, b.Velocity, 1e-9, "spring is frame-rate independent (velocity)");

        // a target moving at a constant speed is followed speed x tau behind
        var f = new Spring();
        f.Reset(0);
        double target = 0, dt = 1 / 120.0;
        for (int i = 0; i < 120 * 20; i++)
        {
            target += 60 * dt;
            f.Step(target, 0.5, dt);
        }
        Near(target - f.Value, 60 * 0.5, 0.6, "spring lag = speed x tau");

        // tau 0: a cut; a long frame is safe
        var c = new Spring();
        c.Reset(5);
        c.Step(9, 0, 0.016);
        Near(c.Value, 9, 0, "tau 0 cuts");
        var l = new Spring();
        l.Reset(0);
        l.Step(1, 0.5, 100);
        Near(l.Value, 1, 1e-6, "a very long frame lands on the target");
    }

    static MapView View(double cx, double cy, double cz, double span, double aspect, double yaw, double pitch, bool fx = false, bool fy = false)
    {
        var v = new MapView { Cx = cx, Cy = cy, Cz = cz, Span = span, SpanZ = span / aspect, FlipX = fx, FlipY = fy, Valid = true };
        v.Angles(yaw, pitch);
        return v;
    }

    static void Projection()
    {
        const double w = 712, h = 499;
        var v = View(1000, 50, 2000, 3000, w / h, 0, 90);
        v.ToPixel(1000, 50, 2000, w, h, out float px, out float py);
        Near(px, w / 2, 1e-3, "centre x");
        Near(py, h / 2, 1e-3, "centre y");
        v.ToPixel(1000 + 1500, 50, 2000, w, h, out px, out py);
        Near(px, w, 1e-3, "east edge is the picture's right edge (+X right)");
        v.ToPixel(1000, 50, 2000 - 1500 / (w / h), w, h, out px, out py);
        Near(py, 0, 1e-3, "north edge is the picture's top (-Z up, window y grows downward)");
        v.ToPixel(1000, 999, 2000 + 100, w, h, out px, out py);
        Check(py > h / 2, "south is below the centre");
        v.ToPixel(1000, 999, 2000, w, h, out px, out float py2);
        Near(py2, h / 2, 1e-3, "straight down a height does not move the pixel");

        // yaw 90: east at the top
        v = View(0, 0, 0, 1000, w / h, 90, 90);
        v.ToPixel(100, 0, 0, w, h, out px, out py);
        Check(py < h / 2 && Math.Abs(px - w / 2) < 1e-3, "yaw 90 puts east (+X) at the top");
        v.ToPixel(0, 0, 100, w, h, out px, out py);
        Check(px > w / 2 && Math.Abs(py - h / 2) < 1e-3, "yaw 90 puts south (+Z) on the right");

        // flips
        v = View(0, 0, 0, 1000, w / h, 0, 90, true, false);
        v.ToPixel(100, 0, 0, w, h, out px, out py);
        Check(px < w / 2, "flip x mirrors left and right");
        v = View(0, 0, 0, 1000, w / h, 0, 90, false, true);
        v.ToPixel(0, 0, -100, w, h, out px, out py);
        Check(py > h / 2, "flip y mirrors up and down");

        // tilt: the ground along the picture's up is foreshortened by sin(pitch), a height lifts by cos(pitch)
        v = View(0, 0, 0, 1000, w / h, 0, 45);
        v.ToPixel(0, 0, -100, w, h, out px, out py);
        Near(h / 2 - py, 100 * Math.Sin(Math.PI / 4) / (1000 / (w / h)) * h, 1e-3, "pitch 45: 100 units north");
        v.ToPixel(0, 100, 0, w, h, out px, out py);
        Near(h / 2 - py, 100 * Math.Cos(Math.PI / 4) / (1000 / (w / h)) * h, 1e-3, "pitch 45: 100 units up");
        v.ToPixel(100, 0, 0, w, h, out px, out py);
        Near(px - w / 2, 100.0 / 1000 * w, 1e-3, "pitch 45: 100 units east are as wide as straight down");

        // there and back
        var rng = new Random(7);
        for (int i = 0; i < 200; i++)
        {
            v = View(rng.NextDouble() * 1e5, rng.NextDouble() * 500, rng.NextDouble() * 1e5, 200 + rng.NextDouble() * 5e4, w / h,
                rng.NextDouble() * 360, 35 + rng.NextDouble() * 55, rng.Next(2) == 0, rng.Next(2) == 0);
            double x = v.Cx + (rng.NextDouble() - 0.5) * v.Span, z = v.Cz + (rng.NextDouble() - 0.5) * v.Span, y = v.Cy + rng.NextDouble() * 300;
            v.ToPixel(x, y, z, w, h, out px, out py);
            v.ToWorld(px, py, y, w, h, out double bx, out double bz);
            Check(Math.Abs(bx - x) < v.Span * 2e-5 && Math.Abs(bz - z) < v.Span * 2e-5, "ToWorld undoes ToPixel");
        }
    }

    /// <summary>MapView.ToPixel against the matrices TopCam.DrawTop draws the picture with (the same construction), through the
    /// blit's mapping: clip x, y -> texture u, v (row 0 at the bottom) -> the rect's pixels (y downward).</summary>
    static void ProjectionAgainstTheDrawMatrix()
    {
        const float w = 712, h = 499;
        var rng = new Random(11);
        double worst = 0;
        for (int i = 0; i < 4000; i++)
        {
            double yawDeg = i % 4 == 0 ? 0 : rng.NextDouble() * 360;
            double pitchDeg = i % 5 == 0 ? 90 : 35 + rng.NextDouble() * 55;
            double spanD = 300 + rng.NextDouble() * 59000;
            // the centre near the origin so the float matrix is not the limit of this comparison; the formulas do not depend on it
            var v = View((rng.NextDouble() - 0.5) * 2000, rng.NextDouble() * 300, (rng.NextDouble() - 0.5) * 2000, spanD, w / h, yawDeg, pitchDeg);

            // exactly as DrawTop
            float span = (float)v.Span, spanZ = (float)v.SpanZ, sinPitch = (float)v.SinPitch, cosPitch = (float)v.CosPitch;
            var centre = new Vector3((float)v.Cx, (float)v.Cy, (float)v.Cz);
            var up = new Vector3((float)v.SinYaw, 0f, -(float)v.CosYaw);
            var forward = up * cosPitch - Vector3.UnitY * sinPitch;
            float height = MathF.Max(6000f, span * 2f);
            var view = Matrix4x4.CreateLookAt(centre - forward * height, centre, up);
            var projection = Matrix4x4.CreateOrthographic(span, spanZ, 1f, height * 2f);
            var vp = view * projection;

            var p = new Vector3((float)(v.Cx + (rng.NextDouble() - 0.5) * spanD), (float)(v.Cy + (rng.NextDouble() - 0.3) * 400),
                (float)(v.Cz + (rng.NextDouble() - 0.5) * spanD / (w / h)));
            var clip = Vector4.Transform(new Vector4(p, 1f), vp);
            double u = clip.X / clip.W * 0.5 + 0.5, vTex = clip.Y / clip.W * 0.5 + 0.5;
            double mx = u * w, my = (1 - vTex) * h;      // the blit: vUv = texture uv; the rect's row = (1 - vUv.y) * h
            v.ToPixel(p.X, p.Y, p.Z, w, h, out float px, out float py);
            worst = Math.Max(worst, Math.Max(Math.Abs(px - mx), Math.Abs(py - my)));
            if (i < 400)
            {
                // the depth range holds the ground that the picture shows (a point on the ground plane through the centre)
                Check(clip.Z / clip.W > -1e-3 && clip.Z / clip.W < 1 + 1e-3, "the point is inside the depth range");
            }
        }
        Check(worst < 0.05, "MapView.ToPixel equals the draw matrix to 0.05 px (worst " + worst.ToString("0.0000") + ")");
    }

    static void ScaleBar()
    {
        // 10231 units across 712 px: at most 24% of the width = 245 m -> 200 m
        double upp = 10231.0 / 712;
        Near(MapMath.ScaleMetres(upp, 712 * 0.24), 200, 1e-9, "scale bar of the 10231 view");
        Near(MapMath.ScaleMetres(1500.0 / 712, 712 * 0.24), 20, 1e-9, "scale bar of the 1500 view");
        Near(MapMath.ScaleMetres(40000.0 / 712, 712 * 0.24), 500, 1e-9, "scale bar of the 40000 view");
        Near(MapMath.ScaleMetres(60000.0 / 712, 712 * 0.24), 1000, 1e-9, "scale bar of the 60000 view");
        for (double span = 30; span < 70000; span *= 1.07)
        {
            double u = span / 712, max = 712 * 0.24, m = MapMath.ScaleMetres(u, max), px = m * 10 / u;
            double lead = m / Math.Pow(10, Math.Floor(Math.Log10(m) + 1e-9));
            Check(px <= max + 1e-6 && px > max / 2.5 - 1e-6, "bar length within (max / 2.5, max] at span " + span.ToString("0"));
            Check(Math.Abs(lead - 1) < 1e-6 || Math.Abs(lead - 2) < 1e-6 || Math.Abs(lead - 5) < 1e-6, "bar is 1, 2 or 5 x 10^n at span " + span.ToString("0"));
        }
        Check(MapMath.ScaleMetres(0, 100) == 0 && MapMath.ScaleMetres(double.NaN, 100) == 0, "no bar for a degenerate view");
        // exact powers of ten
        Near(MapMath.ScaleMetres(10, 100), 100, 1e-9, "exactly 100 m fits");
        Near(MapMath.ScaleMetres(10, 1000), 1000, 1e-9, "exactly 1000 m fits");
    }

    static void Texts()
    {
        Check(MapMath.ScaleText(50) == "50 m" && MapMath.ScaleText(500) == "500 m" && MapMath.ScaleText(1000) == "1 km" && MapMath.ScaleText(2000) == "2 km",
            "scale texts");
        Check(MapMath.JsonText("a\"b\\c") == "\"a\\\"b\\\\c\"" && MapMath.JsonText(null) == "null", "json text");
    }

    static void PackCentre()
    {
        var x = new double[] { 0, 100, 200, 5000 };
        var y = new double[] { 10, 10, 10, 900 };
        var z = new double[] { 0, 0, 0, 0 };
        Check(MapMath.Centroid(x, y, z, 4, 0, 1200, 1800, out double cx, out double cy, out double cz), "centroid exists");
        Near(cx, 100, 1e-9, "a man 500 m off is left out");
        Near(cy, 10, 1e-9, "and so is his height");
        x[3] = 1500;      // half way out: half his weight (smoothstep 0.5)
        MapMath.Centroid(x, y, z, 4, 0, 1200, 1800, out cx, out cy, out cz);
        Near(cx, (0 + 100 + 200 + 1500 * 0.5) / 3.5, 1e-9, "a man at 1500 counts half");
        Check(!MapMath.Centroid(x, y, z, 0, 0, 1200, 1800, out cx, out cy, out cz), "nobody: no centroid");
    }

    static void TargetFollow()
    {
        const double dt = 1 / 118.0;
        var t = new ViewTarget { Mode = TargetMode.Follow, FollowSpan = 3000, Ease = 0.5 };
        t.Start(0, 0, 1500, 0);
        // the squad walks east at 50 units a second
        double px = 0, lastCx = 0, biggest = 0, lastSpan = 1500, biggestZoom = 0;
        for (int i = 0; i < 118 * 12; i++)
        {
            px += 50 * dt;
            t.Step(dt, true, px, 0);
            if (i > 0) biggest = Math.Max(biggest, Math.Abs(t.Cx - lastCx));
            biggestZoom = Math.Max(biggestZoom, Math.Abs(Math.Log(t.Span / lastSpan)));
            (lastCx, lastSpan) = (t.Cx, t.Span);
        }
        Near(px - t.Cx, 50 * 0.5, 1.0, "follow: the view trails the squad by speed x ease");
        Near(t.Span, 3000, 1.0, "follow: the span reached its target");
        Check(biggest < 50 * dt * 1.5, "follow: no frame moves the centre more than 1.5 x the squad's own step (" + biggest.ToString("0.000") + ")");
        Check(biggestZoom < 0.012, "follow: the zoom is gradual (" + biggestZoom.ToString("0.0000") + " per frame at most)");
        Check(!t.AtRest, "follow: moving while the squad walks");
        Near(t.AimCx, px, 1e-9, "follow: the aim is the squad");
        Near(t.AimSpan, 3000, 1e-9, "follow: the aim's span is the asked one");

        // the squad stops: the view comes to rest exactly on it
        for (int i = 0; i < 118 * 8; i++) t.Step(dt, true, px, 0);
        Near(t.Cx, px, 1e-6, "follow: at rest on the squad");
        Check(t.AtRest, "follow: at rest flag");

        // a new span (topcam zoom) glides: no jump of the zoom, the centre stays
        t.FollowSpan = 600;
        double worstZoom = 0, last = Math.Log(t.Span);
        for (int i = 0; i < 118 * 8; i++)
        {
            t.Step(dt, true, px, 0);
            worstZoom = Math.Max(worstZoom, Math.Abs(Math.Log(t.Span) - last));
            last = Math.Log(t.Span);
        }
        Near(t.Span, 600, 0.5, "zoom: the new span is reached");
        Check(worstZoom < 0.03, "zoom: never more than 3% a frame (" + worstZoom.ToString("0.0000") + ")");
        Near(t.Cx, px, 1e-6, "zoom: the centre stays on the squad");
        t.FollowSpan = 5;
        for (int i = 0; i < 118 * 8; i++) t.Step(dt, true, px, 0);
        Near(t.Span, ViewTarget.MinSpan, 0.01, "zoom: held at the smallest span");
        t.FollowSpan = 1e9;
        for (int i = 0; i < 118 * 20; i++) t.Step(dt, true, px, 0);
        Near(t.Span, ViewTarget.MaxSpan, 1.0, "zoom: held at the largest span");

        // a short step of the squad between two frames is eased, a teleport (a load) is a cut
        var a = new ViewTarget { Mode = TargetMode.Follow, FollowSpan = 3000, Ease = 0.5 };
        a.Start(0, 0, 3000, 0);
        a.Step(dt, true, 0, 0);
        a.Step(dt, true, 2000, 0);
        Check(a.Cx < 100, "a 200 m step of the squad is eased, not a cut (" + a.Cx.ToString("0.0") + ")");
        a.Step(dt, true, 2000 + 50000, 0);
        Near(a.Cx, 52000, 1.0, "a jump of the squad is a cut");
        a.Step(dt, true, 52000 + ViewTarget.JumpUnits + 1, 100);
        Near(a.Cx, 52000 + ViewTarget.JumpUnits + 1, 1.0, "just over the jump distance: a cut");
        Near(a.Cz, 100, 1.0, "the cut is on both axes");

        // nobody to follow for a while: the view holds where it is
        var hold = new ViewTarget { Mode = TargetMode.Follow, FollowSpan = 2000 };
        hold.Start(500, 600, 2000, 0);
        for (int i = 0; i < 100; i++) hold.Step(dt, false, 0, 0);
        Near(hold.Cx, 500, 1e-9, "no squad: the centre holds");
        Near(hold.Cz, 600, 1e-9, "no squad: on both axes");
        // they went and came back near by: eased from the last place they were seen
        hold.Step(dt, true, 520, 600);
        for (int i = 0; i < 100; i++) hold.Step(dt, false, 0, 0);
        Check(hold.Cx > 500 && hold.Cx <= 520, "gone again: the view goes on to where they were last seen (" + hold.Cx.ToString("0.0") + ")");

        // the height the camera looks at eases to the ground
        var hgt = new ViewTarget { Mode = TargetMode.Follow, FollowSpan = 1000 };
        hgt.Start(0, 0, 1000, 100);
        double hh = 100, step = 0;
        for (int i = 0; i < 118 * 6; i++)
        {
            double was = hh;
            hgt.Step(dt, true, 0, 0);
            hh = hgt.StepHeight(160, dt);
            step = Math.Max(step, Math.Abs(hh - was));
        }
        Near(hh, 160, 0.01, "the looked-at height reaches the ground's");
        Check(step < 1.0, "and never jumps (" + step.ToString("0.000") + " a frame at most for a 60 unit step)");
        Near(hgt.StepHeight(double.NaN, dt), 160, 0.01, "a ground that is not a number is ignored");

        // off: nothing moves
        var off = new ViewTarget();
        off.Step(dt, true, 900, 900);
        Check(off.Mode == TargetMode.None && !off.Started && off.Cx == 0, "no follow: Step does nothing");

        // a cold start (no Start call) begins on the squad
        var c = new ViewTarget { Mode = TargetMode.Follow, FollowSpan = 999 };
        c.Step(dt, true, 777, 888);
        Near(c.Cx, 777, 1e-6, "cold start: on the squad");
        Near(c.Cz, 888, 1e-6, "cold start: on both axes");
        Near(c.Span, 999, 1e-6, "cold start: at the asked span");

        // Stop, then follow again: learnt afresh (a cold cut), the old point is forgotten
        c.Stop();
        Check(c.Mode == TargetMode.None && !c.Started && c.AtRest, "Stop: off and at rest");
        c.Mode = TargetMode.Follow;
        c.Step(dt, true, -4000, 250);
        Near(c.Cx, -4000, 1e-6, "follow again after Stop: a cut to the squad");
    }
    /// <summary>A world point's pixel through the matrices TopCam.DrawTop builds and the blit's mapping.</summary>
    static void MatrixPixel(in MapView v, Vector3 p, float w, float h, out double mx, out double my)
    {
        float span = (float)v.Span, spanZ = (float)v.SpanZ, sinPitch = (float)v.SinPitch, cosPitch = (float)v.CosPitch;
        var centre = new Vector3((float)v.Cx, (float)v.Cy, (float)v.Cz);
        var up = new Vector3((float)v.SinYaw, 0f, -(float)v.CosYaw);
        var forward = up * cosPitch - Vector3.UnitY * sinPitch;
        float height = MathF.Max(6000f, span * 2f);
        var vp = Matrix4x4.CreateLookAt(centre - forward * height, centre, up) * Matrix4x4.CreateOrthographic(span, spanZ, 1f, height * 2f);
        var clip = Vector4.Transform(new Vector4(p, 1f), vp);
        mx = (clip.X / clip.W * 0.5 + 0.5) * w;
        my = (1 - (clip.Y / clip.W * 0.5 + 0.5)) * h;
    }

    /// <summary>One glide to its end: (frames, monotonic towards the target, distance travelled, biggest step).</summary>
    static (int Frames, bool Monotonic, double Travel, double Biggest) Glide(AngleEase a, double dt, double tau, int sign)
    {
        int frames = 0;
        bool mono = true;
        double travel = 0, biggest = 0, last = a.Value;
        while (a.Moving && frames < 100000)
        {
            a.Step(dt, tau);
            double step = a.Value - last;
            // the last step lands on the target as it was asked, which for a yaw may be a whole turn from where the glide ran
            if (!a.Moving) step = Math.IEEERemainder(step, 360);
            mono &= step * sign >= -1e-9;
            travel += Math.Abs(step);
            biggest = Math.Max(biggest, Math.Abs(step));
            last = a.Value;
            frames++;
        }
        return (frames, mono, travel, biggest);
    }

    static void Angles()
    {
        const double dt = 1 / 118.0;
        const float w = 712, h = 499;

        // topcam pitch 55 --over 6: down from 90 in 6 seconds, gently at both ends
        var pitch = new AngleEase(90, false);
        pitch.Set(55, 6);
        Check(pitch.Moving && pitch.Target == 55 && pitch.Value == 90, "a timed tilt starts where it stood");
        var g = Glide(pitch, dt, 0.5, -1);
        Check(g.Monotonic, "pitch 90 -> 55 over 6: monotonic");
        Check(pitch.Value == 55 && !pitch.Moving, "pitch 90 -> 55 over 6: ends exactly on 55");
        Near(g.Frames * dt, 6, 2 * dt, "pitch 90 -> 55 over 6: takes 6 seconds");
        Near(g.Travel, 35, 1e-6, "pitch 90 -> 55 over 6: travels 35 degrees");
        Check(g.Biggest <= 35 * 1.5 / 6 * dt * 1.01, "pitch 90 -> 55 over 6: never faster than smoothstep's 1.5 x the mean (" + (g.Biggest / dt).ToString("0.00") + " deg/s)");

        // topcam pitch 90 (no --over): back by the view's spring, never past straight down
        pitch.Set(90, -1);
        g = Glide(pitch, dt, 0.5, +1);
        Check(g.Monotonic, "pitch 55 -> 90 by the spring: monotonic (no overshoot past 90)");
        Check(pitch.Value == 90 && !pitch.Moving, "pitch 55 -> 90 by the spring: ends exactly on 90");
        Check(g.Frames * dt < 6, "pitch 55 -> 90 by the spring (ease 0.5): done in " + (g.Frames * dt).ToString("0.0") + " s");
        Check(g.Biggest <= 35 * (2 / 0.5) / Math.E * dt * 1.02, "the spring's top speed");

        // --over 0: at once
        pitch.Set(40, 0);
        Check(pitch.Value == 40 && !pitch.Moving, "--over 0 sets at once");
        pitch.Set(40, 6);
        Check(!pitch.Moving, "the same angle again: nothing to do");

        // topcam yaw 30 --over 8, topcam yaw 0 --over 8
        var yaw = new AngleEase(0, true);
        yaw.Set(30, 8);
        g = Glide(yaw, dt, 0.5, +1);
        Check(g.Monotonic && yaw.Value == 30, "yaw 0 -> 30 over 8: monotonic, ends exactly on 30");
        Near(g.Frames * dt, 8, 2 * dt, "yaw 0 -> 30 over 8: takes 8 seconds");
        yaw.Set(0, 8);
        g = Glide(yaw, dt, 0.5, -1);
        Check(g.Monotonic && yaw.Value == 0, "yaw 30 -> 0 over 8: monotonic, ends exactly on 0");

        // the short way round
        yaw = new AngleEase(350, true);
        yaw.Set(10, 8);
        g = Glide(yaw, dt, 0.5, +1);
        Check(g.Monotonic && yaw.Value == 10, "yaw 350 -> 10: on through north, ends exactly on 10");
        Near(g.Travel, 20, 1e-6, "yaw 350 -> 10: 20 degrees, not 340");
        yaw.Set(350, 4);
        g = Glide(yaw, dt, 0.5, -1);
        Check(g.Monotonic && yaw.Value == 350, "yaw 10 -> 350: back through north, ends exactly on 350");
        Near(g.Travel, 20, 1e-6, "yaw 10 -> 350: 20 degrees");
        yaw = new AngleEase(0, true);
        yaw.Set(270, -1);
        g = Glide(yaw, dt, 0.5, -1);
        Check(g.Monotonic && yaw.Value == 270, "yaw 0 -> 270 by the spring: turns the other way, ends exactly on 270");
        Near(g.Travel, 90, 0.01, "yaw 0 -> 270: 90 degrees, not 270");
        yaw = new AngleEase(0, true);
        yaw.Set(720, 5);
        Check(!yaw.Moving && yaw.Value == 720, "two whole turns: the same picture, nothing to glide");

        // a new target in the middle of a glide: no jump
        var mid = new AngleEase(90, false);
        mid.Set(35, 6);
        for (int i = 0; i < 300; i++) mid.Step(dt, 0.5);
        double at = mid.Value;
        mid.Set(70, 4);
        Check(mid.Value == at, "a new target mid-glide starts from where the angle is");
        mid.Step(dt, 0.5);
        Check(Math.Abs(mid.Value - at) < 0.05, "and its first step is small");
        Glide(mid, dt, 0.5, +1);
        Check(mid.Value == 70, "and it ends exactly on the new target");

        // the marks through a glide of both angles at once: at sampled frames the pixel MapView gives is the draw matrix's
        var y2 = new AngleEase(0, true);
        var p2 = new AngleEase(90, false);
        y2.Set(30, 8);
        p2.Set(55, 6);
        var rng = new Random(23);
        double worst = 0;
        int samples = 0, frame = 0;
        while ((y2.Moving || p2.Moving) && frame < 5000)
        {
            y2.Step(dt, 0.5);
            p2.Step(dt, 0.5);
            if (frame++ % 7 != 0) continue;
            double span = 600 + rng.NextDouble() * 20000;
            var v = View((rng.NextDouble() - 0.5) * 2000, rng.NextDouble() * 200, (rng.NextDouble() - 0.5) * 2000, span, w / h, y2.Value, p2.Value);
            for (int k = 0; k < 6; k++)
            {
                var pt = new Vector3((float)(v.Cx + (rng.NextDouble() - 0.5) * span), (float)(v.Cy + (rng.NextDouble() - 0.3) * 300),
                    (float)(v.Cz + (rng.NextDouble() - 0.5) * span / (w / h)));
                MatrixPixel(v, pt, w, h, out double mx, out double my);
                v.ToPixel(pt.X, pt.Y, pt.Z, w, h, out float px, out float py);
                worst = Math.Max(worst, Math.Max(Math.Abs(px - mx), Math.Abs(py - my)));
                samples++;
            }
        }
        Check(samples > 600 && worst < 0.05, "marks stay on the picture through a glide of yaw and pitch: " + samples + " points, worst " + worst.ToString("0.0000") + " px");
        Check(y2.Value == 30 && p2.Value == 55, "both glides end on their targets");

        // N turns with the yaw: at yaw 30 real north is up and to the left, and it is where the draw matrix puts it
        var n = View(0, 0, 0, 3000, w / h, 30, 90);
        n.DirToPixel(0, -1, w, h, out float nx, out float ny);
        float len = MathF.Sqrt(nx * nx + ny * ny);
        Near(nx / len, -0.5, 1e-5, "N at yaw 30: x");
        Near(ny / len, -Math.Sqrt(3) / 2, 1e-5, "N at yaw 30: y");
        n = View(0, 0, 0, 3000, w / h, 30, 55);
        n.DirToPixel(0, -1, w, h, out nx, out ny);
        MatrixPixel(n, new Vector3(0, 0, 0), w, h, out double cx0, out double cy0);
        MatrixPixel(n, new Vector3(0, 0, -100), w, h, out double cx1, out double cy1);
        double ml = Math.Sqrt((cx1 - cx0) * (cx1 - cx0) + (cy1 - cy0) * (cy1 - cy0));
        len = MathF.Sqrt(nx * nx + ny * ny);
        Near(nx / len, (cx1 - cx0) / ml, 1e-3, "N at yaw 30, pitch 55 points where the picture's north is (x)");
        Near(ny / len, (cy1 - cy0) / ml, 1e-3, "N at yaw 30, pitch 55 points where the picture's north is (y)");
    }

    static bool Hit(float ax, float ay, float aw, float ah, float bx, float by, float bw, float bh) =>
        ax < bx + bw && bx < ax + aw && ay < by + bh && by < ay + ah;

    static void Labels()
    {
        const float W = 712, H = 499;
        var l = new LabelLayout();
        void Set(int i, long key, float x, float y, float w = 60, float h = 22)
        {
            (l.Key[i], l.DotX[i], l.DotY[i], l.DotR[i], l.W[i], l.H[i]) = (key, x, y, 5, w, h);
        }

        // far apart: each name to the right of its own dot
        l.Count = 2;
        Set(0, 1, 100, 100);
        Set(1, 2, 400, 300);
        l.Solve(W, H);
        Check(!l.Stacked[0] && !l.Stacked[1], "far apart: no column");
        Near(l.X[0], 100 + 5 + LabelLayout.Gap, 1e-3, "name right of its dot");
        Near(l.Y[0], 100 - 11, 1e-3, "name level with its dot");

        // at the right edge: the name goes left
        l.Forget();
        l.Count = 1;
        Set(0, 1, 700, 100);
        l.Solve(W, H);
        Check(l.Left[0] && l.X[0] + l.W[0] <= 700 - 5, "right edge: the name stands left of the dot");

        // three men in a knot: one column, roster order, no overlaps, leader lines
        l.Forget();
        l.Count = 3;
        Set(0, 1, 300, 200);
        Set(1, 2, 306, 204);
        Set(2, 3, 296, 208);
        l.Solve(W, H);
        Check(l.Stacked[0] && l.Stacked[1] && l.Stacked[2] && l.Clusters == 1, "a knot: one column");
        Check(l.Y[0] < l.Y[1] && l.Y[1] < l.Y[2], "the column is in roster order");
        Check(l.X[0] == l.X[1] && l.X[1] == l.X[2] && l.X[0] >= 306 + 5 + LabelLayout.ColumnGap - 1e-3, "the column stands right of the knot");
        Check(!Hit(l.X[0], l.Y[0], 60, 22, l.X[1], l.Y[1], 60, 22) && !Hit(l.X[1], l.Y[1], 60, 22, l.X[2], l.Y[2], 60, 22), "the names do not overlap");
        Check(l.LeadX[0] < l.X[0] && Math.Abs(l.LeadY[1] - (l.Y[1] + 11)) < 1e-3, "leader lines end at the names' near edge");

        // the order does not change when the men swap places
        Set(0, 1, 296, 208);
        Set(2, 3, 300, 200);
        l.Solve(W, H);
        Check(l.Y[0] < l.Y[1] && l.Y[1] < l.Y[2], "the column keeps its order when the dots swap");

        // hysteresis: two men walking apart stay in their column a little longer, and two walking together join a little later
        l.Forget();
        l.Count = 2;
        float partedAt = -1, joinedAt = -1;
        for (float gap = 0; gap <= 60; gap += 0.5f)
        {
            Set(0, 1, 300, 200);
            Set(1, 2, 300, 200 + gap);
            l.Solve(W, H);
            if (!l.Stacked[0] && partedAt < 0) partedAt = gap;
        }
        for (float gap = 60; gap >= 0; gap -= 0.5f)
        {
            Set(0, 1, 300, 200);
            Set(1, 2, 300, 200 + gap);
            l.Solve(W, H);
            if (l.Stacked[0] && joinedAt < 0) joinedAt = gap;
        }
        Check(partedAt > 0 && joinedAt > 0 && partedAt - joinedAt >= LabelLayout.Hyst - 0.6f,
            "hysteresis: parts at " + partedAt + " px, joins at " + joinedAt + " px (at least " + LabelLayout.Hyst + " apart)");

        // no flip-flop: jitter around the joining distance changes the layout at most once
        l.Forget();
        var rng = new Random(5);
        int flips = 0;
        bool was = false;
        for (int i = 0; i < 2000; i++)
        {
            Set(0, 1, 300, 200);
            Set(1, 2, 300, 200 + joinedAt + 1.5f + (float)(rng.NextDouble() - 0.5) * 2f);      // +-1 px of jitter just outside the joining distance
            l.Solve(W, H);
            if (i > 0 && l.Stacked[0] != was) flips++;
            was = l.Stacked[0];
        }
        Check(flips <= 1, "jitter of +-1 px near the threshold: " + flips + " layout changes in 2000 frames");

        // random crowds: names stay inside the picture and never cover each other
        int overlaps = 0, outside = 0, trials = 0;
        for (int trial = 0; trial < 400; trial++)
        {
            l.Forget();
            int n = 2 + rng.Next(11);
            l.Count = n;
            float cx = 80 + (float)rng.NextDouble() * 550, cy = 60 + (float)rng.NextDouble() * 380, spread = 10 + (float)rng.NextDouble() * 150;
            for (int i = 0; i < n; i++)
                Set(i, i + 1, Math.Clamp(cx + (float)(rng.NextDouble() - 0.5) * spread, 5, W - 5), Math.Clamp(cy + (float)(rng.NextDouble() - 0.5) * spread, 5, H - 5),
                    40 + rng.Next(70));
            l.Solve(W, H);
            trials++;
            for (int i = 0; i < n; i++)
            {
                if (l.X[i] < 0 || l.Y[i] < 0 || l.X[i] + l.W[i] > W || l.Y[i] + l.H[i] > H) outside++;
                for (int j = i + 1; j < n; j++)
                    if (Hit(l.X[i], l.Y[i], l.W[i], l.H[i], l.X[j], l.Y[j], l.W[j], l.H[j])) overlaps++;
            }
        }
        Check(outside == 0, "random crowds: every name inside the picture (" + outside + " outside in " + trials + " crowds)");
        Console.WriteLine("info  random crowds: " + overlaps + " overlapping name pairs in " + trials + " crowds of 2 to 12 (0 is the aim; a crowd wider than the room beside it can leave some)");
        Check(overlaps <= trials / 20, "random crowds: overlapping names are rare (" + overlaps + ")");
    }
}
