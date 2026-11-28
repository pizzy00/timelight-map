// Console checks of MapPrefs: the corner words, the sizes, the corner's rectangle for a window, and the options
// (built-in defaults with timelight-map.cfg over them) turned into topcam commands.
using System;
using System.Linq;
using TimeLightMap;

static class PrefsChecks
{
    static string Flat(System.Collections.Generic.List<string[]> list) => string.Join("|", list.Select(l => string.Join(" ", l)));

    const string Marks = "marks on|layer names on|layer foes on|foelabels auto|layer traders on|layer towns on|layer cone on|layer arrows on|layer route on|layer hurt on|layer scale on|layer north on|squadbars off|clock off|cats off|speed off";
    const string Keys = "keys on|bind toggle N|bind big Shift+N|bind zoomin Equal|bind zoomout Minus";
    const string DefaultRun = "corner top-right|size medium|margin 24|shape square|frame off|follow squad|zoom 150|minzoom 50|autozoom on|pitch 90 --over 0|yaw 0 --over 0|alpha 1|quality high|"
                              + Marks + "|textscale 1|hidewithui on|" + Keys + "|on";

    public static void Run(Action<bool, string> Check)
    {
        Check(MapPrefs.Corner("top-right", out int c) && c == 2 && MapPrefs.Corner("TL", out c) && c == 1 && MapPrefs.Corner("bottom_left", out c) && c == 3
              && MapPrefs.Corner("br", out c) && c == 4 && MapPrefs.Corner("off", out c) && c == 0 && MapPrefs.Corner("free", out c) && c == 0, "corner words");
        Check(!MapPrefs.Corner("middle", out _) && !MapPrefs.Corner(null, out _), "an unknown corner is refused");
        Check(MapPrefs.CornerName(2) == "top-right" && MapPrefs.CornerName(0) == null, "corner names");
        Check(MapPrefs.CornerName(MapPrefs.DefaultCorner) == "top-right", "the default corner is the top right");
        Check(MapPrefs.Size("small", out double p) && p == 22 && MapPrefs.Size("Large", out p) && p == 46 && MapPrefs.Size("30", out p) && p == 30
              && MapPrefs.Size("40%", out p) && p == 40, "sizes: words and percents");
        Check(!MapPrefs.Size("9", out _) && !MapPrefs.Size("61", out _) && !MapPrefs.Size("huge", out _), "sizes out of 10..60 refused");
        MapPrefs.Rect(2, MapPrefs.Medium, 24, 2560, 1440, out int x, out int y, out int w, out int h);
        Check(w == 711 && h == 498 && x == 2560 - 24 - 711 && y == 24, "2560x1440, medium, top right: " + x + "," + y + " " + w + "x" + h);
        MapPrefs.Rect(2, 25, 24, 2560, 1440, out x, out y, out w, out h);
        Check(x == 2022 && y == 24 && w == 514 && h == 360, "2560x1440, size 25, top right: " + x + "," + y + " " + w + "x" + h);
        MapPrefs.Rect(1, MapPrefs.Medium, 24, 2560, 1440, out x, out y, out w, out h);
        Check(x == 24 && y == 24, "top left");
        MapPrefs.Rect(3, MapPrefs.Medium, 24, 2560, 1440, out x, out y, out w, out h);
        Check(x == 24 && y == 1440 - 24 - h, "bottom left");
        MapPrefs.Rect(4, MapPrefs.Small, 10, 1920, 1080, out x, out y, out w, out h);
        Check(x + w == 1910 && y + h == 1070 && h == 238 && w == 340, "1920x1080, small, bottom right: " + x + "," + y + " " + w + "x" + h);
        bool inside = true;
        var rng = new Random(5);
        for (int i = 0; i < 2000; i++)
        {
            int ww = rng.Next(1, 5000), wh = rng.Next(1, 3000);
            bool round = i % 2 == 1;
            MapPrefs.Rect(rng.Next(1, 5), rng.Next(0, 80), rng.Next(-5, 600), ww, wh, out x, out y, out w, out h, round);
            if (x < 0 || y < 0 || w < 16 || h < 16 || (ww >= 64 && x + w > ww) || (wh >= 64 && y + h > wh) || (round && w != h)) inside = false;
        }
        Check(inside, "2000 random windows (square and round): the map stays inside the window, never under 16 pixels, a round one square");

        // no config file: the built-in defaults, as commands in running order, the map on last
        var list = MapPrefs.Commands(null, out var faults);
        Check(faults.Count == 0 && Flat(list) == DefaultRun, "no file: the defaults: " + Flat(list));
        list = MapPrefs.Commands("", out faults);
        Check(faults.Count == 0 && Flat(list) == DefaultRun, "an empty file: the defaults");
        // the sample file says the same as the defaults
        list = MapPrefs.Commands(MapPrefs.Sample, out faults);
        Check(faults.Count == 0 && Flat(list) == DefaultRun, "the sample file equals the defaults, no fault (" + string.Join("; ", faults) + "): " + Flat(list));
        // a file's values go over the defaults; any case, quotes, comments, Windows line ends; faults named by line
        list = MapPrefs.Commands("MAP=off\r\n# a comment\r\n\r\nCorner = \"bottom-left\"   # here\nvolume = 3\nzoom\nsize =\nCONE = off\nzoom = 400\n", out faults);
        Check(Flat(list) == DefaultRun.Replace("corner top-right", "corner bottom-left").Replace("zoom 150", "zoom 400").Replace("layer cone on", "layer cone off")
                  .Replace("|on", "|off"),
            "a file over the defaults, off last: " + Flat(list));
        Check(faults.Count == 3 && faults[0].Contains("line 5") && faults[0].Contains("unknown key 'volume'") && faults[1].Contains("line 6")
              && faults[1].Contains("not 'key = value'") && faults[2].Contains("line 7") && faults[2].Contains("no value"), "faults named by line: " + string.Join("; ", faults));
        list = MapPrefs.Commands("map = maybe", out faults);
        Check(faults.Count == 1 && faults[0].Contains("maybe") && list[list.Count - 1].Length == 1 && list[list.Count - 1][0] == "on", "map takes on or off only; a bad value leaves it on");
        // corner free (or the old word off): no size, no margin; the rect from the file instead
        list = MapPrefs.Commands("corner = off\nsize = large", out faults);
        Check(faults.Count == 0 && Flat(list).StartsWith("corner off|rect 24 24 480 336|shape square|"), "corner off: size and margin are not run, the rect is: " + Flat(list));
        list = MapPrefs.Commands("corner = free\nrect = 100, 50, 400 300", out faults);
        Check(faults.Count == 0 && Flat(list).StartsWith("corner free|rect 100 50 400 300|shape square|"), "corner free with a rect: " + Flat(list));
        list = MapPrefs.Commands("corner = free\nrect = 1 2 3", out faults);
        Check(Flat(list).StartsWith("corner free|rect 1 2 3|"), "a bad rect goes to the command, which refuses it with its usage");
        list = MapPrefs.Commands("rect = 100 50 400 300", out faults);
        Check(!Flat(list).Contains("rect "), "a rect without corner free is not run");
        // every key is in the sample, with its comment
        foreach (string key in MapPrefs.Keys)
            Check(MapPrefs.Sample.Contains("\n" + key + " = ") || MapPrefs.Sample.StartsWith(key + " = "), "the sample file has the key '" + key + "'");
        // the old 1.2.0 keys still work
        foreach (string key in new[] { "map", "corner", "size", "margin", "follow", "zoom", "pitch", "alpha", "marks", "cone", "places", "hidewithui" })
            Check(Array.IndexOf(MapPrefs.Keys, key) >= 0, "the 1.2.0 key '" + key + "' is still a key");
        // hidewithui from the file: off keeps the map on screen with the interface hidden
        list = MapPrefs.Commands("hidewithui = off", out faults);
        Check(faults.Count == 0 && Flat(list).Contains("|hidewithui off|"), "hidewithui = off from the file: " + Flat(list));
        // a pitch from the file is set at once
        list = MapPrefs.Commands("pitch = 60", out faults);
        Check(Flat(list).Contains("|pitch 60 --over 0|"), "pitch from the file: at once");
    }
}
