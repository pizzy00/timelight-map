// Console checks of PlaceLayout (MapMath.cs): the place labels' texts laid out round your people's names and dots: never on an
// obstacle or on each other, inside the picture, above the mark when there is room, a leader line when moved, the lowest kind
// dropped first, and the same spot from one frame to the next.
using System;
using System.Collections.Generic;
using TimeLightMap;

static class PlaceChecks
{
    static Action<bool, string> _check;

    static void Check(bool ok, string what) => _check(ok, what);

    public static void Run(Action<bool, string> check)
    {
        _check = check;
        Basics();
        BusyTown();
        Crowd();
        Stable();
        Random_();
    }

    static void Add(PlaceLayout p, string text, float x, float y, int kind, float w = 90f, float h = 20f)
    {
        int n = p.Count++;
        p.Key[n] = PlaceLayout.KeyOf(text, x * 10, y * 10);
        (p.AnchorX[n], p.AnchorY[n], p.Above[n], p.W[n], p.H[n], p.Priority[n]) = (x, y, 7f, w, h, kind);
    }

    static bool Overlap(float ax0, float ay0, float ax1, float ay1, float bx0, float by0, float bx1, float by1) =>
        ax0 < bx1 && bx0 < ax1 && ay0 < by1 && by0 < ay1;

    /// <summary>No shown text on an obstacle, on another shown text, or outside the picture.</summary>
    static bool Clean(PlaceLayout p, float rw, float rh, out string why)
    {
        why = null;
        for (int i = 0; i < p.Count; i++)
        {
            if (!p.Shown[i]) continue;
            float x0 = p.X[i], y0 = p.Y[i], x1 = x0 + p.W[i], y1 = y0 + p.H[i];
            if (x0 < 0 || y0 < 0 || x1 > rw || y1 > rh) { why = "label " + i + " outside"; return false; }
            for (int o = 0; o < p.Obstacles; o++)
                if (Overlap(x0, y0, x1, y1, p.OX0[o], p.OY0[o], p.OX1[o], p.OY1[o])) { why = "label " + i + " on obstacle " + o; return false; }
            for (int j = i + 1; j < p.Count; j++)
                if (p.Shown[j] && Overlap(x0, y0, x1, y1, p.X[j], p.Y[j], p.X[j] + p.W[j], p.Y[j] + p.H[j])) { why = "labels " + i + " and " + j; return false; }
        }
        return true;
    }

    static void Basics()
    {
        var p = new PlaceLayout();
        Add(p, "Bar", 300, 200, 0);
        p.Solve(712, 499);
        Check(p.Shown[0] && !p.Moved[0] && p.X[0] == 300 - 45 && p.Y[0] == 200 - 7 - 20, "a free label: above its mark, centred, no leader line");
        // a person's name right where the label would go: it moves, with a leader line or to a near spot
        p = new PlaceLayout();
        p.AddObstacle(240, 160, 360, 195);
        Add(p, "Bar", 300, 200, 0);
        p.Solve(712, 499);
        bool clean = Clean(p, 712, 499, out string why);
        Check(p.Shown[0] && clean && p.Y[0] > 200, "a name over the mark: the label goes below it" + (why != null ? " (" + why + ")" : ""));
        // near the top edge: kept inside the picture
        p = new PlaceLayout();
        Add(p, "Edge Town", 30, 5, 1);
        p.Solve(712, 499);
        Check(p.Shown[0] && p.X[0] >= PlaceLayout.Edge && p.Y[0] >= PlaceLayout.Edge, "at the picture's corner: kept inside");
        // nowhere to go: dropped
        p = new PlaceLayout();
        p.AddObstacle(0, 0, 712, 499);
        Add(p, "Bar", 300, 200, 0);
        p.Solve(712, 499);
        Check(!p.Shown[0] && p.Dropped == 1, "no room anywhere: dropped");
    }

    /// <summary>A busy town at a 50 m scale: a column of eight names, their dots and four trader labels in one pile.</summary>
    static void BusyTown()
    {
        var p = new PlaceLayout();
        float colX = 330, colY = 150;
        const int rows = 8;
        for (int i = 0; i < rows; i++)
        {
            p.AddObstacle(colX, colY + i * 23f, colX + 70f, colY + i * 23f + 20f);      // the names (row 20, gap 3)
            p.AddObstacle(300 + (i % 3) * 6 - 6, 230 + (i / 3) * 6 - 6, 300 + (i % 3) * 6 + 6, 230 + (i / 3) * 6 + 6);      // the dots
        }
        Add(p, "Shinobi Trader", 310, 200, 2, 120);
        Add(p, "Ronin Construction Trader", 340, 215, 2, 210);
        Add(p, "Barman", 300, 240, 0, 70);
        Add(p, "Skeleton Doctor the Trader", 320, 260, 2, 220);
        p.Solve(712, 499);
        int shown = 0;
        for (int i = 0; i < p.Count; i++) if (p.Shown[i]) shown++;
        Check(Clean(p, 712, 499, out string why) && shown == 4, "a busy town: all four labels shown, none on a name, a dot or each other ("
                                                                 + shown + " shown" + (why != null ? "; " + why : "") + ")");
        int moved = 0;
        for (int i = 0; i < p.Count; i++) if (p.Moved[i]) moved++;
        Check(moved >= 1, "a busy town: labels pushed off the pile get leader lines (" + moved + ")");
    }

    static void Crowd()
    {
        // far more labels than room: the highest kind stays, the lowest goes first
        var p = new PlaceLayout();
        for (int i = 0; i < 40; i++) Add(p, "Place " + i, 100 + (i % 8) * 4, 100 + (i / 8) * 4, i < 30 ? 0 : i < 36 ? 2 : 4, 160, 24);
        p.Solve(400, 300);
        bool top = true;
        for (int i = 36; i < 40; i++) top &= p.Shown[i];
        Check(Clean(p, 400, 300, out string why) && top && p.Dropped > 0, "a crowd: no overlaps, every label of the highest kind shown, the lowest dropped first ("
                                                                             + p.Dropped + " dropped" + (why != null ? "; " + why : "") + ")");
    }

    static void Stable()
    {
        // the same scene, the marks drifting by a pixel a frame: the texts keep their spots (no flicker)
        var p = new PlaceLayout();
        int changes = 0;
        float[] lastX = new float[3], lastY = new float[3];
        for (int f = 0; f < 120; f++)
        {
            float d = (f % 2) * 1f;
            p.ClearObstacles();
            p.Count = 0;
            p.AddObstacle(250 + d, 170, 360 + d, 196);
            Add(p, "Shinobi Trader", 300 + d, 200, 2, 120);
            Add(p, "Barman", 305 + d, 205, 0, 70);
            Add(p, "Bar", 295 + d, 210, 0, 40);
            p.Solve(712, 499);
            for (int i = 0; i < 3; i++)
            {
                if (f > 0 && (Math.Abs(p.X[i] - lastX[i]) > 1.5f || Math.Abs(p.Y[i] - lastY[i]) > 1.5f)) changes++;
                (lastX[i], lastY[i]) = (p.X[i], p.Y[i]);
            }
        }
        Check(changes == 0, "120 frames of a jittering scene: no text jumps (" + changes + " jumps)");
    }

    static void Random_()
    {
        var rng = new Random(1113);
        int bad = 0;
        string first = null;
        var p = new PlaceLayout();
        for (int n = 0; n < 2000; n++)
        {
            p.ClearObstacles();
            p.Count = 0;
            float rw = rng.Next(300, 900), rh = rng.Next(200, 700);
            for (int o = rng.Next(0, 30); o > 0; o--)
            {
                float x = rng.Next(0, (int)rw), y = rng.Next(0, (int)rh);
                p.AddObstacle(x, y, x + rng.Next(4, 90), y + rng.Next(4, 24));
            }
            for (int l = rng.Next(0, 20); l > 0; l--)
                Add(p, "L" + l, rng.Next(0, (int)rw), rng.Next(0, (int)rh), rng.Next(0, 5), rng.Next(20, 220), rng.Next(14, 26));
            p.Solve(rw, rh);
            if (!Clean(p, rw, rh, out string why)) { bad++; first ??= "case " + n + ": " + why; }
        }
        Check(bad == 0, "2000 random maps: no shown text on an obstacle, on another text or outside the picture" + (first != null ? " (" + first + ")" : ""));
    }
}
