# Mine-Deck Strategy

一款把双人竞技、扫雷和技能卡组组合在一起的 Unity 策略游戏。两名玩家通过 Unity Relay 联机，在同一张棋盘上争夺区域、积累能量，并使用防御塔、陷阱、治疗、侦查和干扰类卡牌建立通路、击败对手。

> 当前状态：可编译、可联机游玩的功能型 Alpha。核心战斗与 9 张技能卡已经接入，并已通过 Windows 双进程联机与异常断线回归；仍需继续进行公网 Relay 双设备回归、数值平衡和正式发布打包。

## 项目亮点

- 服务端权威战斗：回合、血量、能量、地图状态和技能效果由 Host 统一结算。
- 卡组安全校验：服务器校验玩家携带卡牌、技能费用、冷却和目标坐标，拒绝伪造 RPC。
- 双人在线联机：使用 Netcode for GameObjects、Unity Transport、Relay 和匿名认证。
- 数据驱动卡牌：用 `ScriptableObject` 配置技能类型、费用、冷却、说明和图标。
- 战术扫雷机制：包含领地争夺、战争冷却、焦土耐久、通路伤害、防御塔、陷阱和私有旗标。
- 在线房间列表：通过 Supabase REST 保存房间号、房间名、状态和人数。
- 自动化校验：本地 Unity 场景校验器与 GitHub Actions 仓库完整性检查。

## 技术栈

| 范畴 | 技术 |
| --- | --- |
| 引擎与语言 | Unity 2022.3 LTS、C# |
| 联机 | Netcode for GameObjects、Unity Transport、Unity Relay |
| 云服务 | Unity Authentication、Supabase REST |
| 游戏表现 | 2D Tilemap、UGUI、TextMesh Pro、ScriptableObject |
| 工程化 | Unity Batch Mode、PowerShell、GitHub Actions |

## 运行方式

1. 使用 Unity Hub 添加仓库目录，并用 Unity `2022.3` LTS 打开。
2. 在 Unity Dashboard 中启用 Authentication 和 Relay，并把项目关联到对应的 Cloud Project。
3. 打开 `Assets/Scenes/Network_Playground.unity`。
4. 在场景的 `LobbyManager` 上配置 Supabase Project URL 和 anon public key。
5. 确认 Supabase 中存在 `rooms` 表，字段至少包含 `join_code`、`room_name`、`status`、`player_count` 和 `created_at`。
6. 构建一个 Windows 或 WebGL 客户端；一端创建房间，另一端通过房间列表或 Join Code 加入。

主场景已经加入 Build Settings。桌面端 Relay 使用 DTLS，WebGL 自动改用 WSS。

## 操作与规则

- 鼠标左键：翻开格子；选中卡牌后改为对目标释放技能。
- 鼠标右键：放置或取消自己的私有旗标。
- 按住鼠标中键：高亮数字格周围可联动翻开的区域。
- 双方各自选择 1–4 张卡并准备后开始战斗。
- 翻格和占领可获得能量，卡牌消耗、冷却和合法性均由服务器判断。
- 建立从己方起点到敌方终点的连续通路会对对方造成伤害。

## 自动验证

仓库静态检查：

```powershell
pwsh -File .\Tools\Validate-Repository.ps1
```

Unity 场景、组件引用和卡牌配置检查：

```powershell
$Unity = '你的 Unity.exe 绝对路径'
& $Unity -batchmode -nographics -quit `
  -projectPath (Get-Location).Path `
  -executeMethod MineDeckProjectValidator.ValidateBatch `
  -logFile '.\Logs\project-validation.log'
```

也可以在编辑器中使用 `Tools > Mine-Deck > Validate Project`。GitHub Actions 会在每次 push 和 pull request 时自动执行仓库静态检查。

项目纹理可以通过编辑器菜单 `Tools > Mine-Deck > Optimize Textures`，或在批处理模式下调用 `MineDeckTextureOptimizer.OptimizeBatch` 统一优化。当前规则会保留 2048 上限的全屏背景，把技能图标、职业图标和棋盘/UI 精灵分别限制到适合其显示尺寸的分辨率，并为 Standalone 与 WebGL 启用压缩。一次 Windows Development Build 实测从 222.22 MB 降至 156.08 MB，纹理构建占用从 102.7 MB 降至 36.7 MB。

UI 字体可以通过 `Tools > Mine-Deck > Optimize UI Font`，或批处理方法 `MineDeckFontOptimizer.OptimizeBatch` 重新收集当前场景、卡牌和运行时代码所需字符。生成的 `MineDeck UI SDF` 使用单张 2048 图集预热当前文案，同时保留动态中文扩展能力。字体优化后同一 Windows Development Build 从 156.08 MB 进一步降至 130.58 MB。

Development Build 还提供双实例端到端回归入口。先构建测试客户端：

```powershell
$Unity = '你的 Unity.exe 绝对路径'
$env:MINE_DECK_E2E_BUILD_PATH = (Join-Path (Get-Location) 'Builds\E2E\MineDeckStrategyE2E.exe')
& $Unity -batchmode -nographics -quit `
  -projectPath (Get-Location).Path `
  -executeMethod MineDeckBuildAutomation.BuildWindowsDevelopment `
  -logFile '.\Logs\e2e-build.log'
```

然后用两个独立进程分别传入 `--mine-deck-e2e-host <共享目录>` 和 `--mine-deck-e2e-client <共享目录>`。默认走真实 Relay；追加 `--mine-deck-e2e-local` 可改用本机 `127.0.0.1:7777`，用于在没有 Relay 服务时验证连接、准备、开局、回合交替、揭示、能量同步和卡牌扣费。测试结果会写入共享目录中的 `host.done`、`client.done` 或对应的 `.failed` 文件。

## 目录结构

```text
Assets/
  Editor/              Unity 项目校验器
  Gamedata/Cards/      9 张 CardData 卡牌资产
  Scenes/              主游戏场景
  Scripts/             战斗、联机、卡组、房间和 UI 逻辑
  Art/                 图标、棋盘和卡牌美术
Packages/              Unity 包依赖
ProjectSettings/       Unity 项目与构建设置
Tools/                 本地和 CI 校验脚本
```

## 安全说明

Supabase anon key 是面向客户端的公开密钥，不应把 `service_role` key 或其他管理密钥放进 Unity、场景或仓库。正式环境必须启用 Row Level Security，并只开放房间列表所需的最小权限；如果需要可靠的房主身份校验、房间删除或防刷，应把写操作迁移到受控后端或 Edge Function。

## 已知限制

- 当前是 Host 权威的双人房间，不是专用服务器架构。
- P2 掉线会终止当前对局并回到等待状态，暂不支持恢复原对局。
- Supabase 房间记录还需要过期清理和更严格的服务端所有权校验。
- 本机双实例回归不能替代两个物理设备、不同网络环境下的 Relay 延迟、丢包和重连测试。
