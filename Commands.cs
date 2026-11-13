// TimeLight Map: an in-game 3D picture-in-picture map for MeitouClient.
// Copyright (C) 2026 TimeLight Map contributors
// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Meitou.Game.DevTools;

namespace TimeLightMap
{
    /// <summary>
    /// The mod's one console command, 'topcam'. DevConsole.Register(ConsoleCommand) is public and replaces by name, so the
    /// mod registers the command on each console the first time that console runs a line (a prefix on DevConsole.Execute).
    /// A command that changes something answers with one line saying where the map stands; 'topcam' alone (or
    /// 'topcam status') answers with the whole state as one line of JSON.
    /// </summary>
    internal static class Commands
    {
        static readonly ConditionalWeakTable<DevConsole, object> Seen = new ConditionalWeakTable<DevConsole, object>();
        static readonly object Registered = new object();

        // Harmony prefix on DevConsole.Execute(string line)
        public static void ExecutePrefix(DevConsole __instance)
        {
            try
            {
                if (__instance == null || Seen.TryGetValue(__instance, out _)) return;
                Seen.AddOrUpdate(__instance, Registered);
                Register(__instance);
            }
            catch (Exception e) { Log.Error("the topcam command was not added: " + e.Message); }
        }

        static void Register(DevConsole con)
        {
            con.Register(new ConsoleCommand("topcam",
                TopCam.Usage.Substring("usage: ".Length),
                "TimeLight Map: a live 3D view of the world from above, drawn in a corner of the game window. "
                + "on|off; corner top-left|top-right|bottom-left|bottom-right; size small|medium|large|<10..60>; zoom <metres across>; "
                + "follow squad|camera|off; marks on|off (dots, names, labels, scale, north); cone on|off (where the main camera looks); "
                + "places on|off (discovered towns); layer <name> on|off (one kind of mark); pitch <35..90> (tilt); alpha <0..1> (strength); "
                + "also: yaw, view, rect, ease and the picture settings (world units, 10 = 1 m; north up at yaw 0). "
                + "No argument: the state as JSON",
                (args, c) => Reply(c, Run(args))));
        }

        static void Reply(DevConsole con, (bool Ok, string Text) r)
        {
            if (r.Ok) con.Print(r.Text, DevConsole.Kind.Info);
            else con.Error(r.Text);
        }

        /// <summary>The arguments after "topcam". None, or "status": the state as JSON; else the command's own answer
        /// (a refusal with its usage, or one line saying where the map stands now).</summary>
        public static (bool Ok, string Text) Run(IReadOnlyList<string> args)
        {
            try
            {
                if (args.Count == 0 || (args.Count == 1 && args[0].Equals("status", StringComparison.OrdinalIgnoreCase)))
                    return (true, TopCam.Status());
                var r = TopCam.Command(args);
                return r.Ok ? (true, TopCam.Summary()) : r;
            }
            catch (Exception e)
            {
                return (false, "topcam failed: " + e.Message);
            }
        }
    }
}
