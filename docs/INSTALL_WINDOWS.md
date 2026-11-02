# REMNANTS on Windows

Needs Windows 10 or 11, 64-bit. You don't need to install .NET or anything else. The game brings its own runtime.

## Install and run

1. Unzip `remnants-windows-x86_64.zip` anywhere, for example `C:\Games\remnants`. Keep every file in the folder together.
   `remnants.exe` needs `remnants.pck` and the `data_Remnants.Game_windows_x86_64` folder next to it.
2. Double-click `remnants.exe`.
3. If the game crashes or won't start, run `remnants.console.exe` instead. It opens a console window that shows the log.
   Copy that text into your bug report.

## "Windows protected your PC" (SmartScreen)

The build isn't code-signed, so SmartScreen may block the first launch. Click **More info**, then **Run anyway**.
Windows only asks once.

## Firewall prompt (multiplayer)

When you **Host** a game, Windows Defender Firewall asks whether `remnants.exe` may accept connections. Allow it on
**Private networks** so friends on your LAN can join. The host listens on **TCP 7777**. To play over the internet, the
host must forward TCP 7777 on their router to their PC. Joining a game needs no firewall change.

## Where saves go

Saves, settings and your Trading Post identity are in:

```
%APPDATA%\Godot\app_userdata\Remnants\
```

Paste that path into the Explorer address bar to open it. `colony.json` is your colony save, `settings.cfg` holds your
settings, and `trade_identity.json` is your Trading Post key. Keep that file safe: if you lose it, you can't reach your
open listings. Deleting the game folder does not delete your saves.

## Modding the balance (optional)

The game data is built into the game. To override it, copy the repo's `data\` folder next to `remnants.exe`. It
must contain `resources.json`. The game then reads its definitions from that folder.
