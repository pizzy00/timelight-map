TimeLight Map 1.5.0
===================

A live 3D map in a corner of the game window, for MeitouClient.

It is not a drawn map: it is the game world itself, rendered a second time from straight above (or tilted), inside a
rectangle in a corner of the screen (or a circle). It follows your squad and shows, drawn by the game every frame:

  - your people as dots: your active squad with names (its leader in gold, the selected ones ringed, a hollow dot
    for someone out cold, a small arrow on anyone walking), your other squads as smaller grey-blue dots
  - where your selected people are walking: a dotted line to the end of their route
  - an orange ring on anyone hurt, a red one on anyone badly hurt
  - characters hostile to you as red dots
  - moving trade caravans as green ringed diamonds with their names and walk arrows
  - the names of the towns and places you have discovered
  - a dotted cone showing where the main camera is and which way it looks
  - a scale bar and a north arrow

You set it up on the game's settings page (Settings > Mods > TimeLight Map), with four hotkeys while you play,
in a small config file, or in the game's console.


REQUIREMENTS
------------

  - MeitouClient 0.24.x (the open-source engine that runs Kenshi's game files). The mod is built against 0.24.0
    (verified on 0.24.0 and 0.24.1). The game refuses to load it on anything older than 0.24.0 or from 0.25.0 on.
    On MeitouClient 0.22.x use TimeLight Map 1.4.0; on 0.21.x use 1.3.1; on 0.20.x use 1.3.0; on 0.19.x use 1.1.0; on
    0.17.x use 1.0.0.
  - It does NOT run on stock Kenshi or on RE_Kenshi. Those ignore the code; nothing happens there.
  - Developed and played on the Linux build (Vulkan). 1.3.0 also loaded on the Windows build of 0.20.1. Not expected
    to work on the macOS (Metal) build.


INSTALL
-------

  1. Copy the folder "TimeLightMap" from this zip into the "mods" folder of your Kenshi install, so that you have:

         <Kenshi>/mods/TimeLightMap/TimeLightMap.mod
         <Kenshi>/mods/TimeLightMap/meitou.json
         <Kenshi>/mods/TimeLightMap/Meitou.Tests.dll

     Updating from an older version: replace those three files. Your timelight-map.cfg, if you made one, stays.

  2. Start the MeitouClient launcher. "TimeLightMap" appears in the mod list, marked as a code mod. Make sure it is
     ticked (a newly found mod starts ticked). Without the launcher: add the line  TimeLightMap.mod  to
     <Kenshi>/data/mods.cfg.

  3. Play. Load a save or start a new game: the map is in the top right corner, following your squad.
     N shows and hides it, Shift+N makes it big, = and - zoom.

  The game's log output then has the line:
         mod       timelight-map: TimeLight Map 1.5.0 ready: 8 patches, 0 failed; ...

  To remove the mod: untick it, or delete the folder. The mod keeps no data in your saves. The game itself notes
  which code mods a save was made with and tells you once when that list has changed; the save loads either way.


THE FILE IS CALLED Meitou.Tests.dll. WHY?
-----------------------------------------

The map needs parts of the engine that MeitouClient 0.24 keeps internal (its renderer and its HUD). The engine lets
exactly one assembly name see those parts: "Meitou.Tests", the name of its own test project. So the mod's DLL carries
that name. It contains no tests and nothing of MeitouClient's; it is this mod's own code, and the source is in
TimeLightMap-1.5.0-source.zip. What this means for you:

  - The mod is tied to MeitouClient 0.24.x. Internal parts change without notice; a new MeitouClient release may need
    a new build of this mod. The mod declares the versions it accepts (">=0.24.0 <0.25.0"); outside them the game
    leaves it out with a notice instead of crashing. On a later 0.24.x a part that no longer fits switches itself off
    and says so in the log.
  - If a later MeitouClient closes this door, the mod will need another way in.
  - Another mod that uses the same trick ships a file of the same name in its own folder. By the engine's loader each
    mod gets its own copy, so that should work, but it has not been tried.


HOTKEYS
-------

  N          show / hide the map
  Shift+N    the big map: in the middle of the window, larger and three times wider; again to go back
  =          zoom in (closer, a step of 1.5 times)
  -          zoom out

The keys work in the game world when nothing else wants them: not while you type in a text box or the console, and
not on the title screen. Shift, Ctrl and Alt must match exactly (N alone does not fire on Shift+N). A key you bind in
the game's own controls wins: the map then leaves it alone and says so once in the game's log. Change them on the
settings page, in the config file (key_toggle and so on), or in the console:  topcam bind big Shift+M,
topcam bind zoomin off,  topcam keys off  (all four off). The game's own keys cannot be taken: Esc, Enter, Tab,
Space, Delete, Page Up/Down, Home, F6 and the keypad.


THE SETTINGS PAGE
-----------------

Settings > Mods > TimeLight Map has the everyday options: map on/off, corner, size, shape, frame, follow, zoom,
auto zoom, close zoom, tilt, strength, quality, each kind of mark, text size, hide with the interface, and the four
hotkeys. A change shows at once. The game keeps what you change there (in its meitou.user.json) and it wins over the
config file. "Reset" on the page goes back to the config file's value (or the built-in default when there is no file).


CONFIG FILE
-----------

<Kenshi>/mods/TimeLightMap/timelight-map.cfg, read once when the game starts. Plain text, one "key = value" a line,
"#" starts a comment. Every key is optional; a key you leave out keeps its default. With no file at all you get the
defaults. A line the mod cannot read is skipped and named in the game's log. timelight-map.cfg.sample in this zip
lists every key with its default; copy it under the name timelight-map.cfg and edit it. Or set the map up in the
game and type  topcam save file  to write the file from what you see.

  key          values                                            default      what it does
  -----------  ------------------------------------------------  -----------  -----------------------------------------
  map          on | off                                          on           the map itself
  corner       top-left | top-right | bottom-left |              top-right    which corner of the window; free: at 'rect'
               bottom-right | free
  size         small | medium | large | a number 10..60          medium       the map's height in percent of the window's
                                                                              (small 22, medium 34.6, large 46)
  margin       0..400                                            24           pixels between the map and the window's edges
  rect         x y w h (window pixels)                           24 24 480 336  the map's rectangle, used with corner = free
                                                                              (round: a circle as high as the shorter side,
                                                                              kept to the side nearer the window's edge)
  shape        square | round                                    square       round: a circle, the game shows round it
  frame        on | off                                          off          a thin two-tone frame round the map
  follow       squad | selected | camera | off                   squad        squad: your active squad
                                                                              selected: the people you select (the squad
                                                                              when nobody is selected)
                                                                              camera: the spot the main view looks at
                                                                              off: stays where it is
  zoom         10..5000                                          150          metres across the map, left to right (widest
                                                                              zoom when autozoom is on)
  minzoom      10..5000                                          50           closest metres across when your followed
                                                                              group is huddled together (with autozoom on)
  autozoom     on | off                                          on           zooms in closer when your group is close
                                                                              together, out as they spread
  pitch        35..90                                            90           90 looks straight down; lower tilts the view
                                                                              so heights show
  yaw          0..360                                            0            the map's turn: 0 north up, 90 east up
  alpha        0..1                                              1            1 solid, 0.5 half see-through
  quality      low | medium | high                               high         how sharp and how often the picture is drawn
                                                                              (see "The picture" below)
  marks        on | off                                          on           everything drawn on the picture
  names        on | off                                          on           your people's names
  hostiles     on | off                                          on           red dots on characters hostile to you
  traders      on | off                                          on           green markers and names on moving trade
                                                                              caravans (stationary shopkeepers excluded)
  places       on | off                                          on           names of discovered towns and places
  cone         on | off                                          on           the main camera's dotted view cone
  arrows       on | off                                          on           small arrows on your people while they walk
  routes       on | off                                          on           where your selected people are walking to
  hurt         on | off                                          on           orange / red rings on hurt people
  scale        on | off                                          on           the scale bar
  north        on | off                                          on           the north arrow
  textscale    0.5..3                                            1            the size of names and labels
  hidewithui   on | off                                          on           on: the map hides while the game's interface
                                                                              is hidden (F7, or hud hide in the console)
                                                                              off: the map stays on screen
  keys         on | off                                          on           the four hotkeys
  key_toggle   a key, e.g. N, Shift+N, Ctrl+M, F8, Equal, off    N            show / hide
  key_big      a key                                             Shift+N      the big map
  key_zoomin   a key                                             Equal        zoom in (Equal is the = key)
  key_zoomout  a key                                             Minus        zoom out

On/off keys also take true/false, yes/no and 1/0. The words of 1.2.0 still work. Example (a small round map
bottom left, zoomed out, tilted, with a frame):

      corner = bottom-left
      size = small
      shape = round
      frame = on
      zoom = 400
      pitch = 60


CONSOLE COMMANDS
----------------

Open the game's console with Insert (or Ctrl+`), type a command, press Enter. There is one command, "topcam".
What you set in the console lasts until you quit, unless you save it:  topcam save  (the game keeps it, as on the
settings page) or  topcam save file  (into timelight-map.cfg; the old file is kept as timelight-map.cfg.bak).
topcam reset  goes back to the options the game started with.

Everyday:

  topcam on                         show the map;  topcam off  hides it;  topcam toggle  switches
  topcam big                        the big map in the middle of the window, and back;  topcam big on | off
  topcam corner bottom-left         move it: top-left | top-right | bottom-left | bottom-right
  topcam corner top-left large      corner and size in one go
  topcam corner free                keep the map where it is now and let  topcam rect  place it
  topcam size small                 small | medium | large, or a number: the height in percent of the window (10..60)
  topcam size 25
  topcam margin 40                  pixels from the window's edges (0..400)
  topcam shape round                a round map;  topcam shape square
  topcam frame on                   a thin frame round the map;  topcam frame off
  topcam zoom 150                   metres across the map (10..5000); try 60 for a fight, 600 for travel
  topcam zoom in                    a step closer (1.5 times);  topcam zoom out;  topcam zoom auto  (turns autozoom on)
  topcam autozoom on                zoom in closer when your group is huddled together;  topcam autozoom off
  topcam minzoom 50                 closest metres across for auto-zoom (10..5000)
  topcam follow squad               stay on your active squad
  topcam follow selected            stay on the people you select (on the squad while nobody is selected)
  topcam follow camera              stay on the spot the main view looks at
  topcam follow off                 stay where it is
  topcam quality medium             low | medium | high (see "The picture")
  topcam marks off                  the bare picture, nothing drawn on it;  topcam marks on  brings it back
  topcam pitch 60                   tilt the view (35..90; 90 is straight down);  topcam pitch 60 --over 2  glides there in 2 s
  topcam alpha 0.6                  see-through (0..1);  topcam alpha 0 --over 1  fades it out in 1 s
  topcam hidewithui off             keep the map on screen when you hide the interface (F7);  topcam hidewithui on
                                    (the default) hides it along with the interface
  topcam keys off                   no hotkeys;  topcam bind toggle M  puts show / hide on M (see HOTKEYS)
  topcam save                       keep what you set (the game keeps it);  topcam save file  writes timelight-map.cfg
  topcam reset                      back to the options the game started with
  topcam                            the full state as one line of JSON (also: topcam status)

Each command answers with one line saying where the map stands, for example:
  map on, bottom-left, size 22% of the window's height, margin 24, 150 m across, follows the squad, marks on
A command the mod does not understand answers with its usage.

Single kinds of mark (all on at first):

  topcam layer names off            the layers: names | foes | towns | cone | arrows | route | hurt | scale | north | traders,
  topcam layer arrows off scale off     or all, each followed by on or off; several in one line
  topcam layer all on               (hostiles, places, routes and caravans work as names too)
                                    names: your squad's names         foes: the red dots of hostile characters
                                    traders: green moving caravans    towns: town and place names
                                    cone: the camera's view cone      arrows: the arrows on walkers
                                    route: where selected people walk hurt: the rings on hurt people
                                    scale: the scale bar              north: the N arrow
  topcam cone off                   the same as  topcam layer cone off  (also: places, hostiles, traders)
  topcam waypoints x z [x z ...] --name text   draw a multi-point route on the map;  topcam waypoints clear
  topcam textscale 1.3              bigger names and labels (0.5..3)
  topcam cone reach 2 fill 0.25     how long the cone is (0.5..8) and how strong its inside is (0..1)

The view, by hand (positions are world units: 10 units = 1 m, +X is east, +Z is south; "topcam" alone prints the map's
centre under "now"):

  topcam yaw 90                     turn the map (0: north up; 90: east up);  topcam yaw 90 --over 2  glides there in 2 s
  topcam view 43000 -18000 3000     a fixed view: centre x z and the width across, in world units
  topcam view follow                the same as  topcam follow camera;  topcam view follow 3000  also sets the width
  topcam ease 0.5                   how softly the view follows the squad and turns (seconds, 0..10)
  topcam rect 100 100 640 448       an exact rectangle in window pixels (x y w h, at least 16 x 16) instead of a corner

The picture:

  topcam quality high               the three presets:  high   full sharpness, a still map 30 times a second,
                                                                a moving map every frame, everything rendered
                                                        medium 0.75 sharpness, still 20 a second, moving 30
                                                        low    0.5 sharpness, still 10 a second, moving 20, no plants
                                    the settings below change one part each (quality then reads "custom")
  topcam fps 30                     how often a still map is redrawn
  topcam movefps 30                 how often a moving map is redrawn at most (0: every frame)
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
    frame rate drops: topcam quality medium (or low), topcam size small, or topcam off. The big map costs the most.
  - It shows what the game has loaded. Buildings, plants and characters exist only near where the main camera looks,
    so a map far from the main view (follow off, or the camera far from the squad) shows bare ground there.
  - Wider than 900 m across, characters are left out of the picture (they would be smaller than a pixel). The dots stay.
  - Red dots are the characters the game itself counts as hostile to you or fighting you, awake and alive, whether or
    not your people can see them. Turn them off with  topcam hostiles off  if you find that unfair.
  - Routes come from the game's own path for each of your selected people, from their next step on. The part the
    game has not worked out yet is drawn fainter. At most 16 routes are drawn.
  - A hurt ring goes by the worse of a character's blood and their most hurt body part: orange below 60 %, red below 30 %.
  - Town and place names come from the game's own map knowledge: a place appears once discovered, under its
    "unexplored" name until you have visited it. Nests and camps are not named. Zoomed out beyond 2 km, only towns.
  - At most 48 of your people are drawn (40 with names), 40 hostiles and 48 place labels. A label with no free room is
    not drawn (its diamond stays). On a round map, text that would cross the circle's edge is left out.
  - When you hide the game's interface (F7, or hud hide in the console), the whole map hides with it and comes back
    with it. To keep the map on screen without the interface: topcam hidewithui off (or hidewithui = off). The map's
    names and labels are part of the game's HUD, so with hidewithui off they still go away with the interface; the
    picture, the dots and the cone stay.
  - The settings page keeps only what you changed there; margin, rect and yaw are kept by the config file only.
  - If something goes wrong while drawing, the affected part switches itself off and writes one line to the game's log
    ("mod       timelight-map: ..."); the game goes on.  topcam on  /  topcam marks on  try again.
  - Built for one MeitouClient release line (see above).


LICENCE
-------

TimeLight Map is free software: you can redistribute it and/or modify it under the terms of the GNU General Public
License as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later
version. It is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without even the implied
warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. The full text is in the file LICENSE.

The complete source code is in TimeLightMap-1.5.0-source.zip, published together with this zip.

The mod is built against MeitouClient 0.24.0, which is licensed GPL-3.0-or-later, and patches it at run time with
Harmony, which is licensed MIT. Both come with the game; neither is included here. No game data or assets are included.


VERSIONS
--------

1.5.0  Built for MeitouClient 0.24.0 (accepts 0.24.x; verified on 0.24.0 and 0.24.1). New: moving trade caravans shown
       as emerald-green ringed diamonds with walk arrows and non-overlapping green name labels (traders = on,
       Settings > Mods > Moving traders, topcam traders on|off; stationary town shopkeepers and hostile factions
       excluded), plus topcam waypoints for custom multi-point routes. Improved: circular-chord wing tucking for large
       squads on round maps, round-map shader early-out, active-fighter hostile priority, and full topcam reset.
1.4.0  Built for MeitouClient 0.22.0 (accepts 0.22.x). New: group auto-zoom (autozoom / minzoom) that eases in closer
       when your squad is huddled together and out as they spread. Improved: balanced two-wing squad name columns,
       leader-line avoidance so town names stay clear, darker text pills and chrome backing. Fixed: window clip and
       round-map aspect, stale shown-frame state, topcam reset completeness, settings corner/custom-key handling, sky
       eye restore, non-finite camera guard. Full list: CHANGELOG.md.
1.3.2  Built for MeitouClient 0.22.0 (accepts 0.22.x). The map itself is unchanged.
1.3.1  Built for MeitouClient 0.21.0 (accepts 0.21.x). The map itself is unchanged.
1.3.0  Built for MeitouClient 0.20.0 (accepts 0.20.x). New: a settings page (Settings > Mods), hotkeys (N, Shift+N,
       =, -), the big map, a round map, a frame, routes of the selected people, hurt rings, follow selected, quality
       presets, topcam save / reset. Fixed: the map follows your active squad (not every squad mixed together); wrong
       on/off words are refused instead of read as off; one switch per mark kind; status JSON. Full list: CHANGELOG.md.
1.2.0  Built for MeitouClient 0.20.0 (accepts 0.20.x). New: the map hides when you hide the game's interface
       (F7 / hud hide) and comes back with it; topcam hidewithui off (or hidewithui = off) keeps the old behaviour.
1.1.0  Built for MeitouClient 0.19.0 (accepts 0.19.x). The map itself is unchanged.
1.0.0  First version, built for MeitouClient 0.17.4 (accepts 0.17.4 and later 0.17.x).


CREDITS
-------

Built by the Kenshi Live Show team.
Harmony (MIT) by Andreas Pardeike and contributors, as shipped with MeitouClient.
