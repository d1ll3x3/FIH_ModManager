# Third-party notices

## BepInEx

FIH Mod Manager embeds an unmodified build of BepInEx
(`BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.755+3fab71a`) so it can install it into the game.

- Project: https://github.com/BepInEx/BepInEx
- Builds: https://builds.bepinex.dev/projects/bepinex_be
- License: GNU Lesser General Public License v2.1
  (https://github.com/BepInEx/BepInEx/blob/master/LICENSE)

The embedded archive is located at `src/FIHModManager.Core/Resources/BepInEx.zip` and is
distributed as-is. Its source code is available from the project link above.

## Linux build only

- **Avalonia** 11.3 (UI framework): MIT License, https://github.com/AvaloniaUI/Avalonia
- **Inter** (UI font, via Avalonia.Fonts.Inter): SIL Open Font License 1.1, https://github.com/rsms/inter
- **Material Icons** (`src/FIHModManager.Linux/Assets/Fonts/MaterialIcons-Regular.ttf`, unmodified):
  Apache License 2.0, https://github.com/google/material-design-icons
