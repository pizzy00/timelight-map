# TimeLight Map

A live 3D map in a corner of your Kenshi game window that follows your squad. It is the game world itself, rendered a
second time from above, with your people as named dots, the routes your selected people walk, rings on the hurt,
hostiles as red dots, discovered town names, a dotted cone for where your camera looks, a scale bar and a north arrow.

Set it up on the game's settings page (Settings > Mods > TimeLight Map), with hotkeys while you play (N show/hide,
Shift+N big map, `=` / `-` zoom), in a small config file, or in the game's console (`topcam ...`).
1.4.0 is built for MeitouClient 0.22.x. New in 1.4.0: automatic close-up zoom when your followed group huddles together
(`autozoom` / `minzoom`), cleaner balanced two-wing squad name labels with darker backing pills and leader-line avoidance
so town names and chrome stay readable, plus a round of stability and layout bug fixes; see the [CHANGELOG](CHANGELOG.md).

- **Download:** [Steam Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3839458940), [Nexus Mods](https://www.nexusmods.com/kenshi/mods/2285), or the zip under [Releases](../../releases)
- **Needs:** [MeitouClient](https://github.com/VlcekM/MeitouClient) 0.22.x (TimeLight Map 1.4.0). On MeitouClient 0.21.x use
  [1.3.1](../../releases/tag/v1.3.1), on 0.20.x use [1.3.0](../../releases/tag/v1.3.0), on 0.19.x use
  [1.1.0](../../releases/tag/v1.1.0), on 0.17.x use [1.0.0](../../releases/tag/v1.0.0). It does not run on stock Kenshi or RE_Kenshi.
  Developed on the Linux (Vulkan) build.
- **Install, hotkeys, settings, config keys, console commands, known limits:** [README.txt](README.txt) · **Changes:** [CHANGELOG.md](CHANGELOG.md)
- **Building from source:** [BUILDING.txt](BUILDING.txt)
- **License:** GPL-3.0-or-later ([LICENSE](LICENSE)). Uses Harmony (MIT), shipped with MeitouClient.

Built by the Kenshi Live Show team. Feedback and feature requests: open an issue, or comment on the Nexus page.

Kenshi Live Show: a brutal Kenshi crew with dark anime vibes, live 24/7 on [Twitch](https://twitch.tv/beepistoostrong) · [YouTube](https://www.youtube.com/@BeepIsTooStrong) · [Kick](https://kick.com/beepistoostrong) · [Rumble](https://rumble.com/user/BeepIsTooStrong) | @BeepIsTooStrong
