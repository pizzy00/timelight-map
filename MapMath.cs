// TimeLight Map: an in-game 3D picture-in-picture map for MeitouClient.
// Copyright (C) 2026 TimeLight Map contributors
// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.Globalization;

namespace TimeLightMap
{
    // The map's pure arithmetic: no game types in this file, so it can be compiled and tested on its own.
    //   Spring       a critically damped spring (the eased view)
    //   MapView      one view of the map and its world -> picture pixel mapping (any yaw and pitch)
    //   MapMath      scale bar steps, squad centroid, texts
    //   ViewTarget   the view the mod owns while it follows the squad, eased every frame
    //   AngleEase    the yaw and the pitch gliding to a new value
    //   LabelLayout  the squad names next to their dots: columns for crowded dots, with hysteresis
    //   CamPose, ViewCone   the main game camera and its view cone on the ground
    //   Heading      which way each character walks (the arrow on the dot), from where the frames draw them
    //   MapLayers    the marks' layer switches (topcam layer <name> on|off)
    //   RoutePlan    a walking man's path on the map: legs thinned to whole pixels, one line for a group going the same way
    //   MapShape     the round map's circle, and how badly hurt a man is (the hurt rings)
    //   CaptionText  a plain text as a HUD caption
    //   PlaceLayout  where the place labels' texts go so that nothing covers anything else
    //   KnownPlaces  which of the game's towns and places get a label, and under which name

    /// <summary>A critically damped spring, stepped exactly for a target that stands still during the step, so any frame time is
    /// stable. <c>tau</c> is the lag in seconds: a target moving at a constant speed v is followed v x tau behind
    /// (the spring's omega is 2 / tau, the same meaning as a "smooth time").</summary>
    internal struct Spring
    {
        public double Value, Velocity;

        public void Reset(double value)
        {
            Value = value;
            Velocity = 0;
        }

        public void Step(double target, double tau, double dt)
        {
            if (!(dt > 0) || !double.IsFinite(dt) || !double.IsFinite(target)) return;
            if (!(tau > 1e-4))
            {
                Value = target;
                Velocity = 0;
                return;
            }
            double omega = 2.0 / tau, e = Math.Exp(-omega * dt);
            double change = Value - target;
            double temp = (Velocity + omega * change) * dt;
            Velocity = (Velocity - omega * temp) * e;
            Value = target + (change + temp) * e;
        }
    }

    /// <summary>One view of the map: an orthographic camera looking at the centre (Cx, Cy, Cz). On the ground the picture's
    /// right is r = (cos yaw, sin yaw) in world (X, Z) and its up is n = (sin yaw, -cos yaw): at yaw 0 right = +X (east) and
    /// up = -Z (north). Pitch 90 looks straight down; a lower pitch tilts the camera back (it stands on the -n side and looks
    /// towards +n), so the picture's up axis is n sin(pitch) + Y cos(pitch): heights show, and the ground along n is
    /// foreshortened by sin(pitch). Span is the world width across the picture, at every pitch.
    /// These are the axes of Matrix4x4.CreateLookAt(centre - f * d, centre, n) with f = n cos(pitch) - Y sin(pitch), the
    /// matrix the picture is drawn with.
    /// Pixels have their origin at the picture's top left and y grows downward (window pixels inside the rect).</summary>
    internal struct MapView
    {
        public double Cx, Cy, Cz;    // the centre, world units (10 = 1 m); Cy: the height the camera looks at
        public double Span, SpanZ;   // world width and height across the picture
        public double SinYaw, CosYaw, SinPitch, CosPitch;
        public bool FlipX, FlipY;
        public bool Valid;

        public const double StraightDown = 89.95;      // degrees: from here up the view is exactly top-down

        public void Angles(double yawDegrees, double pitchDegrees)
        {
            double yaw = yawDegrees * Math.PI / 180;
            (SinYaw, CosYaw) = (Math.Sin(yaw), Math.Cos(yaw));
            if (pitchDegrees >= StraightDown) (SinPitch, CosPitch) = (1, 0);
            else
            {
                double pitch = Math.Max(pitchDegrees, 5) * Math.PI / 180;
                (SinPitch, CosPitch) = (Math.Sin(pitch), Math.Cos(pitch));
            }
        }

        public void ToPixel(double x, double y, double z, double w, double h, out float px, out float py)
        {
            double dx = x - Cx, dz = z - Cz;
            double right = dx * CosYaw + dz * SinYaw;
            double up = (dx * SinYaw - dz * CosYaw) * SinPitch + (y - Cy) * CosPitch;
            double u = 0.5 + right / Span, v = 0.5 - up / SpanZ;
            if (FlipX) u = 1 - u;
            if (FlipY) v = 1 - v;
            px = (float)(u * w);
            py = (float)(v * h);
        }

        /// <summary>The world point at height <paramref name="y"/> that a pixel shows.</summary>
        public void ToWorld(double px, double py, double y, double w, double h, out double x, out double z)
        {
            double u = px / w, v = py / h;
            if (FlipX) u = 1 - u;
            if (FlipY) v = 1 - v;
            double right = (u - 0.5) * Span, up = (0.5 - v) * SpanZ;
            double along = (up - (y - Cy) * CosPitch) / SinPitch;      // along n on the ground
            x = Cx + right * CosYaw + along * SinYaw;
            z = Cz + right * SinYaw - along * CosYaw;
        }

        /// <summary>The picture direction (pixels, y down, not normalised) of a level world direction.</summary>
        public void DirToPixel(double dx, double dz, double w, double h, out float px, out float py)
        {
            double right = dx * CosYaw + dz * SinYaw, up = (dx * SinYaw - dz * CosYaw) * SinPitch;
            double u = right / Span, v = -up / SpanZ;
            if (FlipX) u = -u;
            if (FlipY) v = -v;
            px = (float)(u * w);
            py = (float)(v * h);
        }

        /// <summary>Whether <paramref name="o"/> shows the same picture to within <paramref name="pixels"/> of a picture
        /// <paramref name="w"/> pixels wide.</summary>
        public bool Near(in MapView o, double pixels, double w)
        {
            if (!Valid || !o.Valid) return false;
            double tol = Span / Math.Max(w, 1) * pixels;
            return Math.Abs(Cx - o.Cx) <= tol && Math.Abs(Cz - o.Cz) <= tol && Math.Abs(Span - o.Span) <= tol
                   && Math.Abs(SpanZ - o.SpanZ) <= tol && Math.Abs(Cy - o.Cy) * Math.Abs(CosPitch) <= tol
                   && SinYaw == o.SinYaw && CosYaw == o.CosYaw && SinPitch == o.SinPitch && FlipX == o.FlipX && FlipY == o.FlipY;
        }
    }

    internal static class MapMath
    {
        public const double UnitsPerMetre = 10.0;

        /// <summary>The scale bar: the largest of 1, 2, 5 x 10^n metres that is at most <paramref name="maxPixels"/> long.
        /// 0 when there is none.</summary>
        public static double ScaleMetres(double unitsPerPixel, double maxPixels)
        {
            double most = unitsPerPixel * maxPixels / UnitsPerMetre;
            if (!(most > 0) || double.IsInfinity(most)) return 0;
            double p = Math.Pow(10, Math.Floor(Math.Log10(most)));
            while (p * 10 <= most) p *= 10;     // a log that came out a hair low
            while (p > most) p /= 10;           // or high
            double m = most / p;
            return (m >= 5 ? 5 : m >= 2 ? 2 : 1) * p;
        }

        public static string ScaleText(double metres) =>
            metres >= 1000 ? (metres / 1000).ToString("0.#", CultureInfo.InvariantCulture) + " km"
                           : metres.ToString("0.##", CultureInfo.InvariantCulture) + " m";

        public static double SmoothStep(double a, double b, double x)
        {
            double t = Math.Clamp((x - a) / (b - a), 0, 1);
            return t * t * (3 - 2 * t);
        }

        /// <summary>The pack's middle: the members weighted 1 within <paramref name="near"/> of the anchor (the first living
        /// member), fading to 0 at <paramref name="far"/>, so a man who wanders off lets go of the map gradually instead of
        /// jerking it. False when there is nobody.</summary>
        public static bool Centroid(double[] x, double[] y, double[] z, int count, int anchor, double near, double far,
            out double cx, out double cy, out double cz)
        {
            cx = cy = cz = 0;
            if (count <= 0 || anchor < 0 || anchor >= count) return false;
            double ax = x[anchor], az = z[anchor], sw = 0;
            for (int i = 0; i < count; i++)
            {
                double dx = x[i] - ax, dz = z[i] - az;
                double w = i == anchor ? 1 : 1 - SmoothStep(near, far, Math.Sqrt(dx * dx + dz * dz));
                if (w <= 0) continue;
                cx += x[i] * w;
                cy += y[i] * w;
                cz += z[i] * w;
                sw += w;
            }
            if (!(sw > 0)) return false;
            cx /= sw;
            cy /= sw;
            cz /= sw;
            return true;
        }

        /// <summary>The pack's weighted radius around (<paramref name="cx"/>, <paramref name="cz"/>): the largest distance of
        /// any member weighted the same way as <see cref="Centroid"/> (1 within <paramref name="near"/> of the anchor, fading
        /// smoothly to 0 at <paramref name="far"/>), so a lone wanderer smoothly lets go of auto-zoom without jerking it.</summary>
        public static double Spread(double[] x, double[] z, int count, int anchor, double near, double far, double cx, double cz)
        {
            if (count <= 1 || anchor < 0 || anchor >= count) return 0;
            double ax = x[anchor], az = z[anchor], maxR = 0;
            for (int i = 0; i < count; i++)
            {
                double adx = x[i] - ax, adz = z[i] - az;
                double w = i == anchor ? 1 : 1 - SmoothStep(near, far, Math.Sqrt(adx * adx + adz * adz));
                if (w <= 0.05) continue;
                double dx = x[i] - cx, dz = z[i] - cz;
                double r = Math.Sqrt(dx * dx + dz * dz) * Math.Min(1.0, w * 2.0);
                if (r > maxR) maxR = r;
            }
            return maxR;
        }

        public static string Num(double v, string format = "0.#") =>
            double.IsFinite(v) ? v.ToString(format, CultureInfo.InvariantCulture) : "0";

        public static string JsonText(string s)
        {
            if (s == null) return "null";
            var sb = new System.Text.StringBuilder(s.Length + 2);
            sb.Append('"');
            foreach (char ch in s)
            {
                if (ch == '"') sb.Append("\\\"");
                else if (ch == '\\') sb.Append("\\\\");
                else if (ch < ' ') sb.Append(' ');
                else sb.Append(ch);
            }
            return sb.Append('"').ToString();
        }
    }

    /// <summary>
    /// One of the view's angles (degrees) on its way to a new value: over a given time (smoothstep: it starts and ends
    /// gently), or by the view's own spring when no time is given, or at once. A yaw goes the short way round (never more
    /// than half a turn). At the end the value is exactly the target as it was asked. Stepped once a frame on the render
    /// hook; set by commands on the same thread.
    /// </summary>
    internal sealed class AngleEase
    {
        public double Value, Target;
        public bool Moving;
        readonly bool _wraps;
        double _goal, _from, _time, _over;      // _goal: the target on the line the value travels (a yaw's may lie beyond 0 or 360)
        bool _timed;
        Spring _spring;

        public AngleEase(double value, bool wraps)
        {
            Value = Target = _goal = value;
            _wraps = wraps;
            _spring.Reset(value);
        }

        /// <summary><paramref name="over"/>: seconds of a timed glide; 0: at once; below 0: by the spring (the tau given to
        /// Step).</summary>
        public void Set(double target, double over)
        {
            Target = target;
            double delta = target - Value;
            if (_wraps) delta = Math.IEEERemainder(delta, 360);      // -180..180: the short way round
            if (over == 0 || delta == 0)
            {
                Finish();
                return;
            }
            _goal = Value + delta;
            if (over > 0) (_timed, _from, _time, _over) = (true, Value, 0, over);
            else
            {
                if (!Moving || _timed) _spring.Reset(Value);      // from rest; a spring already under way keeps its speed
                _timed = false;
            }
            Moving = true;
        }

        public void Step(double dt, double tau)
        {
            if (!Moving || !(dt > 0)) return;
            if (_timed)
            {
                _time += dt;
                double t = _time / _over;
                if (t >= 1) Finish();
                else Value = _from + (_goal - _from) * (t * t * (3 - 2 * t));
            }
            else
            {
                _spring.Step(_goal, tau, dt);
                Value = _spring.Value;
                if (!(tau > 1e-4) || (Math.Abs(Value - _goal) < 0.002 && Math.Abs(_spring.Velocity) < 0.01)) Finish();
            }
        }

        void Finish()
        {
            Value = _goal = Target;
            _spring.Reset(Target);
            Moving = _timed = false;
        }
    }

    internal enum TargetMode { None, Follow }

    /// <summary>
    /// The view the mod owns while it follows the squad: where it should be (on the squad's middle, at the asked span) and
    /// the eased view of this frame. The centre and the height it looks at ease in world units, the span in log space (a
    /// zoom looks even). A new span keeps the eased state, so the view glides from wherever it is; only a jump of the
    /// followed point itself (a load, a teleport) is a cut. Stepped once a frame on the render hook; set by commands on the
    /// same thread.
    /// </summary>
    internal sealed class ViewTarget
    {
        public TargetMode Mode;
        public double FollowSpan = 3000;                        // the world width across the picture while following (max when AutoZoom is on)
        public double SpanFactor = 1;                           // the big map shows this many times FollowSpan (it glides there)
        public double Ease = 0.5;                               // seconds (see Spring)
        public bool AutoZoom;                                   // smoothly zoom in closer when the followed group is huddled together
        public double AutoMinSpan = 500;                        // world units across when tightly grouped (50 m)

        public const double MinSpan = 20, MaxSpan = 60000;
        public const double JumpUnits = 3000;                   // the followed point moved this far in one frame: a cut
        public const double AutoSpreadScale = 3.2, AutoPad = 250;   // framing: 3.2x weighted radius + 25 m breathing room

        Spring _cx, _cz, _logSpan, _height;
        bool _started, _heightStarted;
        bool _havePoint;
        double _pointX, _pointZ;
        double _autoSpan;

        public double Cx => _cx.Value;
        public double Cz => _cz.Value;
        public double Span => Math.Exp(_logSpan.Value);
        public double Height => _height.Value;
        public double AimCx, AimCz, AimSpan;      // where it is heading
        public bool AtRest = true;
        public bool Started => _started;
        /// <summary>Begun cold (no Start) with nobody to follow yet: the first point that comes is a cut, not a sweep from
        /// the origin.</summary>
        public bool WaitingForPoint { get; private set; }

        /// <summary>The eased view starts from here (the view on screen when the following began).</summary>
        public void Start(double cx, double cz, double span, double height)
        {
            _cx.Reset(cx);
            _cz.Reset(cz);
            _logSpan.Reset(Math.Log(Math.Clamp(span, MinSpan, MaxSpan)));
            _height.Reset(height);
            _started = _heightStarted = true;
            AtRest = false;
        }

        public void Stop()
        {
            Mode = TargetMode.None;
            _started = _heightStarted = _havePoint = false;
            _autoSpan = 0;
            AtRest = true;
            WaitingForPoint = false;
        }

        /// <summary>One frame. <paramref name="dt"/>: real seconds since the last step. (<paramref name="pointX"/>,
        /// <paramref name="pointZ"/>): the followed point when there is one (the squad's middle). <paramref name="spread"/>:
        /// the group's weighted radius in world units (negative when unknown).</summary>
        public void Step(double dt, bool havePoint, double pointX, double pointZ, double spread = -1)
        {
            if (Mode == TargetMode.None) return;
            bool cold = !_started;      // nobody gave a start: begin at the aim itself (a cut, never a sweep from nowhere)
            if (cold) Start(0, 0, FollowSpan, 0);
            bool cut = false;
            if (havePoint)
            {
                cut = Moved(pointX, pointZ) || WaitingForPoint;
                WaitingForPoint = false;
            }
            else if (cold) WaitingForPoint = true;
            // with nobody to follow the view holds where it is
            double aimX = _havePoint ? _pointX : _cx.Value, aimZ = _havePoint ? _pointZ : _cz.Value;
            double baseSpan = FollowSpan;
            bool autoActive = AutoZoom && havePoint && spread >= 0 && double.IsFinite(spread) && !(SpanFactor > 1.01);
            if (autoActive)
            {
                double hi = Math.Clamp(FollowSpan, MinSpan, MaxSpan);
                double lo = Math.Clamp(Math.Min(AutoMinSpan, hi), MinSpan, hi);
                double raw = Math.Clamp(spread * AutoSpreadScale + AutoPad, lo, hi);
                if (cold || cut || !(_autoSpan > 0)) _autoSpan = raw;
                else if (raw > _autoSpan * 1.15 || raw < _autoSpan / 1.18
                         || (raw <= lo + 1e-6 && _autoSpan > lo * 1.05) || (raw >= hi - 1e-6 && _autoSpan < hi / 1.05))
                    _autoSpan = raw;
                else _autoSpan = Math.Clamp(_autoSpan, lo, hi);
                baseSpan = _autoSpan;
            }
            else _autoSpan = 0;
            double aimSpan = Math.Clamp(baseSpan * (SpanFactor > 0 ? SpanFactor : 1), MinSpan, MaxSpan);
            (AimCx, AimCz, AimSpan) = (aimX, aimZ, aimSpan);
            double logAim = Math.Log(aimSpan);
            if (cold || cut)
            {
                _cx.Reset(aimX);
                _cz.Reset(aimZ);
                if (cold) _logSpan.Reset(logAim);
                _heightStarted = false;
            }
            dt = Math.Clamp(dt, 0, 0.25);
            _cx.Step(aimX, Ease, dt);
            _cz.Step(aimZ, Ease, dt);
            _logSpan.Step(logAim, autoActive ? Math.Max(Ease, 0.9) : Ease, dt);

            // at rest: within a fiftieth of a pixel (of a 1000 pixel picture) and all but still
            double eps = Span * 2e-5;
            AtRest = Math.Abs(_cx.Value - aimX) < eps && Math.Abs(_cz.Value - aimZ) < eps && Math.Abs(_logSpan.Value - logAim) < 2e-5
                     && Math.Abs(_cx.Velocity) < eps * 4 && Math.Abs(_cz.Velocity) < eps * 4 && Math.Abs(_logSpan.Velocity) < 8e-5;
            if (AtRest)
            {
                _cx.Reset(aimX);
                _cz.Reset(aimZ);
                _logSpan.Reset(logAim);
            }
        }

        /// <summary>The height the camera looks at eases to the ground under the eased centre (a tilted view would otherwise
        /// bob with every bump the centre crosses). Call after Step, with the ground at (Cx, Cz).</summary>
        public double StepHeight(double ground, double dt)
        {
            if (!double.IsFinite(ground)) return _height.Value;
            if (!_heightStarted)
            {
                _height.Reset(ground);
                _heightStarted = true;
            }
            else _height.Step(ground, Math.Max(Ease, 0.3), Math.Clamp(dt, 0, 0.25));
            return _height.Value;
        }

        /// <summary>Notes where the followed point is now. True: it jumped since the last frame (a load, a teleport), and
        /// the view cuts to it.</summary>
        bool Moved(double x, double z)
        {
            double dx = x - _pointX, dz = z - _pointZ;
            bool jumped = _havePoint && dx * dx + dz * dz > JumpUnits * JumpUnits;
            (_pointX, _pointZ, _havePoint) = (x, z, true);
            return jumped;
        }
    }

    /// <summary>
    /// The squad's names beside their dots. A name stands to the right of its dot (to the left at the picture's right edge).
    /// Dots too close for that (a name would cover another name or another dot) form a cluster: its names are stacked in
    /// one column beside the cluster, in the order the items were given (the roster's order, so the column never reshuffles
    /// as the men move), each joined to its dot by a thin leader line. Hysteresis: two items that shared a column last frame
    /// must come <see cref="Hyst"/> pixels further apart than the plain test asks before they part again, and a side (left
    /// or right) is kept until it no longer fits or the other has room to spare.
    /// The caller fills Count and the in arrays, calls Solve, reads the out arrays. No allocation after the first frames.
    /// </summary>
    internal sealed class LabelLayout
    {
        public const int Max = 40, MaxObstacles = 8, SplitAt = 6;
        public const float Gap = 4f;          // dot edge to its name
        public const float Pad = 2f;          // least clear space between two names
        public const float Hyst = 6f;
        public const float ColumnGap = 14f;   // a cluster's dots to its column
        public const float RowGap = 4f;       // between two rows of a column
        public const float Edge = 2f;         // names stay this far inside the picture
        public const float LeadGap = 5f;      // a leader line stops this far from its name (outside the dark pill)
        public const float Spare = 40f;       // room the right side must have before a left side returns to it

        /// <summary>A round map: above 0, names prefer the side that fits inside this circle round the picture's middle (pixels).</summary>
        public float CircleR;

        // chrome obstacles (scale bar, north arrow) that squad names stay clear of
        public int Obstacles;
        public readonly float[] OX0 = new float[MaxObstacles], OY0 = new float[MaxObstacles], OX1 = new float[MaxObstacles], OY1 = new float[MaxObstacles];

        // in
        public int Count;
        public readonly long[] Key = new long[Max];
        public readonly float[] DotX = new float[Max], DotY = new float[Max], DotR = new float[Max];
        public readonly float[] W = new float[Max], H = new float[Max];

        // out
        public readonly float[] X = new float[Max], Y = new float[Max];
        public readonly float[] LeadX = new float[Max], LeadY = new float[Max];
        public readonly bool[] Stacked = new bool[Max], Left = new bool[Max];
        public int Clusters;

        Dictionary<long, long> _prevRoot = new Dictionary<long, long>(), _nextRoot = new Dictionary<long, long>();
        Dictionary<long, bool> _prevInlineLeft = new Dictionary<long, bool>(), _nextInlineLeft = new Dictionary<long, bool>();
        Dictionary<long, bool> _prevColumnLeft = new Dictionary<long, bool>(), _nextColumnLeft = new Dictionary<long, bool>();
        readonly int[] _parent = new int[Max], _size = new int[Max], _members = new int[Max];
        readonly float[] _inX = new float[Max], _inY = new float[Max];
        readonly bool[] _inLeft = new bool[Max];
        readonly float[] _colX0 = new float[Max], _colY0 = new float[Max], _colX1 = new float[Max], _colY1 = new float[Max];
        readonly float[] _col2X0 = new float[Max], _col2Y0 = new float[Max], _col2X1 = new float[Max], _col2Y1 = new float[Max];
        readonly bool[] _colLeft = new bool[Max], _hasCol2 = new bool[Max];

        public void ClearObstacles() => Obstacles = 0;

        public void AddObstacle(float x0, float y0, float x1, float y1)
        {
            if (Obstacles >= MaxObstacles || !(x1 > x0) || !(y1 > y0)) return;
            (OX0[Obstacles], OY0[Obstacles], OX1[Obstacles], OY1[Obstacles]) = (x0, y0, x1, y1);
            Obstacles++;
        }

        public void Forget()
        {
            _prevRoot.Clear();
            _prevInlineLeft.Clear();
            _prevColumnLeft.Clear();
        }

        int Find(int i)
        {
            while (_parent[i] != i) i = _parent[i] = _parent[_parent[i]];
            return i;
        }

        // the smaller index is always the root: a cluster's root is its first member in the given order
        bool Union(int a, int b)
        {
            a = Find(a);
            b = Find(b);
            if (a == b) return false;
            if (b < a) (a, b) = (b, a);
            _parent[b] = a;
            return true;
        }

        bool Together(int i, int j) =>
            _prevRoot.TryGetValue(Key[i], out long a) && _prevRoot.TryGetValue(Key[j], out long b) && a == b;

        static bool Overlap(float ax0, float ay0, float ax1, float ay1, float bx0, float by0, float bx1, float by1) =>
            ax0 < bx1 && bx0 < ax1 && ay0 < by1 && by0 < ay1;

        static bool BoxDot(float x0, float y0, float x1, float y1, float cx, float cy, float r)
        {
            float nx = Math.Clamp(cx, x0, x1) - cx, ny = Math.Clamp(cy, y0, y1) - cy;
            return nx * nx + ny * ny < r * r;
        }

        bool HitsObstacle(float x0, float y0, float x1, float y1)
        {
            float a0 = x0 - Pad, b0 = y0 - Pad, a1 = x1 + Pad, b1 = y1 + Pad;
            for (int o = 0; o < Obstacles; o++)
                if (Overlap(a0, b0, a1, b1, OX0[o], OY0[o], OX1[o], OY1[o])) return true;
            return false;
        }

        bool InBounds(float x0, float y0, float x1, float y1, float rectW, float rectH) =>
            x0 >= Edge && y0 >= Edge && x1 <= rectW - Edge && y1 <= rectH - Edge
            && (CircleR <= 0f || MapShape.BoxInCircle(x0, y0, x1, y1, rectW * 0.5f, rectH * 0.5f, CircleR - Edge))
            && !HitsObstacle(x0, y0, x1, y1);

        float NudgeY(float x, float y, float w, float h, float rectH)
        {
            float lo = Edge, hi = MathF.Max(Edge, rectH - Edge - h);
            y = Math.Clamp(y, lo, hi);
            for (int o = 0; o < Obstacles; o++)
            {
                if (x + w + Pad <= OX0[o] || x - Pad >= OX1[o]) continue;
                if (y + h + Pad <= OY0[o] || y - Pad >= OY1[o]) continue;
                float up = OY0[o] - Pad - h, down = OY1[o] + Pad;
                bool upOk = up >= lo, downOk = down <= hi;
                if (upOk && (!downOk || MathF.Abs(up - y) <= MathF.Abs(down - y))) y = up;
                else if (downOk) y = down;
            }
            return Math.Clamp(y, lo, hi);
        }

        bool ColumnHits(int r, int j, float pad)
        {
            if (HitBox(_colX0[r] - pad, _colY0[r] - pad, _colX1[r] + pad, _colY1[r] + pad, j)) return true;
            return _hasCol2[r] && HitBox(_col2X0[r] - pad, _col2Y0[r] - pad, _col2X1[r] + pad, _col2Y1[r] + pad, j);
        }

        bool HitBox(float x0, float y0, float x1, float y1, int j) =>
            Overlap(x0, y0, x1, y1, X[j], Y[j], X[j] + W[j], Y[j] + H[j]) || BoxDot(x0, y0, x1, y1, DotX[j], DotY[j], DotR[j] + 1f);

        public void Solve(float rectW, float rectH)
        {
            int n = Count = Math.Clamp(Count, 0, Max);

            // A. every name beside its own dot
            for (int i = 0; i < n; i++)
            {
                float inY = Math.Clamp(DotY[i] - H[i] * 0.5f, Edge, Math.Max(Edge, rectH - Edge - H[i]));
                float right = DotX[i] + DotR[i] + Gap, left = DotX[i] - DotR[i] - Gap - W[i];
                bool fitsRight = InBounds(right, inY, right + W[i], inY + H[i], rectW, rectH);
                bool fitsLeft = InBounds(left, inY, left + W[i], inY + H[i], rectW, rectH);
                bool rightSpare = fitsRight && right + W[i] <= rectW - Edge - Spare
                    && (CircleR <= 0f || MapShape.BoxInCircle(right + Spare, inY, right + W[i] + Spare, inY + H[i], rectW * 0.5f, rectH * 0.5f, CircleR - Edge));
                bool wasLeft = _prevInlineLeft.TryGetValue(Key[i], out bool l) && l;
                bool useLeft;
                if (!fitsRight && !fitsLeft && CircleR > 0f) useLeft = DotX[i] > rectW * 0.5f;
                else useLeft = wasLeft ? fitsLeft && !rightSpare : !fitsRight && fitsLeft;
                _inLeft[i] = useLeft;
                _inX[i] = Math.Clamp(useLeft ? left : right, Edge, Math.Max(Edge, rectW - Edge - W[i]));
                _inY[i] = inY;
                _parent[i] = i;
            }

            // B. names that would cover another name or another dot share a cluster
            for (int i = 0; i < n; i++)
                for (int j = i + 1; j < n; j++)
                {
                    float pad = Pad + (Together(i, j) ? Hyst : 0f);
                    bool hit = Overlap(_inX[i] - pad, _inY[i] - pad, _inX[i] + W[i] + pad, _inY[i] + H[i] + pad,
                                       _inX[j], _inY[j], _inX[j] + W[j], _inY[j] + H[j])
                               || BoxDot(_inX[i] - pad, _inY[i] - pad, _inX[i] + W[i] + pad, _inY[i] + H[i] + pad, DotX[j], DotY[j], DotR[j] + 1f)
                               || BoxDot(_inX[j] - pad, _inY[j] - pad, _inX[j] + W[j] + pad, _inY[j] + H[j] + pad, DotX[i], DotY[i], DotR[i] + 1f);
                    if (hit) Union(i, j);
                }

            // C. place, then D. a column that covers somebody else's name or dot takes him in; a few rounds settle it
            for (int round = 0; round < 5; round++)
            {
                Place(n, rectW, rectH);
                bool merged = false;
                for (int r = 0; r < n; r++)
                {
                    if (Find(r) != r || _size[r] < 2) continue;
                    for (int j = 0; j < n; j++)
                    {
                        if (Find(j) == r) continue;
                        float pad = Pad + (Together(r, j) ? Hyst : 0f);
                        if (ColumnHits(r, j, pad))
                        {
                            Union(r, j);
                            merged = true;
                            break;      // this column changes: place again
                        }
                    }
                }
                if (!merged) break;
                if (round == 4) Place(n, rectW, rectH);
            }

            // what the next frame remembers
            _nextRoot.Clear();
            _nextInlineLeft.Clear();
            _nextColumnLeft.Clear();
            Clusters = 0;
            for (int i = 0; i < n; i++)
            {
                int r = Find(i);
                if (_size[r] >= 2)
                {
                    _nextRoot[Key[i]] = Key[r];
                    if (i == r)
                    {
                        _nextColumnLeft[Key[r]] = _colLeft[r];
                        Clusters++;
                    }
                }
                else _nextInlineLeft[Key[i]] = _inLeft[i];
            }
            (_prevRoot, _nextRoot) = (_nextRoot, _prevRoot);
            (_prevInlineLeft, _nextInlineLeft) = (_nextInlineLeft, _prevInlineLeft);
            (_prevColumnLeft, _nextColumnLeft) = (_nextColumnLeft, _prevColumnLeft);
        }

        void Place(int n, float rectW, float rectH)
        {
            for (int i = 0; i < n; i++) (_size[i], _hasCol2[i]) = (0, false);
            for (int i = 0; i < n; i++) _size[Find(i)]++;
            for (int r = 0; r < n; r++)
            {
                if (Find(r) != r) continue;
                if (_size[r] < 2)
                {
                    (X[r], Y[r], Stacked[r], Left[r]) = (_inX[r], _inY[r], false, _inLeft[r]);
                    (LeadX[r], LeadY[r]) = (DotX[r], DotY[r]);
                    continue;
                }
                float minX = float.MaxValue, maxX = float.MinValue, sumY = 0, cw = 0, ch = -RowGap;
                int m = 0;
                for (int i = r; i < n; i++)
                {
                    if (Find(i) != r) continue;
                    _members[m++] = i;
                    minX = MathF.Min(minX, DotX[i] - DotR[i]);
                    maxX = MathF.Max(maxX, DotX[i] + DotR[i]);
                    sumY += DotY[i];
                    cw = MathF.Max(cw, W[i]);
                    ch += H[i] + RowGap;
                }
                float avgY = sumY / m;
                float rx = maxX + ColumnGap, lx = minX - ColumnGap - cw;
                float ry = NudgeY(rx, avgY - ch * 0.5f, cw, ch, rectH);
                float ly = NudgeY(lx, avgY - ch * 0.5f, cw, ch, rectH);
                bool fitsRight = InBounds(rx, ry, rx + cw, ry + ch, rectW, rectH);
                bool fitsLeft = InBounds(lx, ly, lx + cw, ly + ch, rectW, rectH);

                // large clusters (6+, or 4+ when one tall column won't fit): split into two balanced wings (right + left)
                // in strict roster order so the stack stays compact in the middle of the map and clear of chrome / rims
                if (m >= SplitAt || (m >= 4 && !fitsRight && !fitsLeft))
                {
                    int rightN = (m + 1) / 2;
                    float rw1 = 0, rh1 = -RowGap, lw2 = 0, lh2 = -RowGap;
                    for (int k = 0; k < rightN; k++)
                    {
                        int idx = _members[k];
                        rw1 = MathF.Max(rw1, W[idx]);
                        rh1 += H[idx] + RowGap;
                    }
                    for (int k = rightN; k < m; k++)
                    {
                        int idx = _members[k];
                        lw2 = MathF.Max(lw2, W[idx]);
                        lh2 += H[idx] + RowGap;
                    }
                    float rwx = maxX + ColumnGap, lwx = minX - ColumnGap - lw2;
                    float rwy = NudgeY(rwx, avgY - rh1 * 0.5f, rw1, rh1, rectH);
                    float lwy = NudgeY(lwx, avgY - lh2 * 0.5f, lw2, lh2, rectH);
                    bool rightOk = InBounds(rwx, rwy, rwx + rw1, rwy + rh1, rectW, rectH);
                    bool leftOk = InBounds(lwx, lwy, lwx + lw2, lwy + lh2, rectW, rectH);
                    bool preferSplit = rightOk && leftOk;
                    if (!preferSplit && !fitsRight && !fitsLeft)
                    {
                        // neither single column nor both wings fit wholly: pick whichever keeps more names inside bounds
                        int splitIn = CountIn(0, rightN, rwx, rwy, rw1, false, rectW, rectH)
                                    + CountIn(rightN, m, lwx, lwy, lw2, true, rectW, rectH);
                        bool singleLeft = minX - Edge > rectW - Edge - maxX;
                        float sx = Math.Clamp(singleLeft ? lx : rx, Edge, Math.Max(Edge, rectW - Edge - cw));
                        float sy = singleLeft ? ly : ry;
                        int singleIn = CountIn(0, m, sx, sy, cw, singleLeft, rectW, rectH);
                        preferSplit = splitIn > singleIn;
                    }
                    if (preferSplit)
                    {
                        float x1 = Math.Clamp(rwx, Edge, Math.Max(Edge, rectW - Edge - rw1));
                        float x2 = Math.Clamp(lwx, Edge, Math.Max(Edge, rectW - Edge - lw2));
                        (_colX0[r], _colY0[r], _colX1[r], _colY1[r], _colLeft[r]) = (x1, rwy, x1 + rw1, rwy + rh1, false);
                        (_col2X0[r], _col2Y0[r], _col2X1[r], _col2Y1[r], _hasCol2[r]) = (x2, lwy, x2 + lw2, lwy + lh2, true);
                        PlaceColumn(0, rightN, x1, rwy, rw1, false);
                        PlaceColumn(rightN, m, x2, lwy, lw2, true);
                        continue;
                    }
                }

                bool rightSpare = fitsRight && rx + cw <= rectW - Edge - Spare
                    && (CircleR <= 0f || MapShape.BoxInCircle(rx + Spare, ry, rx + cw + Spare, ry + ch, rectW * 0.5f, rectH * 0.5f, CircleR - Edge));
                bool wasLeft = _prevColumnLeft.TryGetValue(Key[r], out bool l) && l;
                bool left;
                if (!fitsRight && !fitsLeft) left = minX - Edge > rectW - Edge - maxX;      // neither: the roomier side
                else if (wasLeft) left = fitsLeft && !rightSpare;
                else left = !fitsRight;
                float x = Math.Clamp(left ? lx : rx, Edge, Math.Max(Edge, rectW - Edge - cw));
                float y = left ? ly : ry;
                (_colX0[r], _colY0[r], _colX1[r], _colY1[r], _colLeft[r]) = (x, y, x + cw, y + ch, left);
                PlaceColumn(0, m, x, y, cw, left);
            }
        }

        int CountIn(int from, int to, float colX, float y, float cw, bool left, float rectW, float rectH)
        {
            int ok = 0;
            for (int k = from; k < to; k++)
            {
                int i = _members[k];
                float xi = left ? colX + cw - W[i] : colX;
                if (InBounds(xi, y, xi + W[i], y + H[i], rectW, rectH)) ok++;
                y += H[i] + RowGap;
            }
            return ok;
        }

        void PlaceColumn(int from, int to, float colX, float y, float cw, bool left)
        {
            for (int k = from; k < to; k++)
            {
                int i = _members[k];
                X[i] = left ? colX + cw - W[i] : colX;
                Y[i] = y;
                LeadX[i] = left ? X[i] + W[i] + LeadGap : X[i] - LeadGap;
                LeadY[i] = y + H[i] * 0.5f;
                (Stacked[i], Left[i]) = (true, left);
                y += H[i] + RowGap;
            }
        }
    }

    // =========================================================================================================================
    //  the camera's view cone, the heading arrows, the layer switches, captions
    // =========================================================================================================================

    /// <summary>The main game camera as the frame drew it (Meitou's WorldCamera: Rendering/Frame/WorldCamera.cs), read once a
    /// frame on the render hook. World units (10 = 1 m), radians.</summary>
    internal struct CamPose
    {
        public double EyeX, EyeY, EyeZ;               // WorldCamera.Eye = Target + Back * Distance
        public double TargetX, TargetY, TargetZ;      // WorldCamera.Target: the point it orbits and looks at (the main picture's middle)
        public double Yaw;                            // WorldCamera.Yaw: 0 looks along -Z (north), the eye on the +Z side of the target
        public double Pitch;                          // WorldCamera.Pitch: the eye's height angle above the horizon, seen from the target
        public double Fov;                            // WorldCamera.FieldOfView: the vertical field of view
        public double Aspect;                         // the main view's width / height (WorldFrame: f.Width / f.Height)
        public bool Valid;
    }

    /// <summary>
    /// The main camera's view cone on the ground: its apex is the eye's ground point, its axis the camera's heading (yaw), its opening the
    /// camera's horizontal field of view (atan(tan(fov / 2) x aspect) a side), its length how far the camera sees the ground ahead:
    /// where the top edge of the picture meets the ground (level with the point it looks at), and never more than
    /// <c>reach</c> (<see cref="DefaultReach"/>; 'topcam cone reach') times the eye's ground distance to that point (so a camera
    /// that sees to the horizon still gets a cone that ends past the middle of its picture); then held between the lengths the
    /// map can show.
    /// </summary>
    internal static class ViewCone
    {
        public const double DefaultReach = 1.5, MinReach = 0.5, MaxReach = 8.0;      // 1.5: long enough to read, never floods the map
        public const double MinHalf = 5 * Math.PI / 180, MaxHalf = 80 * Math.PI / 180;
        public const double SegmentAngle = 8 * Math.PI / 180;      // the far edge is drawn as straight pieces this wide

        /// <summary>False when the camera gives nothing usable. (ax, az): the apex; (dirX, dirZ): the unit axis; half: half the
        /// opening (radians); len: the length (world units), within [minLen, maxLen].</summary>
        public static bool Make(in CamPose c, double reach, double minLen, double maxLen, out double ax, out double az, out double dirX, out double dirZ,
            out double half, out double len)
        {
            (ax, az, dirX, dirZ, half, len) = (c.EyeX, c.EyeZ, 0, -1, 0, 0);
            if (!c.Valid || !(c.Fov > 0) || !(c.Aspect > 0) || !double.IsFinite(c.Fov) || !double.IsFinite(c.Aspect)
                || !double.IsFinite(c.Yaw) || !double.IsFinite(c.Pitch) || !double.IsFinite(ax) || !double.IsFinite(az)
                || !double.IsFinite(c.EyeY) || !double.IsFinite(c.TargetX) || !double.IsFinite(c.TargetY) || !double.IsFinite(c.TargetZ)
                || !(maxLen > 0) || !double.IsFinite(maxLen))
                return false;
            (dirX, dirZ) = (-Math.Sin(c.Yaw), -Math.Cos(c.Yaw));
            half = Math.Clamp(Math.Atan(Math.Tan(Math.Min(c.Fov, 3.0) * 0.5) * c.Aspect), MinHalf, MaxHalf);
            double fx = c.TargetX - c.EyeX, fz = c.TargetZ - c.EyeZ;
            double flat = Math.Sqrt(fx * fx + fz * fz), height = c.EyeY - c.TargetY;
            double far = double.PositiveInfinity;
            double topDown = c.Pitch - Math.Min(c.Fov, 3.0) * 0.5;      // how far below the horizon the picture's top edge looks
            if (height > 0 && topDown > 1e-3) far = height / Math.Tan(Math.Min(topDown, 1.55));
            len = Math.Min(far, Math.Clamp(double.IsFinite(reach) ? reach : DefaultReach, MinReach, MaxReach) * flat);
            if (!double.IsFinite(len)) len = maxLen;
            len = Math.Clamp(len, Math.Min(minLen, maxLen), maxLen);
            return true;
        }

        /// <summary>How many straight pieces draw the far edge of a cone this wide (3..16).</summary>
        public static int Segments(double half) => Math.Clamp((int)Math.Ceiling(2 * half / SegmentAngle), 3, 16);

        /// <summary>The outline on the ground: [0] the apex, then [1..n+1] the far edge from one side to the other.</summary>
        public static void Points(double ax, double az, double dirX, double dirZ, double half, double len, int n, double[] xs, double[] zs)
        {
            xs[0] = ax;
            zs[0] = az;
            for (int i = 0; i <= n; i++)
            {
                double a = -half + 2 * half * i / n, c = Math.Cos(a), s = Math.Sin(a);
                xs[i + 1] = ax + (dirX * c - dirZ * s) * len;
                zs[i + 1] = az + (dirX * s + dirZ * c) * len;
            }
        }
    }

    /// <summary>
    /// Which way each man walks: the arrow on his dot. Fed once
    /// a frame with where the frame draws him and the game clock, so a paused game keeps every arrow as it stood and a faster game
    /// changes nothing (units per GAME second). He walks when his smoothed speed is at least <see cref="OnSpeed"/> AND he made at
    /// least <see cref="OnGround"/> of net ground in the last <see cref="Window"/> (a fight's back and forth shows no arrow); the
    /// arrow goes when the speed falls under <see cref="OffSpeed"/> or the net ground under <see cref="OffGround"/>. The arrow
    /// points along the smoothed velocity. A jump (a load, a teleport) or a gap in the looks (the map was faded out) starts the
    /// man again with no arrow.
    /// </summary>
    internal sealed class Heading
    {
        public const double Smooth = 0.45;                    // game seconds the velocity is averaged over
        public const double Window = 1.5;                     // game seconds the net ground is measured over
        public const double OnSpeed = 8, OffSpeed = 4.5;      // units a game second (0.8 / 0.45 m/s)
        public const double OnGround = 14, OffGround = 6;     // units of net ground in the window (1.4 m / 0.6 m)
        public const double JumpUnits = 400;                  // a step this long between two looks: a load or a teleport
        public const double GapSeconds = 3;                   // no look for this many game seconds: learnt again
        public const int ForgetFrames = 600;                  // a man not seen for this many frames is forgotten
        const int Samples = 24;
        const double SampleEvery = Window / 16;

        sealed class Track
        {
            public double X, Z, T, Vx, Vz;
            public bool Moving;
            public long Seen;
            public readonly double[] Sx = new double[Samples], Sz = new double[Samples], St = new double[Samples];
            public int Count, Next;

            public void Reset(double x, double z, double t)
            {
                (X, Z, T, Vx, Vz, Moving, Count, Next) = (x, z, t, 0, 0, false, 0, 0);
                Push(x, z, t);
            }

            public void Push(double x, double z, double t)
            {
                (Sx[Next], Sz[Next], St[Next]) = (x, z, t);
                Next = (Next + 1) % Samples;
                if (Count < Samples) Count++;
            }

            public double LastSampleT => St[(Next + Samples - 1) % Samples];

            /// <summary>The net ground from the oldest look inside the window (the oldest kept, while the window is not full yet).</summary>
            public double Ground(double x, double z, double t)
            {
                int best = -1;
                double bestT = double.MaxValue;
                for (int i = 0; i < Count; i++)
                {
                    double st = St[i];
                    if (st >= t - Window - 1e-9 && st < bestT) (best, bestT) = (i, st);
                }
                if (best < 0) return 0;
                double dx = x - Sx[best], dz = z - Sz[best];
                return Math.Sqrt(dx * dx + dz * dz);
            }
        }

        readonly Dictionary<long, Track> _tracks = new Dictionary<long, Track>();
        readonly List<long> _drop = new List<long>();
        long _frame;

        public int Count => _tracks.Count;

        public void Clear() => _tracks.Clear();

        /// <summary>Once a frame, before the men of that frame.</summary>
        public void BeginFrame() => _frame++;

        /// <summary>One man this frame (key: his character id). True while he walks; (dx, dz): the unit direction (world X, Z).</summary>
        public bool Step(long key, double x, double z, double t, out double dx, out double dz)
        {
            dx = dz = 0;
            if (!double.IsFinite(x) || !double.IsFinite(z) || !double.IsFinite(t)) return false;
            if (!_tracks.TryGetValue(key, out var k))
            {
                k = new Track();
                k.Reset(x, z, t);
                k.Seen = _frame;
                _tracks[key] = k;
                return false;
            }
            k.Seen = _frame;
            double dtg = t - k.T, sx = x - k.X, sz = z - k.Z;
            if (sx * sx + sz * sz > JumpUnits * JumpUnits || dtg > GapSeconds || dtg < -1e-4)
            {
                k.Reset(x, z, t);
                return false;
            }
            if (dtg > 1e-4)
            {
                double a = 1 - Math.Exp(-dtg / Smooth);
                k.Vx += (sx / dtg - k.Vx) * a;
                k.Vz += (sz / dtg - k.Vz) * a;
                (k.X, k.Z, k.T) = (x, z, t);
                if (t - k.LastSampleT >= SampleEvery - 1e-9) k.Push(x, z, t);
                double speed = Math.Sqrt(k.Vx * k.Vx + k.Vz * k.Vz), ground = k.Ground(x, z, t);
                k.Moving = k.Moving ? speed >= OffSpeed && ground >= OffGround : speed >= OnSpeed && ground >= OnGround;
            }
            if (!k.Moving) return false;
            double v = Math.Sqrt(k.Vx * k.Vx + k.Vz * k.Vz);
            if (!(v > 1e-9)) return false;
            (dx, dz) = (k.Vx / v, k.Vz / v);
            return true;
        }

        /// <summary>Once a frame, after the men: those not seen for <see cref="ForgetFrames"/> frames are forgotten.</summary>
        public void EndFrame()
        {
            if (_frame % 60 != 0) return;
            _drop.Clear();
            foreach (var kv in _tracks)
                if (_frame - kv.Value.Seen > ForgetFrames) _drop.Add(kv.Key);
            foreach (long key in _drop) _tracks.Remove(key);
        }
    }

    /// <summary>The marks' layers, each switched on and off on its own (topcam layer &lt;name&gt; on|off). All on by default.
    /// names: your people's names; foes: the red dots of hostile characters; towns: the names of discovered towns and
    /// places; cone: the main camera's view cone; arrows: the heading arrows on walking people; scale: the scale bar;
    /// north: the N arrow; route: where the selected people walk to; hurt: a ring on anyone badly hurt. The dots of your
    /// people themselves have no switch. Other words: hostiles (foes), places (towns), routes / waypoints (route), health (hurt).</summary>
    internal static class MapLayers
    {
        public const int Names = 0, Foes = 1, Towns = 2, Cone = 3, Arrows = 4, Scale = 5, North = 6, Route = 7, Hurt = 8, Count = 9;
        public const int All = (1 << Count) - 1;
        public static readonly string[] Words = { "names", "foes", "towns", "cone", "arrows", "scale", "north", "route", "hurt" };
        public const string WordList = "names|foes|towns|cone|arrows|scale|north|route|hurt";

        public static bool On(int mask, int layer) => (mask & (1 << layer)) != 0;

        public static int Set(int mask, int layer, bool on) => on ? mask | (1 << layer) : mask & ~(1 << layer);

        /// <summary>A layer's name (any case; the singular too: name, foe, town, arrow; and the other words). -1: none.</summary>
        public static int Index(string word)
        {
            if (string.IsNullOrEmpty(word)) return -1;
            switch (word.ToLowerInvariant())
            {
                case "hostiles": case "hostile": return Foes;
                case "places": case "place": return Towns;
                case "routes": case "waypoints": case "waypoint": case "paths": case "path": return Route;
                case "health": return Hurt;
            }
            for (int i = 0; i < Words.Length; i++)
            {
                string w = Words[i];
                if (word.Equals(w, StringComparison.OrdinalIgnoreCase)) return i;
                if (w.EndsWith('s') && word.Length == w.Length - 1 && w.StartsWith(word, StringComparison.OrdinalIgnoreCase)) return i;
            }
            return -1;
        }

        /// <summary>"&lt;name&gt; on|off [&lt;name&gt; on|off ...]" or "all on|off" from a[from] on, applied to <paramref name="mask"/>
        /// all or nothing: null and the new mask, or the refusal (the mask unchanged).</summary>
        public static string Apply(IReadOnlyList<string> a, int from, int mask, out int result)
        {
            result = mask;
            int n = a.Count - from;
            if (n <= 0 || n % 2 != 0) return "usage: topcam layer [<" + WordList + "|all> on|off ...]   (no argument: the state)";
            int m = mask;
            for (int i = from; i < a.Count; i += 2)
            {
                if (!MapPrefs.OnOff(a[i + 1], out bool on)) return "topcam layer: on or off after '" + a[i] + "', not '" + a[i + 1] + "'";
                if (a[i].Equals("all", StringComparison.OrdinalIgnoreCase))
                {
                    m = on ? All : 0;
                    continue;
                }
                int k = Index(a[i]);
                if (k < 0) return "topcam layer: no layer '" + a[i] + "' (" + WordList + ", all)";
                m = Set(m, k, on);
            }
            result = m;
            return null;
        }

        public static void Json(System.Text.StringBuilder sb, int mask)
        {
            sb.Append('{');
            for (int i = 0; i < Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append('"').Append(Words[i]).Append("\":").Append(On(mask, i) ? "true" : "false");
            }
            sb.Append('}');
        }
    }

    /// <summary>
    /// The texts on the map are HUD captions, and the HUD reads MyGUI tags in a caption: '#' followed by six hex digits
    /// starts a colour, "##" prints one '#' (Meitou.Data.Ui.MyGuiTextTags). A name is shown as it is, so its '#' are doubled.
    /// </summary>
    internal static class CaptionText
    {
        /// <summary>A plain text as a caption: every '#' doubled. The same object when there is none.</summary>
        public static string Escape(string s) => string.IsNullOrEmpty(s) ? "" : s.IndexOf('#') < 0 ? s : s.Replace("#", "##");

        /// <summary>How many characters an escaped caption shows ("##" is one).</summary>
        public static int ShownLength(string caption)
        {
            if (string.IsNullOrEmpty(caption)) return 0;
            int n = 0;
            for (int i = 0; i < caption.Length; i++)
            {
                if (caption[i] == '#' && i + 1 < caption.Length && caption[i + 1] == '#') i++;
                n++;
            }
            return n;
        }
    }

    /// <summary>
    /// Where the place labels' texts go on the map so that none covers a squad name, a squad dot, another label's text or
    /// mark, or the map's own chrome (scale bar, N), all inside the picture. Each label tries its spots in a fixed order: above its
    /// mark, below, right, left, then rings of eight further out; its spot of the last frame comes first
    /// while it is still free (no jumping about). A label whose text stands off its mark gets a leader line (Moved). Labels are
    /// placed in priority order (the higher Priority first: a town before a smaller place; then the order they were given);
    /// one with no free spot is dropped (its mark stays, its text is not drawn).
    /// </summary>
    internal sealed class PlaceLayout
    {
        public const int Max = 48, MaxObstacles = 160, MaxLineObstacles = 48;
        public const float Pad = 3f;            // clear space round a text
        public const float Edge = 2f;           // texts stay this far inside the picture
        public const float Step = 16f;          // the rings' spacing, pixels
        public const int Rings = 4;
        public const float LeadFrom = 6f;       // a text this far (or more) off its usual spot gets a leader line

        /// <summary>A round map: above 0, every text stays inside this circle round the picture's middle (pixels).</summary>
        public float CircleR;

        // obstacles (x0, y0, x1, y1) and line obstacles (leader lines), set each frame
        public int Obstacles, LineObstacles;
        public readonly float[] OX0 = new float[MaxObstacles], OY0 = new float[MaxObstacles], OX1 = new float[MaxObstacles], OY1 = new float[MaxObstacles];
        public readonly float[] LX0 = new float[MaxLineObstacles], LY0 = new float[MaxLineObstacles], LX1 = new float[MaxLineObstacles], LY1 = new float[MaxLineObstacles];

        // in
        public int Count;
        public readonly long[] Key = new long[Max];
        public readonly float[] AnchorX = new float[Max], AnchorY = new float[Max], Above = new float[Max], W = new float[Max], H = new float[Max];
        public readonly int[] Priority = new int[Max];

        // out
        public readonly bool[] Shown = new bool[Max], Moved = new bool[Max];
        public readonly float[] X = new float[Max], Y = new float[Max];
        public int Dropped;

        Dictionary<long, int> _prev = new Dictionary<long, int>(), _next = new Dictionary<long, int>();
        readonly int[] _order = new int[Max];
        readonly float[] _px0 = new float[Max], _py0 = new float[Max], _px1 = new float[Max], _py1 = new float[Max];
        readonly Comparer<int> _byPriority;

        // the rings' directions: up, down, the four diagonals, right, left (up and down first: a text reads best over or under its mark)
        static readonly float[] RingX = { 0f, 0f, 0.7071f, -0.7071f, 0.7071f, -0.7071f, 1f, -1f };
        static readonly float[] RingY = { -1f, 1f, -0.7071f, -0.7071f, 0.7071f, 0.7071f, 0f, 0f };

        public PlaceLayout() =>
            _byPriority = Comparer<int>.Create((a, b) => Priority[a] != Priority[b] ? Priority[b].CompareTo(Priority[a]) : a.CompareTo(b));

        public void ClearObstacles() => Obstacles = LineObstacles = 0;

        public void AddObstacle(float x0, float y0, float x1, float y1)
        {
            if (Obstacles >= MaxObstacles || !(x1 > x0) || !(y1 > y0)) return;
            (OX0[Obstacles], OY0[Obstacles], OX1[Obstacles], OY1[Obstacles]) = (x0, y0, x1, y1);
            Obstacles++;
        }

        public void AddLineObstacle(float x0, float y0, float x1, float y1)
        {
            if (LineObstacles >= MaxLineObstacles || !float.IsFinite(x0) || !float.IsFinite(y0) || !float.IsFinite(x1) || !float.IsFinite(y1)) return;
            (LX0[LineObstacles], LY0[LineObstacles], LX1[LineObstacles], LY1[LineObstacles]) = (x0, y0, x1, y1);
            LineObstacles++;
        }

        static bool SegHitsBox(float x0, float y0, float x1, float y1, float bx0, float by0, float bx1, float by1)
        {
            float dx = x1 - x0, dy = y1 - y0;
            float t0 = 0f, t1 = 1f;
            if (!Clip(-dx, x0 - bx0, ref t0, ref t1)) return false;
            if (!Clip(dx, bx1 - x0, ref t0, ref t1)) return false;
            if (!Clip(-dy, y0 - by0, ref t0, ref t1)) return false;
            if (!Clip(dy, by1 - y0, ref t0, ref t1)) return false;
            return t0 <= t1;

            static bool Clip(float p, float q, ref float t0, ref float t1)
            {
                if (p == 0f) return q >= 0f;
                float r = q / p;
                if (p < 0f) { if (r > t1) return false; if (r > t0) t0 = r; }
                else { if (r < t0) return false; if (r < t1) t1 = r; }
                return true;
            }
        }

        public const int Spots = 4 + 8 * Rings;

        /// <summary>The top left corner of label i's text at spot s (0 above, 1 below, 2 right, 3 left, then the rings).</summary>
        public void Spot(int i, int s, out float x, out float y)
        {
            float ax = AnchorX[i], ay = AnchorY[i], w = W[i], h = H[i], above = Above[i];
            switch (s)
            {
                case 0: (x, y) = (ax - w * 0.5f, ay - above - h); return;
                case 1: (x, y) = (ax - w * 0.5f, ay + above); return;
                case 2: (x, y) = (ax + above + 2f, ay - h * 0.5f); return;
                case 3: (x, y) = (ax - above - 2f - w, ay - h * 0.5f); return;
            }
            int k = s - 4, ring = k / 8 + 1, dir = k % 8;
            float r = ring * Step;
            float cx = ax + RingX[dir] * (r + w * 0.5f + above), cy = ay + RingY[dir] * (r + h * 0.5f + above);
            (x, y) = (cx - w * 0.5f, cy - h * 0.5f);
        }

        bool Free(float x0, float y0, float x1, float y1, int placed, float rw, float rh)
        {
            if (x0 < Edge || y0 < Edge || x1 > rw - Edge || y1 > rh - Edge) return false;
            if (CircleR > 0 && !MapShape.BoxInCircle(x0, y0, x1, y1, rw * 0.5f, rh * 0.5f, CircleR - Edge)) return false;
            float a0 = x0 - Pad, b0 = y0 - Pad, a1 = x1 + Pad, b1 = y1 + Pad;
            for (int o = 0; o < Obstacles; o++)
                if (a0 < OX1[o] && OX0[o] < a1 && b0 < OY1[o] && OY0[o] < b1) return false;
            for (int l = 0; l < LineObstacles; l++)
                if (SegHitsBox(LX0[l], LY0[l], LX1[l], LY1[l], a0 - 1f, b0 - 1f, a1 + 1f, b1 + 1f)) return false;
            for (int j = 0; j < placed; j++)
                if (a0 < _px1[j] && _px0[j] < a1 && b0 < _py1[j] && _py0[j] < b1) return false;
            return true;
        }

        public void Solve(float rw, float rh)
        {
            int n = Math.Min(Count, Max);
            for (int i = 0; i < n; i++) _order[i] = i;
            Array.Sort(_order, 0, n, _byPriority);
            _next.Clear();
            int placed = 0;
            Dropped = 0;
            for (int k = 0; k < n; k++)
            {
                int i = _order[k];
                Shown[i] = Moved[i] = false;
                if (!(W[i] > 0) || !(H[i] > 0) || !float.IsFinite(AnchorX[i]) || !float.IsFinite(AnchorY[i])) { Dropped++; continue; }
                int chosen = -1;
                if (_prev.TryGetValue(Key[i], out int last) && last >= 0 && last < Spots)
                {
                    Spot(i, last, out float lx, out float ly);
                    // the last spot holds unless a spot nearer the mark (one of the first four) is free again
                    if (Free(lx, ly, lx + W[i], ly + H[i], placed, rw, rh)) chosen = last;
                }
                if (chosen < 0 || chosen >= 4)
                    for (int s = 0; s < (chosen < 0 ? Spots : 4); s++)
                    {
                        Spot(i, s, out float sx, out float sy);
                        if (Free(sx, sy, sx + W[i], sy + H[i], placed, rw, rh)) { chosen = s; break; }
                    }
                if (chosen < 0) { Dropped++; continue; }
                Spot(i, chosen, out float x, out float y);
                (X[i], Y[i], Shown[i]) = (x, y, true);
                Spot(i, 0, out float ux, out float uy);
                Moved[i] = chosen >= 4 || (chosen != 0 && chosen != 1 && MathF.Abs(x - ux) + MathF.Abs(y - uy) >= LeadFrom * 3f);
                (_px0[placed], _py0[placed], _px1[placed], _py1[placed]) = (x, y, x + W[i], y + H[i]);
                placed++;
                _next[Key[i]] = chosen;
            }
            (_prev, _next) = (_next, _prev);
        }

        /// <summary>Where a leader line from the mark meets label i's text box: the nearest point of the box to the mark.</summary>
        public void LeadEnd(int i, out float x, out float y)
        {
            x = Math.Clamp(AnchorX[i], X[i], X[i] + W[i]);
            y = Math.Clamp(AnchorY[i], Y[i], Y[i] + H[i]);
        }

        public void Forget() => _prev.Clear();

        /// <summary>A stable key for a label: its text and its spot on the ground (to 10 units).</summary>
        public static long KeyOf(string caption, double x, double z)
        {
            unchecked
            {
                long h = 1469598103934665603L;
                foreach (char c in caption ?? "") h = (h ^ c) * 1099511628211L;
                h = (h ^ (long)Math.Round(x / 10)) * 1099511628211L;
                return (h ^ (long)Math.Round(z / 10)) * 1099511628211L;
            }
        }
    }

    /// <summary>
    /// Which of the game's own towns and places get a label on the map, and under which name: the rules of the game's own
    /// world map. A place shows once the player has discovered it; until it has been explored it goes by its "unexplored"
    /// name when the game data gives one. Zoomed far out only towns are named, so the map does not fill with small places.
    /// The game side (Marks.GamePlaces) reads the placed towns and the player's knowledge of them and asks here.
    /// </summary>
    internal static class KnownPlaces
    {
        /// <summary>The map is wider than this (world units): only towns are named.</summary>
        public const double TownsOnlySpan = 20000;

        /// <summary>The name to show, or null for no label.</summary>
        public static string Name(bool discovered, bool explored, string name, string unexploredName)
        {
            if (!discovered) return null;
            string shown = explored || string.IsNullOrWhiteSpace(unexploredName) ? name : unexploredName;
            return string.IsNullOrWhiteSpace(shown) ? null : shown.Trim();
        }

        /// <summary>Whether a place is named at this zoom.</summary>
        public static bool Shows(bool isTown, double span) => isTown || span <= TownsOnlySpan;

        /// <summary>Whether a place at (x, z) can be on a picture of this view at all (a cheap test before the projection):
        /// within the larger of the picture's two ground extents of the centre along both world axes, which covers the
        /// picture at any turn (the picture's half diagonal on the ground is at most 0.71 of that).</summary>
        public static bool Near(in MapView v, double x, double z)
        {
            double sinP = Math.Max(v.SinPitch, 0.05);
            double reach = Math.Max(v.Span, (v.SpanZ + 1500.0 * Math.Abs(v.CosPitch)) / sinP);
            return Math.Abs(x - v.Cx) <= reach && Math.Abs(z - v.Cz) <= reach;
        }
    }

    /// <summary>
    /// A walking man's path on the map (the "route" layer): the picture points of his path, from where the frame draws him to
    /// the path's end, thinned so that no leg is shorter than <see cref="MinStep"/> pixels (the end itself is always kept) and
    /// no route has more than <see cref="MaxLegs"/> legs (past that the last leg runs straight to the end). Men walking together
    /// have near the same path: a route that ends within <see cref="SameEnd"/> pixels of a route already drawn this frame, and
    /// starts within <see cref="SameStart"/> of it, is the same route and is not drawn again. Per frame: BeginFrame; per man:
    /// Start, Add each point, Finish (true: draw X/Y[0..Count)). No allocation.
    /// </summary>
    internal sealed class RoutePlan
    {
        public const float MinStep = 6f, SameEnd = 12f, SameStart = 40f;
        public const int MaxLegs = 24, MaxRoutes = 16;

        public readonly float[] X = new float[MaxLegs + 1], Y = new float[MaxLegs + 1];
        public readonly bool[] Far = new bool[MaxLegs + 1];      // the leg that ends at this point is on the provisional, long-range part
        public int Count;
        public int Routes, Skipped;

        readonly float[] _sx = new float[MaxRoutes], _sy = new float[MaxRoutes], _ex = new float[MaxRoutes], _ey = new float[MaxRoutes];
        float _pendX, _pendY;
        bool _pend, _pendFar;

        public void BeginFrame() => Routes = Skipped = 0;

        public void Start(float x, float y)
        {
            Count = 0;
            _pend = false;
            if (!float.IsFinite(x) || !float.IsFinite(y)) return;
            (X[0], Y[0], Far[0], Count) = (x, y, false, 1);
        }

        public void Add(float x, float y, bool far)
        {
            if (Count == 0 || !float.IsFinite(x) || !float.IsFinite(y)) return;
            float dx = x - X[Count - 1], dy = y - Y[Count - 1];
            if (dx * dx + dy * dy < MinStep * MinStep)
            {
                (_pendX, _pendY, _pendFar, _pend) = (x, y, far, true);      // kept only if it turns out to be the end
                return;
            }
            Put(x, y, far);
        }

        void Put(float x, float y, bool far)
        {
            _pend = false;
            if (Count >= MaxLegs + 1) Count = MaxLegs;      // full: the last point moves on (the end is what matters)
            (X[Count], Y[Count], Far[Count]) = (x, y, far);
            Count++;
        }

        /// <summary>True: draw this route (it has a leg and is not one already drawn this frame).</summary>
        public bool Finish()
        {
            if (_pend && Count > 0) Put(_pendX, _pendY, _pendFar);
            if (Count < 2) return false;
            float ex = X[Count - 1], ey = Y[Count - 1];
            for (int i = 0; i < Routes; i++)
            {
                if (Near(ex, ey, _ex[i], _ey[i], SameEnd) && Near(X[0], Y[0], _sx[i], _sy[i], SameStart))
                {
                    Skipped++;
                    return false;
                }
            }
            if (Routes < MaxRoutes)
            {
                (_sx[Routes], _sy[Routes], _ex[Routes], _ey[Routes]) = (X[0], Y[0], ex, ey);
                Routes++;
            }
            return true;
        }

        static bool Near(float ax, float ay, float bx, float by, float d) => (ax - bx) * (ax - bx) + (ay - by) * (ay - by) <= d * d;
    }

    /// <summary>The round map's circle, and the hurt rings' levels.</summary>
    internal static class MapShape
    {
        public const double Hurt = 0.6, BadlyHurt = 0.3;

        /// <summary>The round map's radius on a picture w x h (the circle that fits, half a pixel in).</summary>
        public static float Radius(float w, float h) => MathF.Max(0f, MathF.Min(w, h) * 0.5f - 0.5f);

        /// <summary>Whether a box lies wholly inside the circle (all four corners).</summary>
        public static bool BoxInCircle(float x0, float y0, float x1, float y1, float cx, float cy, float r)
        {
            if (!(r > 0)) return false;
            float r2 = r * r;
            float ax = x0 - cx, bx = x1 - cx, ay = y0 - cy, by = y1 - cy;
            return ax * ax + ay * ay <= r2 && bx * bx + ay * ay <= r2 && ax * ax + by * by <= r2 && bx * bx + by * by <= r2;
        }

        /// <summary>How badly hurt a man is, from his blood (0..1 of capacity) and his worst body part (a fraction of its most
        /// HP; below 0 when it is wrecked): 0 not much, 1 hurt (under 0.6), 2 badly hurt (under 0.3). An unknown value counts as
        /// whole.</summary>
        public static int HurtLevel(double blood, double worstPart)
        {
            double b = double.IsFinite(blood) ? blood : 1, w = double.IsFinite(worstPart) ? worstPart : 1;
            double health = Math.Clamp(Math.Min(b, w), 0, 1);
            return health < BadlyHurt ? 2 : health < Hurt ? 1 : 0;
        }
    }
}
