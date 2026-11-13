// TimeLight Map: an in-game 3D picture-in-picture map for MeitouClient.
// Copyright (C) 2026 TimeLight Map contributors
// SPDX-License-Identifier: GPL-3.0-or-later
//
// This program is free software: you can redistribute it and/or modify it under the terms of the GNU General Public
// License as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later
// version. It is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without even the implied
// warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU General Public License (the LICENSE file)
// for more details.
//
// A code mod for MeitouClient 0.17.4. It draws a live 3D view of the world from above in a corner of the game window
// (TopCam.cs), with marks on it (Marks.cs): the squad's dots and names, hostile characters, discovered towns and places,
// the main camera's view cone, a scale bar and a north arrow. One console command, 'topcam', and one optional config
// file, timelight-map.cfg in the mod's folder (MapPrefs.cs). The view arithmetic and the label layout are in MapMath.cs.
using System;
using HarmonyLib;
using Meitou.Game.DevTools;
using Meitou.Modding;

namespace TimeLightMap
{
    /// <summary>The "entry" type of meitou.json. The game creates it with the public parameterless constructor and calls
    /// Initialize once, before the world runs.</summary>
    public sealed class MapEntry : IMod
    {
        public void Initialize(ModContext context)
        {
            Log.Bind(context.Id);
            try { LoadMapPrefs(context.Folder); } catch (Exception e) { Log.Error(MapPrefs.FileName + " not read: " + e.Message); }
            Patches.Apply(context.Harmony, context.Id);
        }

        // Never inlined: TopCam names internal game types, so a refused type would fail in here, inside the caller's try.
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        static void LoadMapPrefs(string folder) => TopCam.LoadPrefs(folder);
    }

    internal static class Log
    {
        static string _prefix = "mod       timelight-map: ";

        public static void Bind(string id) => _prefix = "mod       " + id + ": ";   // the game's own ModContext.Log format

        public static void Info(string text)
        {
            try { Console.WriteLine(_prefix + text); } catch { }
        }

        public static void Error(string text)
        {
            try { Console.Error.WriteLine(_prefix + text); } catch { }
        }
    }

    /// <summary>The map's two render patches, in a class of their own: they name internal game types (GameHost), so if the
    /// runtime refused the friend access, the failure would stay inside this method's caller (Patches.Try).</summary>
    internal static class TopCamPatches
    {
        public static Action Apply(Harmony harmony) => () =>
        {
            var terrainDraw = AccessTools.Method(typeof(Meitou.Game.Rendering.Terrain.TerrainRenderer), "Draw")
                              ?? throw new MissingMethodException("TerrainRenderer", "Draw");
            var hostType = AccessTools.TypeByName("Meitou.Game.GameHost") ?? throw new MissingMemberException("Meitou.Game.GameHost");
            var drawFrame = AccessTools.Method(hostType, "DrawFrame", new[] { typeof(int), typeof(int) })
                            ?? throw new MissingMethodException(hostType.FullName, "DrawFrame");
            harmony.Patch(terrainDraw, new HarmonyMethod(typeof(TopCam), nameof(TopCam.TerrainDrawPrefix)), null, null, null);
            harmony.Patch(drawFrame, null, new HarmonyMethod(typeof(TopCam), nameof(TopCam.DrawFramePostfix)), null, null);
        };
    }

    internal static class Patches
    {
        public static void Apply(Harmony harmony, string id)
        {
            // A second Initialize means a fresh copy of this assembly was loaded: drop the old copy's patches.
            // (Never UnpatchAll(null): that would remove every mod's patches.)
            if (!string.IsNullOrEmpty(id))
            {
                try { harmony.UnpatchAll(id); } catch (Exception e) { Log.Error("unpatch: " + e.Message); }
            }

            int ok = 0, failed = 0;
            void Try(string what, Action patch)
            {
                try { patch(); ok++; }
                catch (Exception e)
                {
                    failed++;
                    Log.Error("patch " + what + " failed, that part is off: " + e.GetType().Name + ": " + e.Message);
                }
            }

            // The 'topcam' console command, registered on every DevConsole the first time it runs a line.
            Try("DevConsole.Execute (the topcam command)", () => harmony.Patch(
                AccessTools.Method(typeof(DevConsole), nameof(DevConsole.Execute), new[] { typeof(string) }),
                new HarmonyMethod(typeof(Commands), nameof(Commands.ExecutePrefix)), null, null, null));

            // The picture: the frame's light from the main terrain draw, the drawing after the world frame.
            Try("TerrainRenderer.Draw + GameHost.DrawFrame (the map picture)", TopCamPatches.Apply(harmony));

            // The map's texts (names, places, scale, N) are HUD text boxes, added where the game adds its speech bubbles;
            // the same postfix keeps the bubbles themselves off the map rectangle.
            Try("SpeechBubbles.AddSpeech (the map's texts)", () =>
            {
                var t = AccessTools.TypeByName("Meitou.Game.Ui.SpeechBubbles")
                        ?? throw new MissingMemberException("Meitou.Game.Ui.SpeechBubbles");
                var m = AccessTools.Method(t, "AddSpeech") ?? throw new MissingMethodException(t.FullName, "AddSpeech");
                harmony.Patch(m, new HarmonyMethod(typeof(MapHud), nameof(MapHud.CountPrefix)),
                    new HarmonyMethod(typeof(MapHud), nameof(MapHud.SpeechPostfix)), null, null);
            });

            // The game's own world-anchored HUD pieces stay off the map rectangle (name tags, task bars, item labels, damage numbers).
            foreach (var (typeName, method) in new[]
                     {
                         ("Meitou.Game.Ui.StealthView", "AddNameTags"), ("Meitou.Game.Ui.StealthView", "AddTaskBars"),
                         ("Meitou.Game.Ui.GroundItemPicks", "AddItemLabels"), ("Meitou.Game.Ui.DamageFloaters", "AddFloaters"),
                     })
            {
                Try(typeName + "." + method + " (kept off the map)", () =>
                {
                    var t = AccessTools.TypeByName(typeName) ?? throw new MissingMemberException(typeName);
                    var m = AccessTools.Method(t, method) ?? throw new MissingMethodException(typeName, method);
                    harmony.Patch(m, new HarmonyMethod(typeof(MapHud), nameof(MapHud.CountPrefix)),
                        new HarmonyMethod(typeof(MapHud), nameof(MapHud.ClipPostfix)), null, null);
                });
            }

            Log.Info("TimeLight Map " + TopCam.Version + " ready: " + ok + " patches, " + failed + " failed; console command: topcam; options: "
                     + MapPrefs.FileName + " in the mod's folder");
        }
    }
}
