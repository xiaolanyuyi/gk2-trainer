# GK2 Trainer · Graveyard Keeper 2 Runtime Trainer

[中文](README.md) | [English](README.en.md)

A **runtime stat trainer** for *Graveyard Keeper 2*: edit money, tech spheres, energy/insanity/stamina,
health, movement speed, game speed, perks, tech unlocks and inventory items while you play.

**[⬇ Download the latest release](../../releases/latest)** ·
[Usage](#install--usage) · [Troubleshooting](#troubleshooting) · [Reverse engineering notes](docs/recon.md)

- **It calls the game's own API** — money goes through `PlayerData.SetRes("money", …)` (that is exactly
  what the game's own trading code does), perks through `PerkSystemData.AddPerk`, tech through
  `KnowledgeSystem.UnlockTech`. No memory scanning, no address hunting, no fake UI values.
- **The in-game plugin is four small C# files** (~25 KB) compiled against the game's own
  `Assembly-CSharp.dll`, with zero reflection. It keeps working across game updates as long as those
  member names stay.
- **Resource limits come from the game** — the "range" column (e.g. tech spheres capped at 999) is read
  from the game's own resource systems.
- **Speed edits are locked** — movement and game speed are rewritten every frame, so combat and pauses
  cannot silently undo them.
- **No manual dependencies** — if BepInEx is missing the app downloads and installs it
  (5.4.23.5, which bundles Doorstop 4.5 for Unity 6).

![Resources tab](docs/screenshots/01-resources.png)

> The UI is currently **Chinese only**. The screenshots show the layout: status header on top,
> seven feature tabs below.

---

## Table of contents

- [Screenshots](#screenshots)
- [What it can change](#what-it-can-change)
- [How it works](#how-it-works)
- [Install & usage](#install--usage)
- [Troubleshooting](#troubleshooting)
- [Building from source](#building-from-source)
- [Project layout](#project-layout)
- [Credits](#credits)
- [Disclaimer](#disclaimer)

---

## Screenshots

**Speed tab** — the headline feature: movement speed and game speed, each independently locked.

![Speed tab](docs/screenshots/02-speed.png)

- **Movement speed** = base speed × multiplier. The game's own attack states set the multiplier to 0.5
  while swinging; "apply and lock" rewrites it every frame so you never slow down mid-fight.
  "Unlock" restores normal behaviour.
- **Game speed** drives `Time.timeScale`: animations, the day/night cycle and machine progress all speed
  up together. It never fights a pause (the game freezes by setting timeScale to 0) and restores the
  original value when unlocked.

**Perks tab** — fetches the game's own 311 perk definitions, search and tick to add or remove.

![Perks tab](docs/screenshots/04-perks.png)

**Tech tab** — shows progress (e.g. 35/236 unlocked), unlock a single tech by id, or unlock everything
with one (irreversible) click.

![Tech tab](docs/screenshots/05-tech.png)

The remaining tabs: **resources** (8 values with the game's real limits), **health** (current/max, heal,
damage-immunity toggle), **inventory** (pick from 814 item definitions), **log/debug** (plugin log,
BepInEx log, probe).

## What it can change

| Category | Details | Game-side API |
|---|---|---|
| Resources | money, tech red/green/blue, energy, insanity, stamina, happiness (add / subtract / set) | `PlayerData.SetRes / AddRes / GetRes` |
| Movement speed | multiplier 0.1–20, locked so combat cannot undo it | `PlayerController.PhysicalBody.SpeedMultiplier` |
| Game speed | 0.1–10, locked and pause-safe | `Time.timeScale` |
| Health | set / refill / damage-immunity toggle | `PlayerData.hpComponent.Hp / MaxHpValue / IsImmuneToDamage` |
| Perks | add or remove any of the game's 311 perk definitions | `PerkSystemData.AddPerk / RemovePerk / HasPerk` |
| Tech | unlock one tech by id, or all of them | `KnowledgeSystem.UnlockTech` + `GameBalance.Me.techDefs` |
| Inventory | add items by id + count (checked against capacity first) | `Inventory.AddItemToInventory(new Item(id, count))` |

## How it works

```
┌──────────────────┐   command.json    ┌──────────────────────────────────────┐
│  GK2Trainer.exe  │ ────────────────► │  GraveyardKeeper2.exe                │
│  C# WinForms     │                   │   └─ BepInEx 5 (installed for you)   │
│                  │ ◄──────────────── │        └─ GK2Trainer.Plugin.dll      │
└──────────────────┘    state.json     └──────────────────────────────────────┘
```

The desktop app only renders the UI, turns button presses into commands and reads/writes one JSON file.
The code that actually edits values is the **plugin inside the game process**, calling the game's own
public API.

| File | Direction | Content |
|---|---|---|
| `Trainer\command.json` | app → game | `{token, seq, ops:[…]}` |
| `Trainer\state.json` | game → app | full state + **acknowledgement** (token/seq) |
| `Trainer\log.txt` | game → app | plugin diagnostics |

(all under `%USERPROFILE%\AppData\LocalLow\Lazy Bear Games\Graveyard Keeper 2\`)

Two design details worth knowing:

- **Command de-duplication** — the plugin writes the last executed `(token, seq)` back into the state
  file, so **reloading a save can never replay the last command** (the classic accident: gold added twice
  after a reload).
- **Locking (freeze)** — values the game rewrites on its own (movement/game speed) are re-applied every
  frame, and the plugin deliberately yields while the game is paused.

## Install & usage

### 0. Requirements

| Requirement | Notes |
|---|---|
| Graveyard Keeper 2 | Unity 6000 / Mono; verified on **1.004.2** |
| BepInEx 5 + UnityDoorstop | **installed automatically** by the app (5.4.23.5 with Doorstop 4.5) |
| .NET | not needed for the released exe; .NET 10 SDK needed to build from source |

### 1. Run the trainer

Download the release and run it. It locates the game by checking the running process first, then a stored
path, then the Steam libraries — an installation on another drive or a non-Steam copy works just as well.
Otherwise use 「选择游戏目录…」 to point it at the game folder.

### 2. Install the plugin

Click **「安装 / 更新插件」**: it installs BepInEx if needed and writes the plugin into
`BepInEx\plugins\`. Then **restart the game** (plugins load at startup).

### 3. Play

Start the game, **load a save**, switch back to the trainer. The header shows
`连接：已连接 · 插件 v0.1.0 · 存档中 · 第 N 天 · seq=…` and every tab becomes live.

> - Changes apply immediately; resource changes are stored in the savegame.
> - **"Unlock all techs" is an irreversible progression change** — back up your save first.
> - Movement/game speed stay locked until you click 「解除锁定」.

### Uninstalling

「卸载插件」 removes the plugin (BepInEx stays). For a full removal, delete `BepInEx\`, `winhttp.dll`,
`doorstop_config.ini` and `.doorstop_version` from the game folder.

## Troubleshooting

**The header keeps waiting for the plugin**
1. Make sure the plugin is installed (the header shows `插件：已部署且是最新`);
2. **Restart the game** — plugins load at startup;
3. **Load a save** — there is nothing to edit at the main menu;
4. Check 「日志 / 调试」: the BepInEx log shows `Loading [GK2 Trainer Bridge …]` when the plugin loaded;
   the plugin's own log is `Trainer\log.txt`.

**The game crashes or shows a black screen on startup**
Usually a BepInEx/Doorstop version mismatch. This project pins a verified combination (BepInEx 5.4.23.5 +
Doorstop 4.5). To undo everything, delete `BepInEx\`, `winhttp.dll`, `doorstop_config.ini` and
`.doorstop_version` from the game folder — the game files themselves are never modified.

**Installing while the game is running**
The app renames the old plugin to `*.dll.old` before writing the new one (a running game memory-maps the
loaded assembly, which blocks overwriting it). **It takes effect on the next game start.**

**A value did not change in game**
Resource edits are immediate; failures are reported in the log tab (inventory full, wrong character,
out of range…). The `游戏内探针（probe）` button prints the game-side truth.

**Does the trainer need to stay open?**
No. It only sends commands. Locked values (speed) keep being enforced by the plugin after you close it.

## Building from source

```powershell
# 1. plugin (game side)
dotnet build plugin\GK2Trainer.Plugin.csproj -c Release
#    non-default game path: dotnet build plugin\GK2Trainer.Plugin.csproj -c Release -p:GameDir="D:\Games\Graveyard Keeper 2"

# 2. desktop app (embeds the plugin)
dotnet build src\GK2Trainer.App\GK2Trainer.App.csproj

# 3. deploy the plugin into the game (works while the game is running)
powershell -File tools\deploy.ps1

# 4. package
powershell -File tools\publish.ps1                 # single file exe (needs .NET runtime)
powershell -File tools\publish.ps1 -SelfContained  # fully standalone

# 5. regenerate the README screenshots (game must be running)
powershell -File tools\capture-screenshots.ps1
```

Head-less commands:

```powershell
GK2Trainer.exe diagnose                 # environment check + print game state
GK2Trainer.exe deploy                   # install BepInEx + deploy plugin
GK2Trainer.exe uninstall                # remove the plugin
GK2Trainer.exe dump-state               # print the game-side state
GK2Trainer.exe op setRes name=money value=9999
GK2Trainer.exe op setMoveSpeed value=3
GK2Trainer.exe op listIds kind=perk limit=50
```

How these APIs were found (and which assumptions were verified in game) is written down in
[`docs/recon.md`](docs/recon.md) (Chinese).

## Project layout

```
├─ plugin/                         ← BepInEx plugin injected into the game (game side)
│  ├─ Plugin.cs                    ← entry: tick polling, command de-duplication, locks
│  ├─ Bridge.cs                    ← file bridge, logging, atomic writes
│  ├─ Snapshot.cs                  ← state collection (resources/health/speed/perks/tech)
│  └─ Ops.cs                       ← every operation
├─ src/GK2Trainer.App/             ← C# WinForms front end
│  └─ Core/
│     ├─ Paths.cs                  ← game detection (running process → settings → Steam)
│     ├─ PluginDeployer.cs         ← plugin deployment / automatic BepInEx install
│     ├─ Bridge.cs                 ← command queue, state polling, retries
│     ├─ Models.cs / LenientJson.cs← protocol DTOs + tolerant JSON parsing
│     └─ Cli.cs                    ← head-less commands
├─ tools/                          ← deploy, screenshot and packaging scripts
└─ docs/recon.md                   ← reverse engineering notes and verification log
```

## Credits

- **[BepInEx](https://github.com/BepInEx/BepInEx)** (LGPL-2.1) — the plugin loader this project depends
  on. The app only downloads it when it is missing; **it is not redistributed here**.
- **[UnityDoorstop](https://github.com/NeighTools/UnityDoorstop)** (MIT) — bundled inside BepInEx 5.4.23.5.
- **[Newtonsoft.Json](https://www.newtonsoft.com/json)** (MIT) — the plugin uses the copy shipped with
  the game.
- **Graveyard Keeper 2 © Lazy Bear Games / tinyBuild** — this project contains no game assets. The item,
  perk and tech id lists are read from the game at runtime and are not part of this repository.

## Disclaimer

- For **single player** personal use only; don't spoil other players' games.
- Editing saves carries risk — back up `Steam_*.dat` / `.info` under
  `%USERPROFILE%\AppData\LocalLow\Lazy Bear Games\Graveyard Keeper 2\` first (the game keeps three
  automatic backups as well).
- Not affiliated with Lazy Bear Games, tinyBuild or the BepInEx authors.
- License: [MIT](LICENSE)
