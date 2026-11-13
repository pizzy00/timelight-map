// TimeLight Map: an in-game 3D picture-in-picture map for MeitouClient.
// Copyright (C) 2026 TimeLight Map contributors
// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.Globalization;

namespace TimeLightMap
{
    // The map's simple options: which corner, how big, how far in, what it follows, on or off.
    // Pure (no game types in this file): the words, the corner's rectangle for a window, and the config file
    // (timelight-map.cfg in the mod's folder) turned into the same topcam commands a player would type.
    internal static class MapPrefs
    {
        static readonly CultureInfo Ci = CultureInfo.InvariantCulture;

        public const string FileName = "timelight-map.cfg";
        public const double Aspect = 712.0 / 499.0;                        // the map's shape, width / height (about 10:7)
        public const double Small = 22, Medium = 34.6, Large = 46;         // the map's height, percent of the window's
        public const double MinSize = 10, MaxSize = 60;
        public const int DefaultCorner = 2;                                // top right
        public const int DefaultMargin = 24, MaxMargin = 400;
        public const double MinZoom = 10, MaxZoom = 5000;                  // metres across the map
        public const double DefaultZoom = 150;

        public const string Words = "corner <top-left|top-right|bottom-left|bottom-right|off> [small|medium|large|<10..60>] | size <small|medium|large|<10..60>>"
                                    + " | margin <0..400> | zoom <10..5000 metres across> | follow squad|camera|off";

        /// <summary>0 off, 1 top left, 2 top right, 3 bottom left, 4 bottom right.</summary>
        public static bool Corner(string word, out int corner)
        {
            corner = 0;
            switch ((word ?? "").ToLowerInvariant().Replace("_", "-"))
            {
                case "off": case "none": corner = 0; return true;
                case "top-left": case "tl": case "topleft": corner = 1; return true;
                case "top-right": case "tr": case "topright": corner = 2; return true;
                case "bottom-left": case "bl": case "bottomleft": corner = 3; return true;
                case "bottom-right": case "br": case "bottomright": corner = 4; return true;
            }
            return false;
        }

        public static string CornerName(int corner) => corner switch { 1 => "top-left", 2 => "top-right", 3 => "bottom-left", 4 => "bottom-right", _ => null };

        /// <summary>small / medium / large, or the map's height as a percent of the window's (10..60).</summary>
        public static bool Size(string word, out double percent)
        {
            percent = Medium;
            switch ((word ?? "").ToLowerInvariant())
            {
                case "small": percent = Small; return true;
                case "medium": percent = Medium; return true;
                case "large": percent = Large; return true;
            }
            return double.TryParse((word ?? "").TrimEnd('%'), NumberStyles.Float, Ci, out percent) && percent >= MinSize && percent <= MaxSize;
        }

        /// <summary>The map's rectangle (window pixels, origin top left) in a corner of a window: its height the percent of the
        /// window's, its width by the map's shape (never over 90% of the window's), the margin from both edges.</summary>
        public static void Rect(int corner, double percent, int margin, int winW, int winH, out int x, out int y, out int w, out int h)
        {
            winW = Math.Max(winW, 64);
            winH = Math.Max(winH, 64);
            margin = Math.Clamp(margin, 0, Math.Min(MaxMargin, Math.Min(winW, winH) / 4));
            h = (int)Math.Round(winH * Math.Clamp(percent, MinSize, MaxSize) / 100.0);
            w = (int)Math.Round(h * Aspect);
            if (w > winW * 0.9)
            {
                w = (int)(winW * 0.9);
                h = (int)Math.Round(w / Aspect);
            }
            w = Math.Clamp(w, 16, Math.Max(16, winW - 2 * margin));
            h = Math.Clamp(h, 16, Math.Max(16, winH - 2 * margin));
            x = corner is 1 or 3 ? margin : winW - margin - w;
            y = corner is 1 or 2 ? margin : winH - margin - h;
            x = Math.Max(0, x);
            y = Math.Max(0, y);
        }

        /// <summary>The config keys, in the order their commands run: the place and the view first, the marks, 'map' (on / off) last.</summary>
        static readonly string[] Keys = { "corner", "size", "margin", "follow", "zoom", "pitch", "alpha", "marks", "cone", "places", "map" };

        /// <summary>What the map does with no config file, and what a key missing from the file means.</summary>
        static readonly string[] Defaults = { "top-right", "medium", "24", "squad", "150", "90", "1", "on", "on", "on", "on" };

        /// <summary>
        /// The options as topcam commands, in the order they must run: the built-in defaults with the config file's values over
        /// them. Lines: key = value; '#' starts a comment; keys (any case): map (on|off), corner, size, margin, zoom (metres
        /// across), follow (squad|camera|off), pitch (35..90), alpha (0..1), marks (on|off), cone (on|off), places (on|off).
        /// Unknown keys and bad lines go to <paramref name="faults"/> and are skipped; the commands themselves refuse bad values
        /// (the built-in default then stands, since it is also the map's initial state). A null or empty text gives the defaults.
        /// </summary>
        public static List<string[]> Commands(string text, out List<string> faults)
        {
            faults = new List<string>();
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < Keys.Length; i++) values[Keys[i]] = Defaults[i];
            int n = 0;
            foreach (string raw in (text ?? "").Split('\n'))
            {
                n++;
                string line = raw;
                int hash = line.IndexOf('#');
                if (hash >= 0) line = line.Substring(0, hash);
                line = line.Trim();
                if (line.Length == 0) continue;
                int eq = line.IndexOf('=');
                if (eq <= 0)
                {
                    faults.Add("line " + n.ToString(Ci) + ": not 'key = value'");
                    continue;
                }
                string key = line.Substring(0, eq).Trim(), value = line.Substring(eq + 1).Trim().Trim('"');
                if (Array.IndexOf(Keys, key.ToLowerInvariant()) < 0)
                {
                    faults.Add("line " + n.ToString(Ci) + ": unknown key '" + key + "'");
                    continue;
                }
                if (value.Length == 0)
                {
                    faults.Add("line " + n.ToString(Ci) + ": '" + key + "' has no value");
                    continue;
                }
                values[key] = value;
            }

            if (!IsOnOff(values["map"]))
            {
                faults.Add("map: on or off, not '" + values["map"] + "'");
                values["map"] = "on";
            }
            // 'corner = off' leaves the rectangle to 'topcam rect': a size or a margin would put the map back in a corner
            bool free = Corner(values["corner"], out int corner) && corner == 0;
            var list = new List<string[]>();
            foreach (string key in Keys)
            {
                string v = values[key];
                if (key == "map") list.Add(new[] { v.ToLowerInvariant() });
                else if (free && key is "size" or "margin") continue;
                else if (key == "pitch") list.Add(new[] { key, v, "--over", "0" });      // at once, not a glide from straight down
                else list.Add(new[] { key, v });
            }
            return list;
        }

        static bool IsOnOff(string v) => v.Equals("on", StringComparison.OrdinalIgnoreCase) || v.Equals("off", StringComparison.OrdinalIgnoreCase);

        /// <summary>The sample config file shipped with the mod: every key at its default.</summary>
        public const string Sample =
            "# TimeLight Map options. To use: copy this file into the mod's folder as timelight-map.cfg and edit it.\n"
            + "# Read once when the game starts. A key you leave out keeps the value shown here.\n"
            + "# The same words work in the game's console while you play: topcam <key> <value>, e.g. topcam corner bottom-left\n"
            + "map = on              # on | off\n"
            + "corner = top-right    # top-left | top-right | bottom-left | bottom-right\n"
            + "size = medium         # small | medium | large | the map's height as a percent of the window's (10..60)\n"
            + "margin = 24           # pixels from the window's edges (0..400)\n"
            + "follow = squad        # squad (your people) | camera (where you look) | off (stays put)\n"
            + "zoom = 150            # metres across the map (10..5000)\n"
            + "pitch = 90            # 90 looks straight down; lower, down to 35: tilted, heights show\n"
            + "alpha = 1             # the map's strength: 1 solid, 0.5 half see-through (0..1)\n"
            + "marks = on            # dots, names, labels, scale bar, north arrow\n"
            + "cone = on             # the dotted cone showing where the main camera looks\n"
            + "places = on           # names of the towns and places you have discovered\n";
    }
}
