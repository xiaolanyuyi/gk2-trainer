# 《守墓人 2》修改器 —— 阶段 0 侦察报告

> 按 `game-trainer` skill 的阶段 0 执行。结论都要有证据；没有实测过的写进「待验证」。
> 侦察时间：2026-09-24 ｜ 游戏版本：1.004.2（Unity 6000.3.9f1）

---

## 1. 基本信息

| 项目 | 结论 | 证据 |
|---|---|---|
| 游戏版本 | `1.004.2`（存档元数据里的 `gameSaveVersion`） | `Steam_1.info` |
| 引擎 | **Unity 6000.3.9f1** | `Player.log`: `Initialize engine version: 6000.3.9f1 (7a9955a4f2fa)` |
| 脚本后端 | **Mono**（不是 IL2CPP） | 存在 `MonoBleedingEdge/`，无 `GameAssembly.dll` |
| 位数 | x64 | PE 头 `0x20b` |
| 安装方式 | Inno Setup 安装（`unins000.exe`），非 Steam 目录 | 顶层目录 |
| 主程序 | `GraveyardKeeper2.exe`（0.64 MB）+ `UnityPlayer.dll`（34 MB） | 顶层目录 |
| 游戏代码 | `GraveyardKeeper2_Data/Managed/Assembly-CSharp.dll`（3.77 MB，**未混淆**，2727 个类） | `ilspycmd -l c` |
| 厂商/产品名 | `Lazy Bear Games` / `Graveyard Keeper 2` | `Data/app.info` |
| 图形后端 | Direct3D 12 / URP（`D3D12/` 目录） | `Player.log` |

**结论：这是最好改的一类目标** —— Mono + 未混淆 + 有清晰的公开 API。

## 2. 现存的模组基础设施

| 项目 | 结论 | 证据 |
|---|---|---|
| 游戏自带 Mods 目录 | `%USERPROFILE%\AppData\LocalLow\Lazy Bear Games\Graveyard Keeper 2\Mods\`（同时也会看"可执行文件旁边的 Mods"） | `Mods/README.txt` |
| 官方模组类型 | **目前仅支持 `Languages/`（语言包）**，无玩法数据模组 API | `Mods/README.txt` |
| 开发者快捷键 | **Shift+F10** = 生成示例文件夹并重载模组；**Shift+F11** = 创意工坊上传器 | `Mods/README.txt` |
| 临时禁用某个包 | 文件夹名以 `~` 开头即被忽略 | `Mods/README.txt` |
| BepInEx / Doorstop | **尚未安装**（游戏根目录只有 `UnityPlayer.dll`、`uninst.dll`） | 根目录列举 |
| 社区现状 | Nexus 上已有 **"BepInEx for Graveyard Keeper 2"**（BepInEx 5.4.23.5 + Unity Doorstop 4.5.0，预配置），说明插件路线已被验证 | 搜索证据（Nexus 页面） |

> 注意：Unity **6000.x** 需要较新的 Doorstop（4.5+）。已知能用的组合就是上面那个社区包；
> 若自行从 GitHub 装 BepInEx 5.4.23.x，Doorstop 版本不匹配是"游戏起不来"的首要原因。

## 3. 存档结构（对"存档编辑"路线）

| 项目 | 结论 | 证据 |
|---|---|---|
| 位置 | `%USERPROFILE%\AppData\LocalLow\Lazy Bear Games\Graveyard Keeper 2\` | 目录列举 |
| 文件 | `Steam_1.dat`（11.8 MB）+ `Steam_1.info`（JSON 元数据）+ 3 个自动备份 | 目录列举 |
| 元数据 | JSON：`{day, isAutoSave, saveDateTime, platform, gameSaveVersion, graveyardQuality, churchQuality, villageRep…}` | `Steam_1.info` |
| 主存档格式 | **.NET BinaryFormatter**：偏移 0x0B 处是长度前缀的 UTF-16 串 `"GameSave, Assembly-CSharp"` | `Steam_1.dat` 头部 hex |
| 附带情报 | BinaryFormatter 会把**整个字段图**以 UTF-16 明文写出 → 直接搜出 1712 个标识符（`money`、`curMoney`、`inventory`、`activePerks`、`lockedTechTabs`、`game_res_tech_red/green/blue`…） | 从 `.dat` 提取 UTF-16 串 |

**含义**：存档编辑可行 —— 用 .NET Framework 4.8（自带 `BinaryFormatter`）+ 游戏自己的
`Assembly-CSharp.dll` 反序列化成真实对象图 → 改字段 → 序列化回去。
（.NET 8+ 已禁用/移除 BinaryFormatter，所以存档工具要单独用 net48 工程。）

## 4. 运行时写入 API —— 已定位（这是好消息）

游戏自己的代码就用这些接口改数值，**全部是 public，可直接调用（BepInEx 插件里可强类型引用）**：

| 功能 | API | 证据 |
|---|---|---|
| **金钱（读）** | `MainGame.PlayerData.GetResInt("money")` | `Trading.cs:20`、`CharMainPageWidgetData.cs:85` |
| **金钱（写）** | `MainGame.PlayerData.SetRes("money", value)` | `Trading.cs:24` |
| **金钱（增减）** | `MainGame.PlayerData.AddRes("money", delta)` | `WgoDataExtensions.cs:98` |
| 资源常量 | `LazyConsts.MONEY_KEY = "money"`；科技点为资源 `game_res_tech_red/green/blue` | `LazyConsts.cs:509`、存档字段 |
| 科技点/其它资源 | 同一套 `GetResInt / GetRes / SetRes / AddRes(string, float)` | `PlayerData.cs` |
| 任意 WGO 的资源 | `WgoData.gameRes` / `AddGameRes/SetGameRes/GetGameResInt` | `WgoData.cs`、`WgoDataExtensions.cs` |
| 生命值 | `MainGame.PlayerData.hpComponent` → `Hp` / `MaxHpValue` / `IsImmuneToDamage` | `HPComponent.cs` |
| 能量（睡眠） | `MainGame.PlayerData.energySystem` → `timeWithoutSleep` | `EnergySystem.cs` |
| 体力 | `MainGame.PlayerData.staminaSystem` → `regenerationDelay`（私有）/ `CanPerformAttack()` | `StaminaSystem.cs` |
| 背包 | `MainGame.PlayerData.inventory` / `toolBeltInventory`（`Inventory`，含 `Data.Inventory` 列表） | `PlayerData.cs`、`WgoData.cs` |
| 天赋 | `PerkSystemData.AddPerk(id) / RemovePerk(id) / HasPerk(id)`、`activePerks` | `PerkSystemData.cs` |
| 科技/配方解锁 | `KnowledgeSystem`：`unlockedTechs / unlockedCrafts / unlockedBuildings / unlockedTalentIds` 等 | `KnowledgeSystem.cs` |
| 商人金钱 | `VendorSystem.AddMoneyToVendor(vendorId, money)`、`Vendor.CurMoney` | `VendorSystem.cs:128` |
| 全局入口 | `MainGame.Instance`（MonoBehaviour 单例）+ 大量静态访问器：`MainGame.PlayerData`、`MainGame.WorldData`、`MainGame.PlayerController` | `MainGame.cs` |

**数据模型（存档根到玩家）**：

```
GameSave
 ├─ hostPlayer : NetworkPlayer
 │    └─ playerData : PlayerData ── inventory / toolBeltInventory / res(GameRes) /
 │                                 energySystem / staminaSystem / hpComponent / customization
 ├─ talentSystemData / zombieSystemData / conveyorSystemData
 └─ worldData : WorldData ── GetWgoData(...) → WgoData ── gameRes / activePerks / Inventory
```

## 5. 通道方案（阶段 1 决策）

| 通道 | 可行性 | 说明 |
|---|---|---|
| **A. 进程内插件** | ✅ **首选** | BepInEx 5.4.23.5 + Doorstop 4.5（社区已有预配置包）。插件用 C# 写，可**直接引用 `Assembly-CSharp.dll`** 调用上表 API（不需要反射，编译期就能查错） |
| B. 控制台注入 | ⚠️ 自建 | BepInEx 本身没有 REPL；Unity 的 `-console`/`-logFile` 无法执行脚本。若想要"免插件"体验，可在插件里自带一个命令通道（无意义），**故不做** |
| C. 内存读写 | 不必 | 有干净的托管 API，没必要逆向指针链 |
| D. 存档编辑 | ✅ 可行 | BinaryFormatter + 反序列化成真实类型（net48 工程）。适合"离线批量改"和"改坏了救档" |

**最终方案：A 为主 + D 作为补充**（和 DOS2 项目同构：插件负责运行时，存档工具负责离线）。

## 6. 复用清单（来自 EoC Trainer）

| 需要 | 直接搬运 | 改动 |
|---|---|---|
| 文件桥 + 命令去重 | `Core/Bridge.cs`、`LenientJson.cs`、`models` | 协议不变；游戏侧换成 C# 实现（可复用 `bridge-protocol.md` 的伪码） |
| WinForms 界面 | `MainForm.cs` 的布局与交互模式 | 功能页换成 GK2 的资源/天赋/科技；枚举表改数据源 |
| 枚举/清单生成 | `generate_enums.py` 的思路 | 数据源改为从反编译结果提取（本报告第 4 节） |
| 假游戏端 | `tools/fake_game.py` | 改为 .NET 版假宿主（或继续用 Python 写 `state.json`） |
| 截图/发布脚本 | `capture-screenshots.ps1`、`publish.ps1`、`publish-via-api.ps1`、`create-release.ps1` | 改名即可 |
| 安装/卸载 | `ModDeployer.cs` 的思路 | 改为"投放 BepInEx 插件 dll + 配置"；BepInEx 自带 `LogOutput.log` |
| 不需要 | `ConsoleApi/GameConsole/ConsoleChannel`、`ModPackage`（pak 打包） | GK2 用不到 |

## 7. 验证结果（2026-09-24 真机实测）

| # | 待验证 | 结果 | 证据 |
|---|---|---|---|
| 1 | BepInEx 5.4.23.5 + Doorstop 4.5 能否带起 Unity 6000.3.9 | ✅ **可以** | `BepInEx/LogOutput.log`: `Running under Unity v6000.3.9.8034645` → `Chainloader startup complete` |
| 2 | `MainGame.PlayerData` 可用、写入立即生效 | ✅ 可用 | 读：`money=1006`；写：`addRes money +1000` → 回读 `2006` |
| 3 | `SetRes` 的副作用 | ✅ 无异常 | `SetRes tech_red 291→500` 成功；资源系统自带 min/max（如 `tech_*` 上限 999）会被游戏自己约束 |
| 4 | 科技点资源 id | ✅ 确认 | `tech_red/green/blue`，值 291/283/66，上限 999 |
| 5 | 天赋/科技解锁 id 来源 | ⏳ 未做 | 后续要时再看 `GameBalance` |
| 6 | 背包精确增删 | ⏳ 未做 | `Inventory` 公开方法待查 |
| 7 | 存档反序列化（net48） | ⏳ 未做 | 只在需要离线编辑器时做 |
| 8 | 游戏更新后类型变动 | ⏳ 长期 | 状态里已带 `saveVersion`，便于日后判断 |

**额外验证到的**：

- `UnityPlayer.dll` 导入了 `WINHTTP.dll` → Doorstop 的 `winhttp.dll` 代理会被加载（这也解释了为什么入口能成立）；
  备选代理名还有 `VERSION.dll` / `WINMM.dll` / `dxgi.dll`（都在导入表里）。
- 桥接文件落在 `%USERPROFILE%\AppData\LocalLow\Lazy Bear Games\Graveyard Keeper 2\Trainer\`
  （`Application.persistentDataPath`，与存档同目录）。
- 插件体积 15 KB（零第三方依赖，`Newtonsoft.Json` 直接用游戏自带的）。
- 命令去重有效：命令执行后 `command.json` 被删除，`(token, seq)` 写回 `state.json`。

**结论**：通道 A（BepInEx 插件）+ 文件桥是该游戏的正确选择，且已验证可用。


## 8. 下一步（建议的实施顺序）

1. **装 BepInEx**（用社区 GK2 包，或官方 5.4.23.x + Doorstop 4.5），确认 `LogOutput.log` 出现、Shift+F10 仍可用。→ 验证待验证项 1
2. **写最小插件**：只做两件事 —— ① 把 `MainGame.PlayerData.GetResInt("money")` 等值写成 `state.json`；
   ② 读 `command.json` 执行 `SetRes/AddRes`。→ 一次验证 2、3、4
3. **复用 EoC 的 WinForms 界面**，先做「金钱 / 资源 / 生命 / 能量」四页，跑通端到端
4. 再按需加：天赋、科技解锁、背包、商人金钱；以及（可选）存档编辑工具
5. 交付：插件 dll + 安装器（投放 dll、备份 BepInEx 配置）+ README/截图/Release
