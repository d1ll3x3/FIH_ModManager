# FIH Mod Manager

Mod manager for **Flipping is Hard** (BepInEx 6 IL2CPP). Install BepInEx, add, enable,
update and configure mods without touching the game files by hand.

## Features

- **One-click BepInEx**: installs the bundled BepInEx 6.0.0-be.755 (Unity IL2CPP x64), with
  reinstall, update and uninstall. Uninstalling keeps your plugins, configs and profiles.
- **Game detection**: finds every Flipping is Hard install in your Steam libraries and desktop
  shortcuts; other folders can be added by hand.
- **Play / Play without mods**: "Play without mods" launches with `--doorstop-enabled false`,
  so no file is changed.
- **My mods**: enable/disable with a switch, search, delete (to the Recycle Bin), open folder.
  Shows the real name, version and GUID read from each DLL and warns about mods built for
  BepInEx 5 or Unity Mono.
- **Drag and drop**: drop a `.dll`, `.zip` or folder on the window to install it. Updating a mod
  copies over the existing folder, so maps, skins and other user data are kept.
- **Explore**: catalog of Flipping is Hard mods installed straight from their GitHub releases,
  plus any other repo (`user/repo` or URL). Shows a badge when a new release is out.
- **Configuration editor**: edits `BepInEx.cfg` and every mod `.cfg` with toggles, dropdowns and
  checkboxes. Only the changed lines are rewritten. Other files (`.json`, `.txt`) open as text.
- **Profiles**: save the set of enabled mods and switch between sets in one click.
- **Self-update**: checks this repository's releases on startup and offers to download and
  install the new version.
- **8 languages**: English, Español, Français, Русский, Polski, العربية (right-to-left),
  简体中文 and 한국어. Change it in Settings.

## Installation

Download `FIHModManager.exe` from the
[latest release](https://github.com/d1ll3x3/FIH_ModManager/releases/latest) and run it.
It is a single self-contained file for Windows 10/11 x64: no installer and no .NET runtime
needed.

Steam installs are found on any drive. Non-Steam copies are found through a desktop shortcut,
or can be added from Settings > Add folder.

## How files are organised

| What | Where |
|---|---|
| Enabled mods | `BepInEx\plugins` |
| Disabled mods | `BepInEx\disabled_plugins` (BepInEx does not load them) |
| Profiles and the source of each mod | `BepInEx\fihmm.json` |
| App settings and log | `%APPDATA%\FIHModManager` |

Mods disabled by renaming their DLL (`.dl`, `.dll.disabled`) are recognised as disabled, and
enabling them restores the `.dll` extension.

## Building

Requires the .NET 10 SDK.

```
dotnet publish src/FIHModManager -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o publish
```

Translations live in `src/FIHModManager/Resources/Lang/<code>.json`; missing keys fall back to
English.

## License

GNU Affero General Public License v3.0, see [LICENSE](LICENSE). BepInEx is bundled under the
LGPL-2.1, see [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
