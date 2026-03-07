# Unity 2D 桌游模拟器（Board Game Simulator）

本项目提供一个可扩展的 Unity 2D 桌游客户端基础框架，已实现：

1. 注册/登录系统（用户名 + 密码）。
2. 游戏选择大厅（支持后续扩展多个游戏入口）。
3. 第一个游戏：德州扑克（Texas Hold'em）基础流程与牌型结算。
4. 完整开发与使用说明文档（见 `Docs/`）。

## 目录结构

- `Assets/Scripts/Auth/`：登录注册相关逻辑。
- `Assets/Scripts/Lobby/`：游戏大厅逻辑。
- `Assets/Scripts/Poker/`：德州扑克核心逻辑（发牌、牌型判定、胜负结算）。
- `Assets/Scripts/Core/`：跨场景会话上下文。
- `Docs/`：详细设计文档与规则说明。

## Unity 场景建议

请在 Unity 中创建以下场景并加入 Build Settings：

1. `Login`：登录注册场景。
2. `GameSelection`：游戏选择大厅场景。
3. `TexasHoldem`：德州扑克对局场景。

### Login 场景挂载

- 新建空对象 `AuthRoot`，挂载：
  - `UserDataStore`
  - `AuthManager`
- `AuthManager` 绑定：
  - `usernameInput`（TMP_InputField）
  - `passwordInput`（TMP_InputField）
  - `feedbackText`（TMP_Text）
  - `dataStore`（同对象上的 `UserDataStore`）
  - `gameSelectionScene = GameSelection`
- 两个按钮分别绑定：
  - 注册按钮 -> `AuthManager.Register()`
  - 登录按钮 -> `AuthManager.Login()`

### GameSelection 场景挂载

- 新建空对象 `LobbyRoot`，挂载 `GameSelectionController`。
- 绑定 `welcomeText`（TMP_Text）。
- “德州扑克”按钮绑定 `EnterTexasHoldem()`。

### TexasHoldem 场景挂载

- 新建空对象 `TexasHoldemRoot`，挂载 `TexasHoldemGameManager`。
- 绑定三个文本：
  - `stateText`：显示胜利者/牌型/底池
  - `boardText`：显示公共牌
  - `playerText`：显示玩家手牌与筹码
- 可新增“下一局”按钮绑定 `StartNewRound()`。

## 当前实现范围

- 已实现完整基础循环：建桌 -> 发底牌 -> 盲注 -> 公共牌 -> 牌型比较 -> 奖池分配。
- 适合作为第一版可运行原型，便于后续扩展下注轮、AI 策略、网络联机等。

## 后续建议

- 接入后端账号系统（数据库 + Token 登录）。
- 完整下注回合（Preflop/Flop/Turn/River），支持 Call/Raise/Fold。
- UI 牌面精灵化（卡牌图片、筹码动画、行动提示）。
- 真多人联网（Netcode for GameObjects / Mirror / Photon）。

