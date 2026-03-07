# Unity 2D 桌游模拟器（可直接运行原型）

本仓库已升级为**可直接运行的单人德州扑克原型**，包含：

- 后端账号系统（Node.js + MySQL + JWT Token）
- Unity 客户端注册/登录（调用后端接口）
- 游戏大厅（可扩展多游戏入口）
- 德州扑克完整下注街：Preflop / Flop / Turn / River
- 玩家操作：Call / Raise / Fold
- 1 名本地玩家 + 5 名 Bot（Bot 固定策略：自动跟注）
- 基础 UI 美术资源（卡牌、筹码、行动按钮、桌布）

---

## 1. 快速启动

### 1.1 启动后端（MySQL + Token）

1. 创建数据库表：
   - 执行 `Backend/sql/schema.sql`
2. 配置环境变量：
   - 复制 `Backend/.env.example` 为 `Backend/.env` 并填写 MySQL/JWT
3. 启动服务：

```bash
cd Backend
npm install
npm start
```

默认监听 `http://127.0.0.1:8080`。

### 1.2 Unity 客户端场景

请建立并加入 Build Settings：

1. `Login`
2. `GameSelection`
3. `TexasHoldem`

---

## 2. 场景绑定说明

### Login 场景

在 `AuthRoot` 挂载：

- `AuthManager`
- `AuthApiClient`

`AuthManager` 绑定：

- `usernameInput`（TMP_InputField）
- `passwordInput`（TMP_InputField）
- `feedbackText`（TMP_Text）
- `authApiClient`
- `gameSelectionScene = GameSelection`

按钮事件：

- 注册 -> `AuthManager.Register()`
- 登录 -> `AuthManager.Login()`

### GameSelection 场景

在 `LobbyRoot` 挂载 `GameSelectionController`，绑定 `welcomeText`。

### TexasHoldem 场景

在 `TexasHoldemRoot` 挂载 `TexasHoldemGameManager`，绑定：

- 文本：`stateText`、`boardText`、`playersText`
- 按钮：`callButton`、`raiseButton`、`foldButton`
- 视图：`ActionPromptView`、`ChipAnimator`
- 牌面：2 个玩家手牌 `CardView` + 5 个公共牌 `CardView`
- 牌库：`CardSpriteLibrary`（将 `Assets/Art/Cards/` 资源拖入）

---

## 3. 目录结构

- `Assets/Scripts/Auth/`：登录控制
- `Assets/Scripts/Networking/`：后端 API 调用
- `Assets/Scripts/Lobby/`：大厅逻辑
- `Assets/Scripts/Poker/`：德州扑克完整流程
- `Assets/Scripts/UI/`：卡牌显示、筹码动画、行动提示
- `Assets/Art/`：基础美术资源
- `Backend/`：Node + MySQL + JWT 登录服务
- `Docs/`：详细技术文档

