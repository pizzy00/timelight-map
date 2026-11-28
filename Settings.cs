// TimeLight Map: an in-game 3D picture-in-picture map for MeitouClient.
// Copyright (C) 2026 TimeLight Map contributors
// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using HarmonyLib;
using Meitou.Game;
using Meitou.Game.Host;
using Meitou.Game.Host.Input;
using Meitou.Modding;
using SilkKey = Silk.NET.Input.Key;

namespace TimeLightMap
{
    /// <summary>
    /// The map's group on the game's Settings screen, Mods page. Each property is one row; its value when the group is
    /// registered (MapOptions.Start: the config file's value, or the built-in default) is the row's default, and only a value
    /// the player changed on the page is kept by the game (UserConfig.ModSettings), so the page starts from the config file
    /// and wins over it. A change runs the matching topcam command (MapOptions.Changed). Margin, rect and yaw are in the
    /// config file and the console only.
    /// </summary>
    public sealed class MapSettings
    {
        public static readonly string[] CornerOptions = { "top-left", "top-right", "bottom-left", "bottom-right", "free" };
        public static readonly string[] ShapeOptions = { "square", "round" };
        public static readonly string[] FollowOptions = { "squad", "selected", "camera", "off" };
        public static readonly string[] QualityOptions = { "low", "medium", "high" };
        // keys no default game binding uses (and Shift / Ctrl ones of them); "custom": the config file's own key
        public static readonly string[] KeyOptions =
        {
            "N", "Shift+N", "Ctrl+N", "V", "Shift+V", "U", "O", "X", "Z", "Equal", "Shift+Equal", "Minus", "Shift+Minus",
            "BackSlash", "Apostrophe", "custom", "off",
        };

        [Setting("Map", Hint = "The 3D map on screen (the toggle key below switches it too)")]
        public bool Map { get; set; } = true;

        [Setting("Corner", Hint = "Where the map sits; free: where the config file's rect puts it", Options = new[] { "top-left", "top-right", "bottom-left", "bottom-right", "free" })]
        public string Corner { get; set; } = "top-right";

        [Setting("Size", Hint = "The map's height, percent of the window's", Min = 10, Max = 60, Step = 1)]
        public float Size { get; set; } = 34.6f;

        [Setting("Shape", Hint = "Square, or a round map", Options = new[] { "square", "round" })]
        public string Shape { get; set; } = "square";

        [Setting("Frame", Hint = "A thin frame round the map")]
        public bool Frame { get; set; }

        [Setting("Follow", Hint = "squad: your active squad; selected: the people you select; camera: where you look; off: stays put",
            Options = new[] { "squad", "selected", "camera", "off" })]
        public string Follow { get; set; } = "squad";

        [Setting("Zoom", Hint = "Metres across the map (widest zoom when Auto zoom is on; the zoom keys change it while you play)", Min = 10, Max = 5000, Step = 10)]
        public float Zoom { get; set; } = 150f;

        [Setting("Auto zoom", Hint = "Zooms in closer when your followed group is huddled together, out as they spread")]
        public bool AutoZoom { get; set; } = true;

        [Setting("Close zoom", Hint = "Closest metres across when your group is tightly grouped (with Auto zoom on)", Min = 10, Max = 500, Step = 5)]
        public float MinZoom { get; set; } = 50f;

        [Setting("Tilt", Hint = "90 looks straight down; lower tilts the view so heights show", Min = 35, Max = 90, Step = 1)]
        public float Pitch { get; set; } = 90f;

        [Setting("Strength", Hint = "1 solid; lower lets the game show through", Min = 0.1, Max = 1, Step = 0.05)]
        public float Alpha { get; set; } = 1f;

        [Setting("Quality", Hint = "high: sharp, 30 pictures a second; medium and low cost less", Options = new[] { "low", "medium", "high" })]
        public string Quality { get; set; } = "high";

        [Setting("Marks", Hint = "Everything drawn on the picture (the rows below)")]
        public bool Marks { get; set; } = true;

        [Setting("Names", Hint = "Your people's names")]
        public bool Names { get; set; } = true;

        [Setting("Hostiles", Hint = "Red dots on characters hostile to you")]
        public bool Hostiles { get; set; } = true;

        [Setting("Moving traders", Hint = "Green markers and names on roaming trade caravans")]
        public bool Traders { get; set; } = true;

        [Setting("Places", Hint = "Names of the towns and places you have discovered")]
        public bool Places { get; set; } = true;

        [Setting("View cone", Hint = "Where the main camera looks")]
        public bool Cone { get; set; } = true;

        [Setting("Walk arrows", Hint = "Small arrows on your people while they walk")]
        public bool Arrows { get; set; } = true;

        [Setting("Routes", Hint = "Where your selected people are walking to")]
        public bool Routes { get; set; } = true;

        [Setting("Hurt rings", Hint = "An orange or red ring on anyone badly hurt")]
        public bool Hurt { get; set; } = true;

        [Setting("Scale bar")]
        public bool ScaleBar { get; set; } = true;

        [Setting("North arrow")]
        public bool North { get; set; } = true;

        [Setting("Text size", Min = 0.5, Max = 3, Step = 0.1)]
        public float TextScale { get; set; } = 1f;

        [Setting("Hide with the interface", Hint = "The map hides while the game's interface is hidden (F7)")]
        public bool HideWithUi { get; set; } = true;

        [Setting("Hotkeys", Hint = "The map's keys below; a key you bind in the game's own controls wins over them")]
        public bool Keys { get; set; } = true;

        [Setting("Key: show / hide", Options = new[] { "N", "Shift+N", "Ctrl+N", "V", "Shift+V", "U", "O", "X", "Z", "Equal", "Shift+Equal", "Minus", "Shift+Minus", "BackSlash", "Apostrophe", "custom", "off" })]
        public string KeyToggle { get; set; } = "N";

        [Setting("Key: big map", Options = new[] { "N", "Shift+N", "Ctrl+N", "V", "Shift+V", "U", "O", "X", "Z", "Equal", "Shift+Equal", "Minus", "Shift+Minus", "BackSlash", "Apostrophe", "custom", "off" })]
        public string KeyBig { get; set; } = "Shift+N";

        [Setting("Key: zoom in", Options = new[] { "N", "Shift+N", "Ctrl+N", "V", "Shift+V", "U", "O", "X", "Z", "Equal", "Shift+Equal", "Minus", "Shift+Minus", "BackSlash", "Apostrophe", "custom", "off" })]
        public string KeyZoomIn { get; set; } = "Equal";

        [Setting("Key: zoom out", Options = new[] { "N", "Shift+N", "Ctrl+N", "V", "Shift+V", "U", "O", "X", "Z", "Equal", "Shift+Equal", "Minus", "Shift+Minus", "BackSlash", "Apostrophe", "custom", "off" })]
        public string KeyZoomOut { get; set; } = "Minus";

        /// <summary>Each row's property and its option key (MapPrefs.Keys).</summary>
        internal static readonly (string Property, string Key)[] Rows =
        {
            ("Map", "map"), ("Corner", "corner"), ("Size", "size"), ("Shape", "shape"), ("Frame", "frame"), ("Follow", "follow"),
            ("Zoom", "zoom"), ("AutoZoom", "autozoom"), ("MinZoom", "minzoom"), ("Pitch", "pitch"), ("Alpha", "alpha"), ("Quality", "quality"), ("Marks", "marks"), ("Names", "names"),
            ("Hostiles", "hostiles"), ("Traders", "traders"), ("Places", "places"), ("Cone", "cone"), ("Arrows", "arrows"), ("Routes", "routes"), ("Hurt", "hurt"),
            ("ScaleBar", "scale"), ("North", "north"), ("TextScale", "textscale"), ("HideWithUi", "hidewithui"), ("Keys", "keys"),
            ("KeyToggle", "key_toggle"), ("KeyBig", "key_big"), ("KeyZoomIn", "key_zoomin"), ("KeyZoomOut", "key_zoomout"),
        };

        internal static string KeyOf(string property)
        {
            foreach (var (p, k) in Rows)
                if (p == property) return k;
            return null;
        }

        static readonly CultureInfo Ci = CultureInfo.InvariantCulture;

        static string Pick(string[] options, string word, string otherwise)
        {
            foreach (string o in options)
                if (o.Equals(word?.Trim(), StringComparison.OrdinalIgnoreCase)) return o;
            return otherwise;
        }

        static float Number(string text, float min, float max, float otherwise) =>
            float.TryParse((text ?? "").Trim(), NumberStyles.Float, Ci, out float v) && float.IsFinite(v) ? Math.Clamp(v, min, max) : otherwise;

        static bool Switch(string text, bool otherwise) => MapPrefs.OnOff(text, out bool on) ? on : otherwise;

        static string KeyChoice(string text, string otherwise)
        {
            if (!MapPrefs.KeySpec(text, out bool c, out bool s, out bool a, out string name)) return otherwise;
            return Pick(KeyOptions, MapPrefs.KeyText(c, s, a, name), "custom");
        }

        /// <summary>One row set from an option's value in the config file's words (a value the row cannot show: its nearest,
        /// or the built-in default; a key the list does not hold: "custom").</summary>
        internal void Set(string key, string text)
        {
            switch (key)
            {
                case "map": Map = Switch(text, true); break;
                case "corner": Corner = MapPrefs.Corner(text, out int corner) ? MapPrefs.CornerName(corner) ?? "free" : "top-right"; break;
                case "size": Size = MapPrefs.Size(text, out double pct) ? (float)pct : (float)MapPrefs.Medium; break;
                case "shape": Shape = MapPrefs.Shape(text, out bool round) && round ? "round" : "square"; break;
                case "frame": Frame = Switch(text, false); break;
                case "follow": Follow = Pick(FollowOptions, text, "squad"); break;
                case "zoom": Zoom = Number(text, (float)MapPrefs.MinZoom, (float)MapPrefs.MaxZoom, (float)MapPrefs.DefaultZoom); break;
                case "autozoom": AutoZoom = Switch(text, true); break;
                case "minzoom": MinZoom = Number(text, (float)MapPrefs.MinZoom, 500f, (float)MapPrefs.DefaultMinZoom); break;
                case "pitch": Pitch = Number(text, TopCam.MinPitch, 90f, 90f); break;
                case "alpha": Alpha = Number(text, 0.1f, 1f, 1f); break;
                case "quality": Quality = Pick(QualityOptions, text, "high"); break;
                case "marks": Marks = Switch(text, true); break;
                case "names": Names = Switch(text, true); break;
                case "hostiles": Hostiles = Switch(text, true); break;
                case "traders": Traders = Switch(text, true); break;
                case "places": Places = Switch(text, true); break;
                case "cone": Cone = Switch(text, true); break;
                case "arrows": Arrows = Switch(text, true); break;
                case "routes": Routes = Switch(text, true); break;
                case "hurt": Hurt = Switch(text, true); break;
                case "scale": ScaleBar = Switch(text, true); break;
                case "north": North = Switch(text, true); break;
                case "textscale": TextScale = Number(text, 0.5f, 3f, 1f); break;
                case "hidewithui": HideWithUi = Switch(text, true); break;
                case "keys": Keys = Switch(text, true); break;
                case "key_toggle": KeyToggle = KeyChoice(text, "N"); break;
                case "key_big": KeyBig = KeyChoice(text, "Shift+N"); break;
                case "key_zoomin": KeyZoomIn = KeyChoice(text, "Equal"); break;
                case "key_zoomout": KeyZoomOut = KeyChoice(text, "Minus"); break;
            }
        }

        /// <summary>One row's value in the config file's words ("custom" for a key: the config file's own).</summary>
        internal string Text(string key) => key switch
        {
            "map" => MapPrefs.OnOffWord(Map),
            "corner" => Corner,
            "size" => Size.ToString("0.#", Ci),
            "shape" => Shape,
            "frame" => MapPrefs.OnOffWord(Frame),
            "follow" => Follow,
            "zoom" => Zoom.ToString("0.#", Ci),
            "autozoom" => MapPrefs.OnOffWord(AutoZoom),
            "minzoom" => MinZoom.ToString("0.#", Ci),
            "pitch" => Pitch.ToString("0.#", Ci),
            "alpha" => Alpha.ToString("0.##", Ci),
            "quality" => Quality,
            "marks" => MapPrefs.OnOffWord(Marks),
            "names" => MapPrefs.OnOffWord(Names),
            "hostiles" => MapPrefs.OnOffWord(Hostiles),
            "traders" => MapPrefs.OnOffWord(Traders),
            "places" => MapPrefs.OnOffWord(Places),
            "cone" => MapPrefs.OnOffWord(Cone),
            "arrows" => MapPrefs.OnOffWord(Arrows),
            "routes" => MapPrefs.OnOffWord(Routes),
            "hurt" => MapPrefs.OnOffWord(Hurt),
            "scale" => MapPrefs.OnOffWord(ScaleBar),
            "north" => MapPrefs.OnOffWord(North),
            "textscale" => TextScale.ToString("0.##", Ci),
            "hidewithui" => MapPrefs.OnOffWord(HideWithUi),
            "keys" => MapPrefs.OnOffWord(Keys),
            "key_toggle" => KeyToggle,
            "key_big" => KeyBig,
            "key_zoomin" => KeyZoomIn,
            "key_zoomout" => KeyZoomOut,
            _ => null,
        };

        /// <summary>The rows from a set of options (the config file's words).</summary>
        internal static MapSettings From(IReadOnlyDictionary<string, string> values)
        {
            var s = new MapSettings();
            foreach (var (_, key) in Rows)
                if (values.TryGetValue(key, out string text)) s.Set(key, text);
            return s;
        }
    }

    /// <summary>
    /// Where the map's options come from and go to: the built-in defaults, the config file (timelight-map.cfg in the mod's
    /// folder) over them, the settings page's saved values over that (MapSettings), and 'topcam save' (the options as they
    /// stand, kept by the game like a change on the page) and 'topcam save file' (written into the config file).
    /// </summary>
    internal static class MapOptions
    {
        static string _modId, _folder;
        static MapSettings _settings;
        static ModSettingsGroup _group;
        static Dictionary<string, string> _file;       // the config file's own values (for 'custom' hotkeys and margin)
        static bool _startedFree;

        /// <summary>In IMod.Initialize: reads the config file, registers the settings page (which applies the saved values),
        /// and hands the result to the map (TopCam.StartWith).</summary>
        public static void Start(ModContext context)
        {
            (_modId, _folder) = (context.Id, context.Folder);
            string text = null, from = "the built-in defaults";
            try
            {
                string path = string.IsNullOrEmpty(_folder) ? null : Path.Combine(_folder, MapPrefs.FileName);
                if (path != null && File.Exists(path))
                {
                    text = File.ReadAllText(path);
                    from = MapPrefs.FileName;
                }
            }
            catch (Exception e) { Log.Error(MapPrefs.FileName + " not read, the defaults are used: " + e.Message); }
            var values = MapPrefs.Parse(text, out var faults);
            foreach (string f in faults) Log.Error(MapPrefs.FileName + ": " + f);
            _file = new Dictionary<string, string>(values, StringComparer.OrdinalIgnoreCase);

            try
            {
                _settings = MapSettings.From(values);
                _group = context.AddSettings(_settings);
                int changed = 0;
                foreach (var setting in _group.Settings)
                {
                    string key = MapSettings.KeyOf(setting.Key);
                    if (key == null || setting.IsDefault) continue;
                    string value = _settings.Text(key);
                    if (value == null || value == "custom") continue;
                    values[key] = value;
                    changed++;
                }
                if (changed > 0) from += (from.StartsWith("the ") ? ", " : " and ") + changed + " from the settings page";
                _group.Changed += Changed;
            }
            catch (Exception e)
            {
                _group = null;
                Log.Error("the map's settings page was not added (the config file and the console still work): " + e.GetType().Name + ": " + e.Message);
            }
            _startedFree = MapPrefs.Corner(values["corner"], out int c) && c == 0;
            TopCam.StartWith(new Dictionary<string, string>(values, StringComparer.OrdinalIgnoreCase), from);
        }

        /// <summary>A row changed on the page: its topcam command, run at the next frame.</summary>
        static void Changed(ModSetting setting)
        {
            try
            {
                string key = MapSettings.KeyOf(setting.Key);
                string text = key == null ? null : _settings.Text(key);
                if (text == null) return;
                if (text == "custom") text = _file != null && _file.TryGetValue(key, out string own) ? own : "off";
                bool free = _settings.Corner == "free";
                var cmds = MapPrefs.CommandsOf(key, text, free);
                if (key == "zoom" && _settings.AutoZoom) cmds.AddRange(MapPrefs.CommandsOf("autozoom", "on", free));
                if (key == "corner" && !free)
                {
                    cmds.AddRange(MapPrefs.CommandsOf("size", _settings.Text("size"), false));
                    if (_startedFree && _file != null && _file.TryGetValue("margin", out string margin))
                    {
                        cmds.AddRange(MapPrefs.CommandsOf("margin", margin, false));
                        _startedFree = false;
                    }
                }
                TopCam.Queue(cmds);
            }
            catch (Exception e) { Log.Error("setting " + setting.Key + ": " + e.Message); }
        }

        /// <summary>'topcam save': the options as they stand become the page's values and the game keeps them (as if set on the
        /// page). 'topcam save file': they are written into the config file (the old one kept as .bak).</summary>
        public static (bool Ok, string Text) Save(IReadOnlyList<string> a)
        {
            bool file = a.Count == 2 && a[1].Equals("file", StringComparison.OrdinalIgnoreCase);
            if (a.Count > 2 || (a.Count == 2 && !file))
                return (false, "usage: topcam save [file]   (no argument: the game keeps the map's options, as on its settings page; file: into "
                               + MapPrefs.FileName + ")");
            var values = TopCam.CurrentValues();
            try
            {
                if (file) return SaveFile(values);
                if (_group == null || _settings == null) return (false, "topcam save: the settings page is not there (see the log); 'topcam save file' writes the config file");
                if (TopCam.Host == null) return (false, "topcam save: the game is not running yet");
                foreach (var (_, key) in MapSettings.Rows)
                    if (values.TryGetValue(key, out string text))
                    {
                        _settings.Set(key, text);
                        if (_settings.Text(key) == "custom" && _file != null) _file[key] = text;      // a key the page cannot show
                    }
                int kept = Keep(TopCam.Host);
                return (true, "saved: the game keeps " + kept + " of the map's options that differ from " + MapPrefs.FileName
                              + " (Settings > Mods > TimeLight Map). Margin, rect and yaw stay in the config file ('topcam save file').");
            }
            catch (Exception e)
            {
                return (false, "topcam save failed: " + e.GetType().Name + ": " + e.Message);
            }
        }

        // Never inlined: it names the game's internal UserConfig, so a refused member fails in here, inside Save's try.
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        static int Keep(GameHost host)
        {
            var config = host.Config;
            if (!config.ModSettings.TryGetValue(_modId, out var saved) || saved == null) config.ModSettings[_modId] = saved = new Dictionary<string, string>();
            foreach (var setting in _group.Settings)
            {
                if (setting.IsDefault) saved.Remove(setting.Key);
                else saved[setting.Key] = setting.Text;
            }
            int kept = saved.Count;
            if (kept == 0) config.ModSettings.Remove(_modId);
            config.Save();
            return kept;
        }

        static (bool Ok, string Text) SaveFile(Dictionary<string, string> values)
        {
            if (string.IsNullOrEmpty(_folder) || !Directory.Exists(_folder)) return (false, "topcam save file: the mod's folder is not known");
            string path = Path.Combine(_folder, MapPrefs.FileName);
            bool backedUp = File.Exists(path);
            if (backedUp) File.Copy(path, path + ".bak", true);
            string header = "# TimeLight Map options, written by 'topcam save file' (" + DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + ").\n"
                            + "# Read once when the game starts. What you change on the game's Settings > Mods page wins over this file.\n"
                            + "# The same words work in the game's console: topcam <key> <value>, e.g. topcam corner bottom-left\n";
            File.WriteAllText(path, MapPrefs.Render(values, header));
            return (true, "saved into " + path + (backedUp ? " (the old file kept as " + MapPrefs.FileName + ".bak)" : ""));
        }
    }

    /// <summary>
    /// The map's hotkeys: show / hide, the big map, zoom in, zoom out (MapPrefs.KeyActions). A prefix on GameLoop.GameKey,
    /// which the game reaches only with the keys nothing else took (the console, a window, a text field, the interface's own
    /// keys). A key matches with exactly its modifiers (Shift+N is not N). A key the game's own controls bind to an action
    /// stays the game's (the map's key does nothing then; the status says which action has it), and the keys the game keeps
    /// for itself (the camera's numpad keys, Esc, Enter, Tab, Space) cannot be bound.
    /// </summary>
    internal static class Hotkeys
    {
        public static bool On = true;
        public static long Presses;
        public static string Error;

        static readonly int N = MapPrefs.KeyActions.Length;
        static readonly bool[] Ctrl = new bool[N], Shift = new bool[N], Alt = new bool[N];
        static readonly SilkKey?[] Keys = new SilkKey?[N];
        static readonly string[] Taken = new string[N];

        /// <summary>The key of action i in its canonical words ("Shift+N", "off").</summary>
        public static string Text(int i) => MapPrefs.KeyText(Ctrl[i], Shift[i], Alt[i], Keys[i]?.ToString());

        static bool Reserved(SilkKey k) =>
            k is SilkKey.Escape or SilkKey.Enter or SilkKey.Tab or SilkKey.Space or SilkKey.Unknown
                or SilkKey.ShiftLeft or SilkKey.ShiftRight or SilkKey.ControlLeft or SilkKey.ControlRight or SilkKey.AltLeft or SilkKey.AltRight
                or SilkKey.SuperLeft or SilkKey.SuperRight or SilkKey.Delete or SilkKey.PageUp or SilkKey.PageDown or SilkKey.Home or SilkKey.F6
            || (k >= SilkKey.Keypad0 && k <= SilkKey.KeypadEqual);

        /// <summary>'topcam keys on|off' and 'topcam bind &lt;action&gt; &lt;key&gt;|off'. Null: done; else the refusal.</summary>
        public static string Command(IReadOnlyList<string> a)
        {
            if (a[0].Equals("keys", StringComparison.OrdinalIgnoreCase))
            {
                if (a.Count != 2 || !MapPrefs.OnOff(a[1], out bool on)) return "usage: topcam keys on|off   (the map's hotkeys)";
                On = on;
                if (on) Error = null;
                return null;
            }
            string usage = "usage: topcam bind <" + string.Join("|", MapPrefs.KeyActions) + "> <key>|off   (e.g. N, Shift+N, Ctrl+Equal, V)";
            if (a.Count != 3) return usage;
            int i = Array.IndexOf(MapPrefs.KeyActions, a[1].ToLowerInvariant());
            if (i < 0) return usage;
            if (!MapPrefs.KeySpec(a[2], out bool c, out bool s, out bool alt, out string name)) return "topcam bind: '" + a[2] + "' is not a key (" + usage.Substring(7) + ")";
            SilkKey? key = null;
            if (name != null)
            {
                if (name.Length == 1 && char.IsDigit(name[0])) name = "Number" + name;      // 5 is the key 5, not the 5th key
                if (!Enum.TryParse(name, true, out SilkKey k) || !Enum.IsDefined(k) || int.TryParse(name, out _))
                    return "topcam bind: no key '" + a[2] + "' (key names: letters, Number0..Number9, F1..F12, Equal, Minus, BackSlash, Apostrophe, ...)";
                if (Reserved(k)) return "topcam bind: " + k + " is the game's own (the camera's keys, Esc, Enter, Tab, Space)";
                key = k;
            }
            (Ctrl[i], Shift[i], Alt[i], Keys[i], Taken[i]) = (c, s, alt, key, null);
            return null;
        }

        /// <summary>Harmony prefix on GameLoop.GameKey(IKeyboard keyboard, Key k): false (the game skips the key) when it was
        /// one of the map's.</summary>
        public static bool GameKeyPrefix(Silk.NET.Input.IKeyboard keyboard, SilkKey k)
        {
            if (!On || keyboard == null) return true;
            try
            {
                for (int i = 0; i < N; i++)
                {
                    if (Keys[i] != k) continue;
                    bool ctrl = keyboard.IsKeyPressed(SilkKey.ControlLeft) || keyboard.IsKeyPressed(SilkKey.ControlRight);
                    bool shift = keyboard.IsKeyPressed(SilkKey.ShiftLeft) || keyboard.IsKeyPressed(SilkKey.ShiftRight);
                    bool alt = keyboard.IsKeyPressed(SilkKey.AltLeft) || keyboard.IsKeyPressed(SilkKey.AltRight);
                    if (ctrl != Ctrl[i] || shift != Shift[i] || alt != Alt[i]) continue;
                    var host = TopCam.Host;
                    if (host == null || Busy(host)) return true;
                    string bound = GameAction(host, k);
                    if (bound != null)
                    {
                        if (Taken[i] != bound)
                        {
                            Taken[i] = bound;
                            Log.Info("the map's " + MapPrefs.KeyActions[i] + " key " + Text(i) + " is bound in the game's controls (" + bound
                                     + "): the game's binding wins; pick another key ('topcam bind' or Settings > Mods)");
                        }
                        return true;
                    }
                    Taken[i] = null;
                    Presses++;
                    var r = TopCam.Command(i switch
                    {
                        0 => new[] { "toggle" },
                        1 => new[] { "big" },
                        2 => new[] { "zoom", "in" },
                        _ => new[] { "zoom", "out" },
                    });
                    if (!r.Ok) Log.Error("hotkey " + MapPrefs.KeyActions[i] + ": " + r.Text);
                    return false;
                }
            }
            catch (Exception e)
            {
                On = false;
                Error = e.GetType().Name + ": " + e.Message;
                Log.Error("the map's hotkeys switched off after an error ('topcam keys on' tries again): " + e);
            }
            return true;
        }

        /// <summary>The title screen, or a text being typed: the keys are not the map's.</summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        static bool Busy(GameHost host) => host.Player is not { } player || player.TitleOpen || player.UiEditing;

        /// <summary>The game action the key is bound to in the game's controls, or null.</summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        static string GameAction(GameHost host, SilkKey k)
        {
            if (KeyMap.Map(k) is not { } key) return null;
            var bindings = host.Session?.Bindings;
            if (bindings == null) return null;
            var binding = Binding.Of(key);
            foreach (var action in Enum.GetValues<InputAction>())
                foreach (var b in bindings.Get(action))
                    if (b == binding) return action.ToString();
            return null;
        }

        public static string Summary()
        {
            if (!On) return "off" + (Error != null ? " (switched off after an error: " + Error + ")" : "");
            var parts = new List<string>(N);
            for (int i = 0; i < N; i++)
                if (Keys[i] != null) parts.Add(Text(i) + " " + MapPrefs.KeyActions[i] + (Taken[i] != null ? " (taken by the game: " + Taken[i] + ")" : ""));
            return parts.Count == 0 ? "none bound" : string.Join(", ", parts);
        }

        public static void Status(System.Text.StringBuilder sb)
        {
            sb.Append("{\"on\":").Append(On ? "true" : "false");
            for (int i = 0; i < N; i++) sb.Append(",\"").Append(MapPrefs.KeyActions[i]).Append("\":").Append(MapMath.JsonText(Text(i)));
            sb.Append(",\"presses\":").Append(Presses);
            sb.Append(",\"taken\":{");
            bool first = true;
            for (int i = 0; i < N; i++)
            {
                if (Taken[i] == null) continue;
                if (!first) sb.Append(',');
                first = false;
                sb.Append('"').Append(MapPrefs.KeyActions[i]).Append("\":").Append(MapMath.JsonText(Taken[i]));
            }
            sb.Append("},\"error\":").Append(MapMath.JsonText(Error)).Append('}');
        }
    }

    /// <summary>The hotkeys' patch, in a class of its own: it names the game's internal GameLoop.</summary>
    internal static class HotkeyPatch
    {
        public static Action Apply(Harmony harmony) => () =>
        {
            var loop = AccessTools.TypeByName("Meitou.Game.Host.GameLoop") ?? throw new MissingMemberException("Meitou.Game.Host.GameLoop");
            var gameKey = AccessTools.Method(loop, "GameKey", new[] { typeof(Silk.NET.Input.IKeyboard), typeof(SilkKey) })
                          ?? throw new MissingMethodException(loop.FullName, "GameKey");
            harmony.Patch(gameKey, new HarmonyMethod(typeof(Hotkeys), nameof(Hotkeys.GameKeyPrefix)), null, null, null);
        };
    }
}
