// TimeLight Map: an in-game 3D picture-in-picture map for MeitouClient.
// Copyright (C) 2026 TimeLight Map contributors
// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace TimeLightMap
{
    // The map's options: which corner, how big, how far in, what it follows, which marks, which keys.
    // Pure (no game types in this file): the words, the corner's rectangle for a window, the quality presets, the hotkey words,
    // and the options themselves as text (the config file timelight-map.cfg, the in-game settings page, 'topcam save'),
    // turned into the same topcam commands a player would type.
    internal static class MapPrefs
    {
        static readonly CultureInfo Ci = CultureInfo.InvariantCulture;

        public const string FileName = "timelight-map.cfg";
        public const double Aspect = 712.0 / 499.0;                        // the map's shape, width / height (about 10:7)
        public const double Small = 22, Medium = 34.6, Large = 46;         // the map's height, percent of the window's
        public const double MinSize = 10, MaxSize = 60;
        public const double BigSize = 72;                                  // the big map's height, percent of the window's
        public const double BigZoom = 3;                                   // the big map shows this many times the width
        public const int DefaultCorner = 2;                                // top right
        public const int DefaultMargin = 24, MaxMargin = 400;
        public const double MinZoom = 10, MaxZoom = 5000;                  // metres across the map
        public const double DefaultZoom = 150;
        public const double ZoomStep = 1.5;                                // 'topcam zoom in' / 'out' (and the hotkeys)

        public const string Words = "corner <top-left|top-right|bottom-left|bottom-right|free> [small|medium|large|<10..60>] | size <small|medium|large|<10..60>>"
                                    + " | margin <0..400> | zoom <10..5000 metres across>|in|out | follow squad|selected|camera|off";

        // =====================================================================================================
        //  words
        // =====================================================================================================

        /// <summary>0 free (the rect as 'topcam rect' set it), 1 top left, 2 top right, 3 bottom left, 4 bottom right.
        /// "off" and "none" are old words for free.</summary>
        public static bool Corner(string word, out int corner)
        {
            corner = 0;
            switch ((word ?? "").ToLowerInvariant().Replace("_", "-"))
            {
                case "free": case "off": case "none": corner = 0; return true;
                case "top-left": case "tl": case "topleft": corner = 1; return true;
                case "top-right": case "tr": case "topright": corner = 2; return true;
                case "bottom-left": case "bl": case "bottomleft": corner = 3; return true;
                case "bottom-right": case "br": case "bottomright": corner = 4; return true;
            }
            return false;
        }

        /// <summary>The corner's word; null for free (no corner).</summary>
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

        /// <summary>on / off (any case; also true / false, 1 / 0, yes / no). Anything else: false, and <paramref name="on"/> is false.</summary>
        public static bool OnOff(string word, out bool on)
        {
            switch ((word ?? "").Trim().ToLowerInvariant())
            {
                case "on": case "true": case "1": case "yes": on = true; return true;
                case "off": case "false": case "0": case "no": on = false; return true;
            }
            on = false;
            return false;
        }

        public static string OnOffWord(bool on) => on ? "on" : "off";

        /// <summary>The map's shape: square (the window's own map shape, about 10:7) or round (a circle).</summary>
        public static bool Shape(string word, out bool round)
        {
            switch ((word ?? "").ToLowerInvariant())
            {
                case "square": case "rect": case "rectangle": round = false; return true;
                case "round": case "circle": round = true; return true;
            }
            round = false;
            return false;
        }

        /// <summary>The picture layers that are rendered ('topcam layers'): one or more of t (terrain), o (objects, the buildings),
        /// f (foliage), c (characters), each once, in any order. The canonical text keeps the order t o f c.</summary>
        public static bool PictureLayers(string word, out string layers)
        {
            layers = null;
            string w = (word ?? "").Trim().ToLowerInvariant();
            if (w.Length == 0 || w.Length > 4) return false;
            foreach (char ch in w)
                if ("tofc".IndexOf(ch) < 0 || w.IndexOf(ch) != w.LastIndexOf(ch)) return false;
            var sb = new StringBuilder(4);
            foreach (char ch in "tofc")
                if (w.IndexOf(ch) >= 0) sb.Append(ch);
            layers = sb.ToString();
            return true;
        }

        /// <summary>'topcam flip': none, x, y, xy (or yx).</summary>
        public static bool Flip(string word, out bool x, out bool y)
        {
            switch ((word ?? "").ToLowerInvariant())
            {
                case "none": case "off": (x, y) = (false, false); return true;
                case "x": (x, y) = (true, false); return true;
                case "y": (x, y) = (false, true); return true;
                case "xy": case "yx": (x, y) = (true, true); return true;
            }
            (x, y) = (false, false);
            return false;
        }

        /// <summary>The picture's quality: how sharp, how often it is drawn, what it renders. high is what 1.2.0 did.
        /// <paramref name="moveFps"/> 0: a moving map is drawn every frame.</summary>
        public static bool Quality(string word, out float scale, out float fps, out float moveFps, out string layers)
        {
            switch ((word ?? "").ToLowerInvariant())
            {
                case "high": (scale, fps, moveFps, layers) = (1f, 30f, 0f, "tofc"); return true;
                case "medium": (scale, fps, moveFps, layers) = (0.75f, 20f, 30f, "tofc"); return true;
                case "low": (scale, fps, moveFps, layers) = (0.5f, 10f, 20f, "toc"); return true;
            }
            (scale, fps, moveFps, layers) = (1f, 30f, 0f, "tofc");
            return false;
        }

        /// <summary>The preset these settings are, or "custom".</summary>
        public static string QualityName(float scale, float fps, float moveFps, string layers)
        {
            foreach (string q in new[] { "high", "medium", "low" })
            {
                Quality(q, out float s, out float f, out float m, out string l);
                if (s == scale && f == fps && m == moveFps && l == layers) return q;
            }
            return "custom";
        }

        /// <summary>The next zoom (metres across) one step in or out, within the limits.</summary>
        public static double ZoomedMetres(double metres, bool zoomIn)
        {
            if (!(metres > 0) || double.IsInfinity(metres)) metres = DefaultZoom;
            double next = zoomIn ? metres / ZoomStep : metres * ZoomStep;
            return Math.Clamp(Math.Round(next, next < 100 ? 1 : 0), MinZoom, MaxZoom);
        }

        // ---- hotkeys ----

        /// <summary>The hotkey actions and their default keys (key names as MeitouClient's keyboard calls them).</summary>
        public static readonly string[] KeyActions = { "toggle", "big", "zoomin", "zoomout" };
        public static readonly string[] KeyDefaults = { "N", "Shift+N", "Equal", "Minus" };

        /// <summary>A hotkey as typed ("N", "shift+n", "Ctrl+Equal", "=", "off"): the modifiers and the key's name (null: no key).
        /// The name is the keyboard's own (Silk.NET Key) name with its first letter upper case; the game side checks it exists.</summary>
        public static bool KeySpec(string text, out bool ctrl, out bool shift, out bool alt, out string name)
        {
            (ctrl, shift, alt, name) = (false, false, false, null);
            string t = (text ?? "").Trim();
            if (t.Length == 0) return false;
            if (t.Equals("off", StringComparison.OrdinalIgnoreCase) || t.Equals("none", StringComparison.OrdinalIgnoreCase)) return true;
            // a lone '+' or '-' is the key itself; "ctrl++" is ctrl and '+'
            var parts = new List<string>();
            int start = 0;
            for (int i = 0; i < t.Length; i++)
            {
                if (t[i] != '+' || i == start) continue;
                parts.Add(t.Substring(start, i - start));
                start = i + 1;
            }
            parts.Add(t.Substring(start));
            for (int i = 0; i < parts.Count - 1; i++)
            {
                switch (parts[i].Trim().ToLowerInvariant())
                {
                    case "ctrl": case "control": ctrl = true; break;
                    case "shift": shift = true; break;
                    case "alt": alt = true; break;
                    default: return false;
                }
            }
            string key = parts[parts.Count - 1].Trim();
            if (key.Length == 0) return false;
            key = key switch
            {
                "=" => "Equal",
                "-" => "Minus",
                "+" => "KeypadAdd",
                "[" => "LeftBracket",
                "]" => "RightBracket",
                "\\" => "BackSlash",
                "'" => "Apostrophe",
                "/" => "Slash",
                _ => key,
            };
            foreach (char ch in key)
                if (!char.IsLetterOrDigit(ch)) return false;
            name = char.ToUpperInvariant(key[0]) + key.Substring(1);
            if (name.Length > 1 && char.IsLetter(name[1]) && name.ToUpperInvariant() == name) name = name[0] + name.Substring(1).ToLowerInvariant();   // "EQUAL" -> "Equal"
            return true;
        }

        /// <summary>A hotkey in its canonical words ("Ctrl+Shift+N", "off").</summary>
        public static string KeyText(bool ctrl, bool shift, bool alt, string name)
        {
            if (string.IsNullOrEmpty(name)) return "off";
            return (ctrl ? "Ctrl+" : "") + (shift ? "Shift+" : "") + (alt ? "Alt+" : "") + name;
        }

        // =====================================================================================================
        //  where the map goes
        // =====================================================================================================

        /// <summary>The map's rectangle (window pixels, origin top left) in a corner of a window: its height the percent of the
        /// window's, its width by the map's shape (<paramref name="round"/>: as wide as high; never over 90% of the window's),
        /// the margin from both edges.</summary>
        public static void Rect(int corner, double percent, int margin, int winW, int winH, out int x, out int y, out int w, out int h, bool round = false)
        {
            winW = Math.Max(winW, 64);
            winH = Math.Max(winH, 64);
            margin = Math.Clamp(margin, 0, Math.Min(MaxMargin, Math.Min(winW, winH) / 4));
            double aspect = round ? 1.0 : Aspect;
            h = (int)Math.Round(winH * Math.Clamp(percent, MinSize, MaxSize) / 100.0);
            w = (int)Math.Round(h * aspect);
            if (w > winW * 0.9)
            {
                w = (int)(winW * 0.9);
                h = (int)Math.Round(w / aspect);
            }
            w = Math.Clamp(w, 16, Math.Max(16, winW - 2 * margin));
            h = Math.Clamp(h, 16, Math.Max(16, winH - 2 * margin));
            if (round) w = h = Math.Min(w, h);
            x = corner is 1 or 3 ? margin : winW - margin - w;
            y = corner is 1 or 2 ? margin : winH - margin - h;
            x = Math.Max(0, x);
            y = Math.Max(0, y);
        }

        /// <summary>A round map in a free rect ('topcam rect', corner = free) that is not square: the square of its shorter side,
        /// kept to the rect's side nearer the window's edge on each axis (the middle when both gaps are equal), so a wide rect at
        /// the right of the window puts the circle at its right, with the rect's own margin, not at its left.</summary>
        public static void FreeSquare(int rx, int ry, int rw, int rh, int winW, int winH, out int x, out int y, out int s)
        {
            s = Math.Min(rw, rh);
            x = rx + Toward(rx, winW - rx - rw, rw - s);
            y = ry + Toward(ry, winH - ry - rh, rh - s);

            static int Toward(int before, int after, int spare) =>
                spare <= 0 ? 0 : after < before ? spare : after > before ? 0 : spare / 2;
        }

        /// <summary>The big map: in the middle of the window, <see cref="BigSize"/> percent of its height (the map's shape; round:
        /// as wide as high), never wider than 94% of the window.</summary>
        public static void BigRect(int winW, int winH, out int x, out int y, out int w, out int h, bool round = false)
        {
            winW = Math.Max(winW, 64);
            winH = Math.Max(winH, 64);
            double aspect = round ? 1.0 : Aspect;
            h = (int)Math.Round(winH * BigSize / 100.0);
            w = (int)Math.Round(h * aspect);
            if (w > winW * 0.94)
            {
                w = (int)(winW * 0.94);
                h = (int)Math.Round(w / aspect);
            }
            x = (winW - w) / 2;
            y = (winH - h) / 2;
        }

        /// <summary>A free rectangle as the config file writes it: "x y w h" (spaces or commas), w and h at least 16.</summary>
        public static bool RectText(string text, out int x, out int y, out int w, out int h)
        {
            (x, y, w, h) = (0, 0, 0, 0);
            var parts = (text ?? "").Split(new[] { ' ', ',', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 4) return false;
            var v = new int[4];
            for (int i = 0; i < 4; i++)
                if (!int.TryParse(parts[i], NumberStyles.Integer, Ci, out v[i])) return false;
            (x, y, w, h) = (v[0], v[1], v[2], v[3]);
            return w >= 16 && h >= 16 && x >= 0 && y >= 0;
        }

        // =====================================================================================================
        //  the options as text: the config file, the settings page, 'topcam save'
        // =====================================================================================================

        /// <summary>One option: its key, its default, the words of its comment in the config file.</summary>
        readonly struct Opt
        {
            public readonly string Key, Default, Note;
            public Opt(string key, string def, string note) => (Key, Default, Note) = (key, def, note);
        }

        /// <summary>The options, in the order their commands run: the place and the view first, then the marks and the keys,
        /// 'map' (on / off) last.</summary>
        static readonly Opt[] Opts =
        {
            new Opt("corner", "top-right", "top-left | top-right | bottom-left | bottom-right | free (free: at 'rect')"),
            new Opt("size", "medium", "small | medium | large | the map's height as a percent of the window's (10..60)"),
            new Opt("margin", "24", "pixels from the window's edges (0..400)"),
            new Opt("rect", "24 24 480 336", "x y w h in window pixels, used only with corner = free"),
            new Opt("shape", "square", "square | round"),
            new Opt("frame", "off", "on: a thin frame round the map"),
            new Opt("follow", "squad", "squad (your active squad) | selected (the people you selected) | camera (where you look) | off (stays put)"),
            new Opt("zoom", "150", "metres across the map (10..5000)"),
            new Opt("pitch", "90", "90 looks straight down; lower, down to 35: tilted, heights show"),
            new Opt("yaw", "0", "the map's turn: 0 north up, 90 east up (0..360)"),
            new Opt("alpha", "1", "the map's strength: 1 solid, 0.5 half see-through (0..1)"),
            new Opt("quality", "high", "low | medium | high: how sharp and how often the picture is drawn (low and medium are faster)"),
            new Opt("marks", "on", "everything drawn on the picture (all the marks below)"),
            new Opt("names", "on", "your people's names"),
            new Opt("hostiles", "on", "red dots on characters hostile to you"),
            new Opt("places", "on", "names of the towns and places you have discovered"),
            new Opt("cone", "on", "the dotted cone showing where the main camera looks"),
            new Opt("arrows", "on", "small arrows on your people while they walk"),
            new Opt("routes", "on", "where your selected people are walking to (their path and its end)"),
            new Opt("hurt", "on", "an orange or red ring on anyone badly hurt"),
            new Opt("scale", "on", "the scale bar"),
            new Opt("north", "on", "the north arrow"),
            new Opt("textscale", "1", "the size of the names and labels (0.5..3)"),
            new Opt("hidewithui", "on", "on: the map hides while the game's interface is hidden (F7, hud hide); off: it stays"),
            new Opt("keys", "on", "the map's hotkeys below (a key you bind in the game's own settings wins over them)"),
            new Opt("key_toggle", "N", "shows / hides the map"),
            new Opt("key_big", "Shift+N", "the big map in the middle of the screen, and back"),
            new Opt("key_zoomin", "Equal", "zoom in (Equal is the = key)"),
            new Opt("key_zoomout", "Minus", "zoom out"),
            new Opt("map", "on", "on | off: the map itself"),
        };

        /// <summary>The option keys, in run order.</summary>
        public static readonly string[] Keys = MakeKeys();

        static string[] MakeKeys()
        {
            var k = new string[Opts.Length];
            for (int i = 0; i < k.Length; i++) k[i] = Opts[i].Key;
            return k;
        }

        /// <summary>Other words the config file accepts for a key.</summary>
        static string Canonical(string key) => key.ToLowerInvariant() switch
        {
            "towns" => "places",
            "foes" => "hostiles",
            "route" => "routes",
            "waypoints" => "routes",
            string k => k,
        };

        /// <summary>The built-in defaults (what the map does with no config file), a new dictionary.</summary>
        public static Dictionary<string, string> Defaults()
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var o in Opts) values[o.Key] = o.Default;
            return values;
        }

        /// <summary>The built-in defaults with a config file's values over them. Lines: key = value; '#' starts a comment; keys in
        /// any case. Unknown keys and bad lines go to <paramref name="faults"/> and are skipped. A null or empty text gives the
        /// defaults. The values themselves are checked by the commands they become (a bad one is refused there and the
        /// built-in default, also the map's starting state, stands).</summary>
        public static Dictionary<string, string> Parse(string text, out List<string> faults)
        {
            faults = new List<string>();
            var values = Defaults();
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
                string key = Canonical(line.Substring(0, eq).Trim()), value = line.Substring(eq + 1).Trim().Trim('"');
                if (Array.IndexOf(Keys, key) < 0)
                {
                    faults.Add("line " + n.ToString(Ci) + ": unknown key '" + line.Substring(0, eq).Trim() + "'");
                    continue;
                }
                if (value.Length == 0)
                {
                    faults.Add("line " + n.ToString(Ci) + ": '" + key + "' has no value");
                    continue;
                }
                values[key] = value;
            }
            if (!OnOff(values["map"], out _))
            {
                faults.Add("map: on or off, not '" + values["map"] + "'");
                values["map"] = "on";
            }
            return values;
        }

        /// <summary>The topcam commands of one option's value (none for 'rect' unless the corner is free, nor for a size or a
        /// margin then).</summary>
        public static List<string[]> CommandsOf(string key, string value, bool freeCorner)
        {
            var list = new List<string[]>();
            string v = (value ?? "").Trim();
            switch (key)
            {
                case "map": list.Add(new[] { v.ToLowerInvariant() }); break;
                case "rect":
                    if (freeCorner && RectText(v, out int x, out int y, out int w, out int h))
                        list.Add(new[] { "rect", x.ToString(Ci), y.ToString(Ci), w.ToString(Ci), h.ToString(Ci) });
                    else if (freeCorner) list.Add(new[] { "rect", v });      // refused there, with its usage
                    break;
                case "size": case "margin":
                    if (!freeCorner) list.Add(new[] { key, v });
                    break;
                case "pitch": case "yaw": list.Add(new[] { key, v, "--over", "0" }); break;      // at once, not a glide
                case "names": case "cone": case "arrows": case "hurt": case "scale": case "north":
                    list.Add(new[] { "layer", key, v }); break;
                case "hostiles": list.Add(new[] { "layer", "foes", v }); break;
                case "places": list.Add(new[] { "layer", "towns", v }); break;
                case "routes": list.Add(new[] { "layer", "route", v }); break;
                case "key_toggle": case "key_big": case "key_zoomin": case "key_zoomout":
                    list.Add(new[] { "bind", key.Substring(4), v }); break;
                default: list.Add(new[] { key, v }); break;
            }
            return list;
        }

        /// <summary>Every option as topcam commands, in the order they must run.</summary>
        public static List<string[]> Commands(IReadOnlyDictionary<string, string> values)
        {
            bool free = Corner(Get(values, "corner"), out int corner) && corner == 0;
            var list = new List<string[]>();
            foreach (string key in Keys) list.AddRange(CommandsOf(key, Get(values, key), free));
            return list;
        }

        /// <summary>The options of a config file as topcam commands (<see cref="Parse"/>, then <see cref="Commands(IReadOnlyDictionary{string,string})"/>).</summary>
        public static List<string[]> Commands(string text, out List<string> faults) => Commands(Parse(text, out faults));

        static string Get(IReadOnlyDictionary<string, string> values, string key)
        {
            if (values != null && values.TryGetValue(key, out string v) && v != null) return v;
            foreach (var o in Opts)
                if (o.Key == key) return o.Default;
            return "";
        }

        /// <summary>A config file holding these values (the defaults for a key missing from them), each with its comment.</summary>
        public static string Render(IReadOnlyDictionary<string, string> values, string header = null)
        {
            var sb = new StringBuilder(2600);
            sb.Append(header ?? "# TimeLight Map options. To use: copy this file into the mod's folder as timelight-map.cfg and edit it.\n"
                                + "# Read once when the game starts. A key you leave out keeps the value shown here.\n"
                                + "# What you change on the game's Settings > Mods page (TimeLight Map) is kept by the game and wins over this file.\n"
                                + "# The same words work in the game's console while you play: topcam <key> <value>, e.g. topcam corner bottom-left\n");
            foreach (var o in Opts)
            {
                string line = o.Key + " = " + Get(values, o.Key);
                sb.Append(line);
                sb.Append(' ', Math.Max(1, 24 - line.Length));
                sb.Append("# ").Append(o.Note).Append('\n');
            }
            return sb.ToString();
        }

        /// <summary>The sample config file shipped with the mod: every key at its default.</summary>
        public static readonly string Sample = Render(Defaults());
    }
}
