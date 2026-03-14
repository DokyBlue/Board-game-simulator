# Poker 模块代码说明

## 作用
`Assets/Scripts/Poker` 实现德州扑克核心玩法：发牌、下注轮次、Bot 决策、结算。

## 关键类
- `TexasHoldemGameManager`
  - 管理整局生命周期（Preflop/Flop/Turn/River/Showdown）。
  - 处理人类玩家操作（Call/Raise/AllIn/Fold）。
  - 处理 Bot 决策与动作广播。
  - 维护底池、玩家筹码、最近动作、操作历史。
  - 维护“Bot 策略面板”并持续显示各 Bot 风格与最近动作。
- `PokerPlayer`
  - 玩家状态模型（筹码、下注额、是否弃牌、是否全下等）。
- `PokerDeck` / `PokerCard`
  - 牌库与卡牌结构。
- `PokerHandEvaluator`
  - 手牌评估（牌型比较）。

## 本次补充能力
1. **Prompt 持续显示 Bot 策略**：每次动作后刷新策略快照，显示 Bot 风格 + 最近动作。
2. **可查询操作历史**：支持通过关键字筛选玩家/Bot历史动作，并显示最近记录。

## 维护建议
- 新增行动类型时，同步更新：人类动作处理、Bot 动作处理、历史记录、策略刷新。
- 若后续支持联机同步，可把 `_actionHistory` 和 `_lastActions` 透传到网络层。
