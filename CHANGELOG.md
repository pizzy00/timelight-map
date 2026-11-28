# Changelog

All notable changes to TimeLight Map. The player's manual is [README.txt](README.txt).

## 1.5.0 (2026-11-28)

Built against MeitouClient 0.24.0; accepts 0.24.x (verified against 0.24.0 and 0.24.1).

### New

- **Moving traders (`traders` layer).** Roaming trade caravans (Hive caravans, Traders Guild caravans, nomad animal
  traders, and modded faction caravans) now appear on the map as bright emerald-green ringed diamonds with their names
  in matching green pills and a heading arrow while walking. Stationary town shopkeepers (town-owned squads, building
  shops, and assigned homes) and hostile faction caravans are excluded so towns stay clean and enemies never show as
  friendly traders. Distant roaming trade caravans also appear when zoomed out. Toggle on **Settings > Mods >
  TimeLight Map** (`Moving traders`), in `timelight-map.cfg` (`traders = on | off`, aliases `caravans`, `merchants`),
  or in the console (`topcam traders on | off`, `topcam layer traders on | off`).
- **Custom waypoint routes (`topcam waypoints`).** Draw a multi-point dotted route with waypoint dots, destination ring,
  and optional escaped label via `topcam waypoints <x> <z> [<x> <z> ...] [--name <text>]` (clear with `topcam waypoints
  clear`), controlled by the `route` layer, with off-screen segment culling so long routes stay within the shape budget.

### Improved

- **Circular-chord wing tucking on round maps.** For large squads (10 to 16+ members) on a round map, top and bottom
  wing rows gently tuck inward along the circle's chord so wide names near the curved rim stay inside the map without
  getting clipped or overlapping squad dots.
- **Round-map GPU shader early-out.** The marks fragment shader now discards pixels outside the circle before running
  the shape loop, skipping ~21.5% of fragment work on round maps (including the big round map).
- **Active-fighter hostile priority.** Hostiles actively fighting your squad are now placed before distant ambient
  hostiles (and hostile cap raised from 40 to 48) with non-finite coordinate guards, so enemies in your fight are never
  crowded out by distant bandits.
- **Complete `topcam reset` & summary range.** `topcam reset` now clears transient layer errors and heading trackers;
  auto-zoom status summary clamps its displayed range when `minzoom` exceeds `zoom`.

## 1.4.0 (2026-11-24)

Built against MeitouClient 0.22.0; accepts 0.22.x (verified against 0.22.0 and 0.22.1).

### New

- **Group auto-zoom.** When following your squad or selected people, the map automatically eases in closer when your
  group is huddled together and zooms smoothly back out as they spread out, between `minzoom` (default `50` m) and
  `zoom` (default `150` m). Toggle on the Settings > Mods page (`Auto zoom` and `Close zoom`), in the config file
  (`autozoom = on | off`, `minzoom = 50`), or in the console (`topcam autozoom on | off`, `topcam minzoom <metres>`,
  `topcam zoom auto`). Using `topcam zoom <metres>` or the `=` / `-` hotkeys switches to manual zoom; `topcam zoom auto`
  turns auto-zoom back on.

### Improved

- **Cleaner, non-overlapping squad and town labels.** Crowded squad names now split into balanced left and right wings
  around the group instead of stacking into one tall column on one side, keeping names inside the map (including round
  maps) and clear of the north arrow and scale bar.
- **Leader-line avoidance for town names.** Place and town labels check against both squad label boxes and their leader
  lines so callout lines no longer slash across nearby town names.
- **Better text readability.** Darker backing pills behind names and place labels, new subtle backing pills behind the
  scale bar and north arrow, softer thinner leader lines, and centered label text alignment.

### Fixed

- **Window clip and round-map aspect.** The map rectangle is now clipped to the output target before computing render
  size and aspect, preventing squashed renders or stale hit rects when the map is hidden, minimized, or off-screen.
- **Stale shown-frame stamp on early returns.** Returning early (map off, loading/menu frames, open render pass, zero
  window size) now clears the shown stamp so HUD clipping and overlays do not stick on a hidden map.
- **`topcam reset` completeness.** Now restores `big`, `ease`, `flip`, `shadows`, `fog`, `haze`, `wide`, `exposure`,
  `clip`, and mark defaults (including cone reach/fill) in addition to the startup config keys.
- **Settings page corner & custom-key handling.** Switching from `corner = free` to a corner now applies the page's size
  and config margin cleanly; custom hotkeys read from the config file rather than overwritten runtime values; `topcam save
  file` only claims a `.bak` was written when an existing config file was actually backed up.
- **Sky eye position restore.** Restores `Sky.EyePosition` alongside the haze/fog fields after the top-down pass so the
  main view's atmosphere never sees the map camera's eye.
- **Non-finite camera guard and status JSON.** Guards against non-finite camera target coordinates and formats `cx`, `cz`,
  and `span` through `MapMath.Num` so `topcam status` never emits invalid JSON.
- **Marks re-enable & alpha parser cleanup.** Re-enabling marks via any accepted truthy word (`true`, `yes`, `1`) clears
  prior shader error flags; removed duplicate `alpha` parse.

## 1.3.2 (2026-11-19)

Built against MeitouClient 0.22.0; accepts 0.22.x. The map itself is unchanged: every game method it hooks is
there in 0.22.0 with the same signature. On MeitouClient 0.21.x keep using 1.3.1, on 0.20.x 1.3.0.

## 1.3.1 (2026-11-17)

Built against MeitouClient 0.21.0; accepts 0.21.x. The map itself is unchanged: every game method it hooks is
there in 0.21.0 with the same signature. On MeitouClient 0.20.x keep using 1.3.0.

## 1.3.0 (2026-11-16)

Built against MeitouClient 0.20.0; accepts 0.20.x (also loads on the Windows build of 0.20.1).

### New

- **Settings page.** Settings > Mods > TimeLight Map: map on/off, corner, size, shape, frame, follow, zoom, tilt,
  strength, quality, each kind of mark, text size, hide with the interface and the four hotkeys. Changes show at once;
  the game keeps them and they win over the config file. Reset on the page goes back to the config file's value.
- **Hotkeys.** N shows / hides the map, Shift+N the big map, `=` zooms in, `-` zooms out. Rebind them on the settings
  page, in the config file (`key_toggle`, `key_big`, `key_zoomin`, `key_zoomout`, `keys`) or with `topcam bind`.
  They fire only in the game world (not while typing, not on the title screen), need the exact Shift/Ctrl/Alt, and a
  key bound in the game's own controls wins (logged once).
- **Big map.** `topcam big`: the map in the middle of the window, larger and three times wider; again to go back.
- **Round map** (`shape = round`) with a soft edge; names and labels stay inside the circle, the scale bar and the
  north arrow move to fit it. In a corner it keeps the same margin from both edges; in a free `rect` that is not
  square it takes the shorter side and keeps to the side nearer the window's edge.
- **Frame** (`frame = on`): a thin two-tone frame round the map, square or round.
- **Routes.** A dotted line along the path of each selected person to a ring at its end; the part the game has not
  worked out yet is fainter. Layer `route`, key `routes`.
- **Hurt rings.** An orange ring on anyone below 60 % (the worse of blood and the most hurt body part), red below 30 %.
  Layer `hurt`, key `hurt`.
- **Other squads.** People in your other squads are drawn as smaller grey-blue dots without names.
- **Follow selected** (`follow = selected`): the map stays on the people you select, on the squad while nobody is.
- **Quality presets** (`quality = low | medium | high`) and `topcam movefps` (how often a moving map is redrawn).
- **Zoom steps:** `topcam zoom in` / `zoom out` (1.5 times a step).
- **`topcam toggle`**, **`topcam save`** (the game keeps the options, as on the settings page), **`topcam save file`**
  (writes `timelight-map.cfg` from what you see, the old one kept as `.bak`), **`topcam reset`** (back to the options
  the game started with).
- **`corner = free`** with a **`rect`** key: an exact rectangle from the config file; `topcam corner free` keeps the
  map where it is.
- New config keys: `rect`, `shape`, `frame`, `yaw`, `quality`, `names`, `hostiles`, `arrows`, `routes`, `hurt`,
  `scale`, `north`, `textscale`, `keys`, `key_toggle`, `key_big`, `key_zoomin`, `key_zoomout`. Every 1.2.0 key still
  works. The sample file lists all of them.
- `topcam status` now also reports the options in config-file words, the shape, frame, big map, follow mode, the
  people count, the rectangle shown, the quality, the hotkeys and the presses.

### Fixed

- **B1 Follow squad could follow the wrong squad.** The map took every player character and anchored on the first one
  in the list. It now follows the active squad (leader first); your other squads are still drawn, not followed.
- **B2 Loose on/off words.** `shadows`, `fog`, `haze`, `wide`, `clip` and `hostiles` read any word other than `on` as
  off, `layers` took any letters and `flip` took anything. Only on/off (or true/false, yes/no, 1/0), the letters
  t o f c and none/x/y/xy are taken now; anything else answers with the usage and changes nothing.
- **B3 Two switches for one mark.** `hostiles` and `places` had their own flags beside the `foes` and `towns` layers,
  so one could be on while the other said off. They are now the same switch (as `cone` already was).
- **B4 Build not reproducible from a git clone.** SourceLink and the source-control queries wrote the commit into the
  DLL, and Windows line endings changed the bytes. Both are off now (and `.gitattributes` keeps LF), so a build from a
  clone and one from the source zip give the same DLL.
- **B5 Status JSON.** The error text was escaped by hand, and `yawTarget` / `pitchTarget` appeared twice. Texts go
  through the JSON escaper now and each key appears once (under `view`).
- **B6 Allocations while drawing.** The place-label layout made two arrays on every call and a new comparer each
  frame; both are made once now.
- **B7 A free map could not be placed from the config file.** `corner = off` had no rectangle key. Now `corner = free`
  (the old `off` still works) takes the new `rect` key; a rect must be at least 16 x 16 pixels.
- **B8 The checks were not in the repository.** The console checks of the pure code (1199 of them: corners, sizes,
  the config file, the layers, the label and place layouts, routes, the round map, the hotkey words) are in
  `checks/` now; run `dotnet run -c Release` in that folder (exit code 0 = every check passed; no game needed).
- **B9 Small things.** A stale comment (KnownPlaces pointed at a member that no longer exists); `alpha` was parsed
  twice.

## 1.2.0 (2026-11-14)

Built for MeitouClient 0.20.0 (accepts 0.20.x). The map hides when you hide the game's interface (F7 / `hud hide`)
and comes back with it; `topcam hidewithui off` (or `hidewithui = off`) keeps it on screen.

## 1.1.0 (2026-11-14)

Built for MeitouClient 0.19.0 (accepts 0.19.x). The map itself is unchanged.

## 1.0.0 (2026-11-13)

First version, built for MeitouClient 0.17.4 (accepts 0.17.4 and later 0.17.x).
