# UI 模块代码说明

## 作用
`Assets/Scripts/UI` 负责牌桌表现层：卡牌、筹码、行动提示等。

## 关键类
- `ActionPromptView`
  - 显示当前行动提示。
  - 新增策略快照显示能力：可独立绑定 `strategyText`，或在单文本模式下自动拼接显示。
- `ChipAnimator`
  - 执行底池变动的视觉动画。
- `CardView`
  - 负责单张牌正反面显示。
- `CardSpriteLibrary`
  - 管理卡牌贴图映射，供 `CardView` 查询。

## 维护建议
- 业务判断放在 Manager，UI 组件尽量保持“被动渲染”。
- UI 组件字段空值要容错，避免场景未绑定时直接抛异常。
