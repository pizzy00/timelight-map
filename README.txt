TimeLight Map 1.0.0
===================

A live 3D map in a corner of the game window, for MeitouClient.

It is not a drawn map: it is the game world itself, rendered a second time from straight above (or tilted), inside a
rectangle in a corner of the screen. It follows your squad and shows, drawn by the game every frame:

  - your people as dots with their names (the first member in gold, the selected ones ringed, a hollow dot for
    someone out cold, a small arrow on anyone walking)
  - characters hostile to you as red dots
  - the names of the towns and places you have discovered
  - a dotted cone showing where the main camera is and which way it looks
  - a scale bar and a north arrow

You choose the corner, the size, the zoom and what it follows, in a small config file or in the game's console.


REQUIREMENTS
------------

  - MeitouClient 0.17.4 (the open-source engine that runs Kenshi's game files). The mod is built against that release
    and has only been used with it. The game refuses to load it on anything older than 0.17.4 or from 0.18.0 on.
  - It does NOT run on stock Kenshi or on RE_Kenshi. Those ignore the code; nothing happens there.
  - Developed and run on the Linux build (Vulkan). Not tried on the Windows build. Not expected to work on the macOS
    (Metal) build.


INSTALL
-------

  1. Copy the folder "TimeLightMap" from this zip into the "mods" folder of your Kenshi install, so that you have:

         <Kenshi>/mods/TimeLightMap/TimeLightMap.mod
         <Kenshi>/mods/TimeLightMap/meitou.json
         <Kenshi>/mods/TimeLightMap/Meitou.Tests.dll

  2. Start the MeitouClient launcher. "TimeLightMap" appears in the mod list, marked as a code mod. Make sure it is
     ticked (a newly found mod starts ticked). Without the launcher: add the line  TimeLightMap.mod  to
     <Kenshi>/data/mods.cfg.

  3. Play. Load a save or start a new game: the map is in the top right corner, following your squad.

  The game's log output then has the line:
         mod       timelight-map: TimeLight Map 1.0.0 ready: 7 patches, 0 failed; ...

  (optional) 4. To change the defaults for every start, copy "timelight-map.cfg.sample" from this zip into
     <Kenshi>/mods/TimeLightMap/ under the name "timelight-map.cfg" and edit it. See CONFIG FILE below.

  To remove the mod: untick it, or delete the folder. The mod keeps no data in your saves. The game itself notes
  which code mods a save was made with and tells you once when that list has changed; the save loads either way.


THE FILE IS CALLED Meitou.Tests.dll. WHY?
-----------------------------------------

The map needs parts of the engine that MeitouClient 0.17.4 keeps internal (its renderer and its HUD). The engine
lets exactly one assembly name see those parts: "Meitou.Tests", the name of its own test project. So the mod's DLL
carries that name. It contains no tests and nothing of MeitouClient's; it is this mod's own code, and the source is
in TimeLightMap-1.0.0-source.zip. What this means for you:

  - The mod is tied to MeitouClient 0.17.4. Internal parts change without notice; a new MeitouClient release may need
    a new build of this mod. The mod declares the versions it accepts (">=0.17.4 <0.18.0"); outside them the game
    leaves it out with a notice instead of crashing. On a later 0.17.x a part that no longer fits switches itself off
    and says so in the log.
  - If a later MeitouClient closes this door, the mod will need another way in.
  - Another mod that uses the same trick ships a file of the same name in its own folder. By the engine's loader each
    mod gets its own copy, so that should work, but it has not been tried.


CONFIG FILE
-----------

<Kenshi>/mods/TimeLightMap/timelight-map.cfg, read once when the game starts. Plain text, one "key = value" a line,
"#" starts a comment. Every key is optional; a key you leave out keeps its default. With no file at all you get the
defaults. A line the mod cannot read is skipped and named in the game's log.

  key      values                                              default      what it does
  -------  --------------------------------------------------  -----------  ------------------------------------------
  map      on | off                                            on           the map itself
  corner   top-left | top-right | bottom-left | bottom-right   top-right    which corner of the window
  size     small | medium | large | a number 10..60            medium       the map's height in percent of the window's
                                                                            (small 22, medium 34.6, large 46)
  margin   0..400                                              24           pixels between the map and the window's edges
  follow   squad | camera | off                                squad        squad: stays on your people
                                                                            camera: stays on the spot the main view looks at
                                                                            off: stays where it is
  zoom     10..5000                                            150          metres across the map, left to right
  pitch    35..90                                              90           90 looks straight down; lower tilts the view
                                                                            so heights show
  alpha    0..1                                                1            1 solid, 0.5 half see-through
  marks    on | off                                            on           everything drawn on the picture: dots, names,
                                                                            labels, cone, scale bar, north arrow
  cone     on | off                                            on           the main camera's dotted view cone
  places   on | off                                            on           names of discovered towns and places

Example (a small map bottom left, zoomed out, tilted):

      corner = bottom-left
      size = small
      zoom = 400
      pitch = 60


CONSOLE COMMANDS
----------------

Open the game's console with Insert (or Ctrl+`), type a command, press Enter. There is one command, "topcam".
What you set in the console lasts until you quit; put it in the config file to keep it.

Everyday:

  topcam on                         show the map
  topcam off                        hide it
  topcam corner bottom-left         move it: top-left | top-right | bottom-left | bottom-right
  topcam corner top-left large      corner and size in one go
  topcam size small                 small | medium | large, or a number: the height in percent of the window (10..60)
  topcam size 25
  topcam margin 40                  pixels from the window's edges (0..400)
  topcam zoom 150                   metres across the map (10..5000); try 60 for a fight, 600 for travel
  topcam follow squad               stay on your people
  topcam follow camera              stay on the spot the main view looks at
  topcam follow off                 stay where it is
  topcam marks off                  the bare picture, nothing drawn on it;  topcam marks on  brings it back
  topcam cone off                   no camera cone;  topcam cone on
  topcam places off                 no town and place names;  topcam places on
  topcam hostiles off               no red dots for hostile characters;  topcam hostiles on
  topcam pitch 60                   tilt the view (35..90; 90 is straight down);  topcam pitch 60 --over 2  glides there in 2 s
  topcam alpha 0.6                  see-through (0..1);  topcam alpha 0 --over 1  fades it out in 1 s
  topcam                            the full state as one line of JSON (also: topcam status)

Each command answers with one line saying where the map stands, for example:
  map on, bottom-left, size 22% of the window's height, margin 24, 150 m across, follows the squad, marks on, cone on, places on
A command the mod does not understand answers with its usage.

Single kinds of mark (all on at first):

  topcam layer names off            the layers: names | foes | towns | cone | arrows | scale | north, or all,
  topcam layer arrows off scale off     each followed by on or off; several in one line
  topcam layer all on
                                    names: your people's names        foes: the red dots of hostile characters
                                    towns: town and place names       cone: the camera's view cone
                                    arrows: the arrows on walkers     scale: the scale bar      north: the N arrow
  topcam textscale 1.3              bigger names and labels (0.5..3)
  topcam cone reach 2 fill 0.25     how long the cone is (0.5..8) and how strong its inside is (0..1)

The view, by hand (positions are world units: 10 units = 1 m, +X is east, +Z is south; "topcam" alone prints the map's
centre under "now"):

  topcam yaw 90                     turn the map (0: north up; 90: east up);  topcam yaw 90 --over 2  glides there in 2 s
  topcam view 43000 -18000 3000     a fixed view: centre x z and the width across, in world units
  topcam view follow                the same as  topcam follow camera;  topcam view follow 3000  also sets the width
  topcam ease 0.5                   how softly the view follows the squad and turns (seconds, 0..10)
  topcam rect 100 100 640 448       an exact rectangle in window pixels (x y w h) instead of a corner

The picture:

  topcam fps 30                     how often a still map is redrawn (a moving map is redrawn every frame)
  topcam scale 0.75                 render the picture smaller than its rectangle (0.1..4): faster, softer
  topcam layers toc                 what is rendered: t terrain, o objects (buildings), f foliage, c characters
  topcam shadows on                 shadows in the map (off by default)
  topcam haze on                    the game's distance haze and weather fog in the map (off by default)
  topcam fog on                     the game's distance fog in the map (off by default)
  topcam wide off                   the game's own draw distances instead of the ones that suit a zoomed-out map
  topcam exposure 1.2               a fixed brightness;  topcam exposure auto  follows the game
  topcam flip x                     mirror the picture: none | x | y | xy
  topcam clip off                   let the game's name tags and speech bubbles draw over the map (kept off it by default)


KNOWN LIMITS
------------

  - The map is a second rendering of the scene. It costs frame time, more with a larger map and a wider zoom. If your
    frame rate drops: topcam size small, topcam scale 0.5, topcam fps 15, topcam layers to, or topcam off.
  - It shows what the game has loaded. Buildings, plants and characters exist only near where the main camera looks,
    so a map far from the main view (follow off, or the camera far from the squad) shows bare ground there.
  - Wider than 900 m across, characters are left out of the picture (they would be smaller than a pixel). The dots stay.
  - Red dots are the characters the game itself counts as hostile to you or fighting you, awake and alive, whether or
    not your people can see them. Turn them off with  topcam hostiles off  if you find that unfair.
  - Town and place names come from the game's own map knowledge: a place appears once discovered, under its
    "unexplored" name until you have visited it. Nests and camps are not named. Zoomed out beyond 2 km, only towns.
  - At most 32 squad members are drawn, 40 hostiles and 48 place labels. A label with no free room is not drawn (its
    diamond stays).
  - The names and labels are part of the game's HUD, so they may disappear with it when you hide the interface; the
    picture, the dots and the cone do not.
  - Console settings are not saved. Only the config file is read at start.
  - If something goes wrong while drawing, the affected part switches itself off and writes one line to the game's log
    ("mod       timelight-map: ..."); the game goes on.  topcam on  /  topcam marks on  try again.
  - Built for one MeitouClient release (see above).


LICENCE
-------

TimeLight Map is free software: you can redistribute it and/or modify it under the terms of the GNU General Public
License as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later
version. It is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without even the implied
warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. The full text is in the file LICENSE.

The complete source code is in TimeLightMap-1.0.0-source.zip, published together with this zip.

The mod is built against MeitouClient 0.17.4, which is licensed GPL-3.0-or-later, and patches it at run time with
Harmony, which is licensed MIT. Both come with the game; neither is included here. No game data or assets are included.


CREDITS
-------

Built by the Beep Is Too Strong team.
Harmony (MIT) by Andreas Pardeike and contributors, as shipped with MeitouClient.
