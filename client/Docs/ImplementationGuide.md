# 开发实现文档（升级版）

## 1. 功能完成情况

1. ✅ 接入后端账号系统（MySQL + JWT Token）。
2. ✅ 德州扑克完整下注街（Preflop/Flop/Turn/River）。
3. ✅ 玩家操作（Call / Raise / Fold）。
4. ✅ 单人模式（1 玩家 + 5 Bot）。
5. ✅ Bot 统一策略（自动跟注 + 摊牌比较）。
6. ✅ UI 牌面精灵化（卡牌、筹码动画、行动提示）。
7. ✅ 基础美术资源可直接使用。

## 2. 后端设计

- 技术栈：Node.js + Express + MySQL + JWT。
- 关键接口：
  - `POST /auth/register`
  - `POST /auth/login`
  - `GET /auth/me`
  - `GET /health`
- 密码：BCrypt 哈希保存（`password_hash`）。
- Token：登录/注册后返回 JWT，客户端存于 `SessionContext.AccessToken`。

关键文件：

- `Backend/src/index.js`
- `Backend/sql/schema.sql`
- `Backend/.env.example`

## 3. Unity 客户端设计

### 3.1 认证流程

- `AuthApiClient`：使用 `UnityWebRequest` 调后端接口。
- `AuthManager`：触发注册/登录并处理返回结果。
- 登录成功后写入 `SessionContext`：`UserId / CurrentUser / AccessToken`。

### 3.2 德州扑克流程

`TexasHoldemGameManager` 中实现：

- 建桌：1 名本地玩家 + 5 名 Bot
- 发牌与盲注
- 四个下注街：
  - Preflop
  - Flop
  - Turn
  - River
- 玩家决策：
  - Call：补齐到当前最大下注
  - Raise：先跟注再追加固定加注额
  - Fold：本局弃牌
- Bot 决策：
  - 始终自动 Call
- Showdown：调用 `PokerHandEvaluator` 比较牌型分配底池

### 3.3 UI 与美术

- `CardSpriteLibrary`：按 `rank_suit` 命名查找牌面精灵。
- `CardView`：控制单张牌的正反面显示。
- `ChipAnimator`：底池数字过渡动画。
- `ActionPromptView`：操作提示与行为反馈。

资源目录：

- `Assets/Art/Cards/`
- `Assets/Art/UI/`

## 4. 扩展建议

- 完善边池（Side Pot）和多人平分池。
- Bot 策略从固定跟注升级为基于底池赔率的决策。
- 为筹码移动增加轨迹动画、音效。
- 接入排行榜与玩家历史对局记录。

