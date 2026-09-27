# Nocturne Modern Controller

[日本語 README](README.md)

A Windows MelonLoader mod for the Steam version of *Shin Megami Tensei III Nocturne HD Remaster*. It modernizes controller input: a native right-stick camera, context-specific bindings, editing of the game's own key config, and an integrated settings window that also hosts the gameplay mods listed below.

Current version: **3.0.0**. See the [CHANGELOG](CHANGELOG.md).

> **Changed in v3.0.0:** Dash, Quick Heal, Force Encounter, and Smart Auto Battle are no longer part of the Controller. They are **separate mods** now. To keep using them, install the matching mods as well. See [Upgrading from v2.0.3](#upgrading-from-v203).

## Packages

| Package | Contents | Required |
|---|---|---|
| NocturneModernController | Controller input, bindings, settings window (this mod) | Base |
| NocturneForceEncounter | Force Encounter | Optional |
| NocturneQuickHeal | Quick Heal | Optional |
| NocturneModernDash | Dash / Dash Keep | Optional |
| NocturneSmartAutoBattle | Smart Auto Battle | Optional |

All five are recommended, but you can install only the gameplay mods you want. Each gameplay mod also works without the Controller; with the Controller installed it joins the settings window and the key bindings.

## Controller features

- Native right-stick dungeon turning and vertical camera control
- Right-stick rotation in PUZZLE (injected as the game's logical input)
- Full Camera and Horizontal Turn modes
- X/Y inversion, sensitivity, and dead-zone settings
- Suppression of legacy LB/RB dungeon turning while preserving BATTLE RB Pass
- SDL3-based generic gamepad input without fixed vendor/product IDs
- Context-specific bindings for FIELD/DUNGEON, BATTLE, PUZZLE, and MENU, with chords of up to three buttons
- Editing of the game's native key config (GAME Bindings)
- Integrated settings window that shows on/off switches and selectable values published by the gameplay mods

The settings window automatically uses Japanese on a Japanese Windows UI and English on other Windows UI languages. You can override this with the **Auto / 日本語 / English** selector. A language change is applied the next time the window opens.

## Gameplay mods

| Mod | What it does | Input without Controller | Default binding with Controller | Settings file |
|---|---|---|---|---|
| NocturneForceEncounter 0.2.0 | Requests an immediate battle through the game's normal encounter check, only where encounters can happen | X | X (rebindable) | `NocturneForceEncounter.settings.json` |
| NocturneQuickHeal 0.1.0 | Heals the party while exploring with learned recovery skills and real MP; revives and cures ailments only with skills you have | SELECT | RB (rebindable) | `NocturneQuickHeal.settings.json` |
| NocturneModernDash 0.1.0 | Moves 1.5x faster in dungeons and on the world map; LT+RT toggles Dash Keep | Hold LT/RT, LT+RT for Keep, keyboard P | LT/RT, Keep LT+RT (rebindable), keyboard P | `NocturneModernDash.settings.json` |
| NocturneSmartAutoBattle 0.1.0 | On top of the game's own Auto, picks commands and single targets from weaknesses, resistances, MP, and kill predictions; optional battle speed while Auto runs | The game's Auto button | No own binding (the game's Auto button) | `NocturneSmartAutoBattle.settings.json` |

- Without the Controller, Quick Heal uses SELECT because RB is the game's own dungeon turn. The Controller suppresses that LB/RB turn, so with the Controller it defaults to RB.
- Smart Auto Battle modes are Normal Attack Only (the game's Auto unchanged) and Skill Priority; speeds are x1.0 / x1.5 / x2.0. A new install starts in Skill Priority at x1.0. The speed applies only while the game's Auto is on in battle.
- Each mod keeps its own settings file. With the Controller you change them in the **MOD Features** tab; without it, edit the file (applied after restarting the game).

| Mod | Standalone | Controller integration |
|---|---|---|
| NocturneForceEncounter | Yes | Yes (Controller 3.0.0 or later) |
| NocturneQuickHeal | Yes | Yes (Controller 3.0.0 or later) |
| NocturneModernDash | Yes | Yes (Controller 3.0.0 or later) |
| NocturneSmartAutoBattle | Yes | Yes (Controller 3.0.0 or later; the Mode and Speed selectors need 3.0.0) |

## Supported controllers

Any controller recognized by SDL3 is a design target, including Xbox, PlayStation, Switch Pro, and common USB/Bluetooth gamepads. Xbox Elite Series 2 has been tested over wired and wireless connections. Other controller families are supported by design but have not been exhaustively tested on every model. reWASD is not required.

## Requirements

- Steam version of SMT3HD
- MelonLoader for the Il2Cpp game build
- .NET 6 Desktop Runtime

## Installation

Extract each ZIP into the game directory (`smt3hd`). Every ZIP starts at `Mods/`.

Controller (`NocturneModernController-v3.0.0.zip`):

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

Each gameplay mod ZIP (for example `NocturneModernDash-v0.1.0.zip`) contains one DLL:

```text
smt3hd/
└─ Mods/
   ├─ NocturneForceEncounter.dll
   ├─ NocturneQuickHeal.dll
   ├─ NocturneModernDash.dll
   └─ NocturneSmartAutoBattle.dll
```

Settings files are created in `Mods` on first run; they are not in the ZIPs.

## Upgrading from v2.0.3

In v3.0.0, Dash / Dash Keep, Quick Heal, Force Encounter, and Smart Auto Battle, which were built into the Controller up to v2.0.3, have been **removed from the Controller** and ship as separate mods. If you update only the Controller, those features are gone. Extract the matching mod ZIPs to keep them.

- **Settings carry over.** On first run, when a mod has no settings file of its own yet, it reads its old values from the Controller's `NocturneModernController.settings.json`:
  - Force Encounter: `ForceEncounterEnabled`
  - Quick Heal: `QuickHealEnabled`
  - Dash: `DashEnabled`
  - Smart Auto Battle: `SmartAutoEnabled`, `AutoBattleMode`, `AutoBattleSpeed`, and the learned affinities (a copy of `NocturneModernController.smart-auto-knowledge.json`)

  After that, each mod's own settings file is authoritative. The Controller's old values and files are left in place.
- **Bindings carry over.** Force Encounter, Quick Heal, Dash, and Dash Keep keep their old action IDs, so bindings you changed are used again as soon as the mod is installed.
- **The old Auto Battle settings tab is gone.** Set Smart Auto Battle's mode and speed in the **MOD Features** tab.
- **Disabling Smart Auto Battle now fully disables it.** In the old built-in version, turning it off still left parts of Skill Priority command replacement and learning running. Now, when disabled, it changes no speed, commands, or targets and learns nothing; the game's own Auto runs unchanged.

> **Important:** Do **not** combine the new gameplay mods with Controller **v2.0.3 or earlier**. The old Controller contains the same features, so both would run (for example Smart Auto Battle selecting commands twice, or both writing the battle speed). Use Controller 3.0.0 or later with the gameplay mods.

If you remove a gameplay mod, its bindings stay in the Controller's `bindings.json`. That is intended: they are used again if you reinstall the mod. You do not need to delete anything.

## Opening settings

Hold **Select** for about 0.8 seconds in the game. The game is minimized while the settings window is open and restored when it closes.

The Controller's only default binding is Select (long press): Open Settings. Installed gameplay mods add their own actions with the default bindings listed above. Context separation prevents FIELD bindings from unconditionally replacing standard BATTLE commands such as RB Pass.

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

## MOD Features tab

Cards are built from the feature information each mod publishes. The Controller's own card is **Right Stick Camera**; installed gameplay mods add theirs (Smart Auto Battle adds three: on/off, mode, speed). Besides on/off switches, cards can offer selectable values. A broken or missing mod file only removes that mod's cards, never the whole window.

## Verified behavior

- Right-stick horizontal turning and standard vertical camera in dungeons
- Suppression of legacy LB/RB dungeon turning while preserving BATTLE RB Pass
- Integrated settings, bindings, and the MOD Features tab (on/off and selectable values)
- GAME binding editing (Command Menu Y -> X -> Y): reflected in actual input and the native Key Config screen, kept across restarts, and the native configuration file was byte-identical to the original after the round trip
- Controller with all four gameplay mods, Controller only, and each gameplay mod on its own
- Carry-over of v2.0.3 settings, bindings, and Smart Auto knowledge

## Known limitations

- PUZZLE right-stick rotation is implemented but still needs final real-play verification.
- PlayStation, Switch, and generic SDL controllers have not been tested model by model.
- Smart Auto Battle is heuristic and may make suboptimal decisions in unusual encounters.
- Smart Auto Battle's speed is verified to return to normal on victory and when Auto is turned off; escape, game over, and returning to the title have not been exhaustively tested.
- The mod integration interface is not yet a stable third-party SDK compatibility promise.

## Separate project: NocturneModernGameplay

Experimental gameplay-rule changes (such as Skill Mutation: Learn as New) are developed in the separate `NocturneModernGameplay` project. They are not part of this repository or its packages.

## Building

```powershell
dotnet build .\NocturneModernController.csproj -c Release --no-restore
dotnet build .\helper\NocturneModernController.InputHelper.csproj -c Release --no-restore
dotnet build .\settings\NocturneModernController.Settings.csproj -c Release --no-restore
.\tools\Build-Release.ps1
```

The gameplay mods are the projects under `mods/`. `Build-Release.ps1` builds everything reproducibly from a temporary git worktree and writes the Controller ZIP and the four gameplay mod ZIPs to `artifacts/release/`, each versioned by its project. Building requires the MelonLoader and Il2Cpp assemblies generated by SMT3HD. `SDL3.dll` comes from the official SDL distribution and is covered by its own license notice.

Release notes: [docs/releases/v3.0.0.md](docs/releases/v3.0.0.md).

## License and notices

Repository code is covered by [LICENSE](LICENSE). See [THIRD_PARTY_NOTICES](THIRD_PARTY_NOTICES.txt) for SDL attribution. No game executable, Atlus/Sega asset, or generated game assembly is distributed. Back up your save data before using mods.
