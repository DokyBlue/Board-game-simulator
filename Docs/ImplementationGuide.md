# 开发说明文档

## 1. 总体架构

客户端采用分层模块：

- **Auth 层**：账号注册与登录（当前使用 `PlayerPrefs` 本地持久化）。
- **Lobby 层**：游戏列表展示与场景跳转。
- **Game 层（Poker）**：德州扑克核心规则与结算引擎。
- **Core 层**：全局会话（当前登录用户）传递。

## 2. 功能拆解

### 2.1 注册/登录

- 输入：用户名、密码。
- 注册：
  - 校验非空。
  - 校验用户名唯一。
  - 成功后写入本地存储。
- 登录：
  - 校验用户存在。
  - 校验密码匹配。
  - 成功后保存当前用户到 `SessionContext`。

核心脚本：

- `Assets/Scripts/Auth/UserDataStore.cs`
- `Assets/Scripts/Auth/AuthManager.cs`
- `Assets/Scripts/Core/SessionContext.cs`

### 2.2 游戏选择大厅

- 进入大厅时显示“欢迎，用户名”。
- 当前仅提供“德州扑克”入口。
- 后续可扩展更多按钮（例如：斗地主、UNO、五子棋等）。

核心脚本：

- `Assets/Scripts/Lobby/GameSelectionController.cs`

### 2.3 德州扑克

当前版本覆盖：

1. 创建牌桌（1 人 + 3 Bot）。
2. 初始化筹码与盲注。
3. 发放每位玩家两张底牌。
4. 发五张公共牌。
5. 进行 7 选 5 的最大牌型比较。
6. 分配奖池，展示赢家信息。

核心脚本：

- `Assets/Scripts/Poker/PokerCard.cs`
- `Assets/Scripts/Poker/PokerDeck.cs`
- `Assets/Scripts/Poker/PokerPlayer.cs`
- `Assets/Scripts/Poker/PokerHandEvaluator.cs`
- `Assets/Scripts/Poker/TexasHoldemGameManager.cs`

## 3. 牌型比较说明

已支持牌型（由高到低）：

1. Royal Flush（皇家同花顺）
2. Straight Flush（同花顺）
3. Four of a Kind（四条）
4. Full House（葫芦）
5. Flush（同花）
6. Straight（顺子）
7. Three of a Kind（三条）
8. Two Pairs（两对）
9. One Pair（一对）
10. High Card（高牌）

比较策略：

- 先比较 `HandRank`。
- 若牌型相同，按踢脚（Kicker）列表逐位比较。

## 4. 扩展路线

### 4.1 多游戏扩展

在 `GameSelection` 场景中新增按钮与场景映射即可。

推荐新增接口：

```csharp
public interface IGameEntry
{
    string GameId { get; }
    string DisplayName { get; }
    void Enter();
}
```

### 4.2 登录系统升级

建议将 `UserDataStore` 替换为 HTTP API：

- `POST /register`
- `POST /login`
- 返回 JWT/SessionToken

并将密码传输改为 HTTPS + 服务端哈希存储（BCrypt/Argon2）。

### 4.3 德州扑克玩法完善

建议追加：

- 完整动作系统（Check/Call/Raise/Fold/All-in）。
- 多轮下注状态机。
- 位置逻辑（庄家位、SB、BB 轮转）。
- 边池（Side Pot）处理。
- 回合计时器与超时托管。

## 5. 交付清单

- ✅ 账号注册登录功能
- ✅ 游戏选择界面
- ✅ 德州扑克第一版实现
- ✅ 详细说明文档

