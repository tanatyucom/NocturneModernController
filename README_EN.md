# Nocturne Modern Controller

[日本語 README](README.md)

A Windows MelonLoader mod for the Steam version of *Shin Megami Tensei III Nocturne HD Remaster*. It adds modern controller and exploration controls while preserving the game's standard command paths wherever possible.

Current public version: **2.0.3**.

## Features

- Native right-stick dungeon turning and vertical camera control
- Full Camera and Horizontal Turn modes
- X/Y inversion, sensitivity, and dead-zone settings
- SDL3-based generic gamepad input without fixed vendor/product IDs
- Context-specific bindings for FIELD/DUNGEON, BATTLE, PUZZLE, and MENU
- Chords of up to three controller buttons
- Dash and persistent Dash Keep
- Quick Heal using learned recovery skills and real party MP
- Force Encounter only where normal encounters are available
- Smart Auto Battle using weaknesses, resistances, reflection, absorption, MP, and attack predictions
- Integrated settings window and metadata-driven external feature providers

The settings window automatically uses Japanese on a Japanese Windows UI and English on other Windows UI languages. You can override this with the **Auto / 日本語 / English** selector. A language change is applied the next time the window opens.

## Supported controllers

Any controller recognized by SDL3 is a design target, including Xbox, PlayStation, Switch Pro, and common USB/Bluetooth gamepads. Xbox Elite Series 2 has been tested over wired and wireless connections. Other controller families are supported by design but have not been exhaustively tested on every model. reWASD is not required.

## Requirements

- Steam version of SMT3HD
- MelonLoader for the Il2Cpp game build
- .NET 6 Desktop Runtime

## Installation

Extract the release ZIP into the game directory so the files are arranged as follows:

```text
smt3hd/
└─ Mods/
   ├─ NocturneModernController.dll
   └─ NocturneModernController.Helper/
      ├─ NocturneModernController.InputHelper.exe
      ├─ NocturneModernController.InputHelper.dll
      ├─ NocturneModernController.InputHelper.deps.json
      ├─ NocturneModernController.InputHelper.runtimeconfig.json
      ├─ NocturneModernController.Settings.exe
      ├─ NocturneModernController.Settings.dll
      ├─ NocturneModernController.Settings.deps.json
      ├─ NocturneModernController.Settings.runtimeconfig.json
      └─ SDL3.dll
```

## Opening settings

Hold **Select** for about 0.8 seconds in the game. The game is minimized while the settings window is open and restored when it closes.

Default bindings include:

- LT or RT: Dash while held
- LT + RT: Toggle Dash Keep
- RB: Quick Heal while exploring
- X: Force Encounter
- Select (long press): Open Settings

Context separation prevents FIELD bindings from unconditionally replacing standard BATTLE commands such as RB Pass.

## GAME Bindings

The **GAME Bindings** tab in Settings edits the game's own (native) controller key config. Changes are saved to the game's native configuration and persist across restarts.

- 15 actions whose mapping has been verified on real hardware can be edited (Confirm/Action, Cancel, UI Display On/Off, Command Menu, Rotate Camera Left/Right, Reset Camera, Toggle First/Third Person, Auto Map, Skill Help On/Off, Auto Battle, Pass, Fast-Forward Text, Puzzle Menu, Punch).
- Supported buttons: A, B, X, Y, LB, LT, RB, RT, L3, R3, SELECT, START.
- One binding can be changed per **Apply**. Apply queues the change; it takes effect in the game when you close Settings with **OK / Save**. Closing with Cancel, or pressing Undo, discards it.
- An assignment that would duplicate another action's button is rejected using the game's own duplicate check. Bindings are never swapped.
- Settings never writes game memory. The Controller mod inside the game checks that the current value and native state still match, applies the change through the game's own save path, and restores the previous binding if anything fails.
- The outcome is shown as "Last change" the next time Settings opens.
- An action bound to a button outside the supported set is shown as `Unknown (raw=N)` and cannot be edited.
- Enter the field in game before opening Settings so the current bindings can be read reliably.

## Verified behavior

- Right-stick horizontal turning and standard vertical camera in dungeons
- Suppression of legacy LB/RB dungeon turning while preserving BATTLE RB Pass
- Dash in the hospital and on the world map
- Quick Heal using actual learned skills and MP
- Force Encounter through the game's normal encounter route
- Smart Auto Battle through the game's standard Auto command route
- Integrated settings, bindings, five built-in feature cards, and external provider discovery
- GAME binding editing (Command Menu Y -> X -> Y): reflected in actual input and the native Key Config screen, kept across restarts, and the native configuration file was byte-identical to the original after the round trip

## Known limitations

- PUZZLE right-stick rotation is implemented but still needs final real-play verification.
- PlayStation, Switch, and generic SDL controllers have not been tested model by model.
- Smart Auto Battle is heuristic and may make suboptimal decisions in unusual encounters.
- The external Feature Provider interface is not yet a stable third-party SDK compatibility promise.

## Controller and Gameplay separation

This repository contains controller input, the settings UI, and the built-in controller actions. `NocturneModernGameplay` is a separate project and distribution unit for gameplay-rule changes. It is not included in this package. Its experimental Skill Mutation feature must not be treated as part of this release.

## Building

```powershell
dotnet build .\NocturneModernController.csproj -c Release --no-restore
dotnet build .\helper\NocturneModernController.InputHelper.csproj -c Release --no-restore
dotnet build .\settings\NocturneModernController.Settings.csproj -c Release --no-restore
.\tools\Build-Release.ps1 -Version 2.0.3
```

Building the controller DLL requires the MelonLoader and Il2Cpp assemblies generated by SMT3HD. `SDL3.dll` comes from the official SDL distribution and is covered by its own license notice.

## License and notices

Repository code is covered by [LICENSE](LICENSE). See [THIRD_PARTY_NOTICES](THIRD_PARTY_NOTICES.txt) for SDL attribution. No game executable, Atlus/Sega asset, or generated game assembly is distributed. Back up your save data before using mods.
