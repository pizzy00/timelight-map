# Changelog

All notable changes to TimeLight Map. The player's manual is [README.txt](README.txt).

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
