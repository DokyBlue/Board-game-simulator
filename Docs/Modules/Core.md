# Core 模块代码说明

## 作用
`Assets/Scripts/Core` 负责维护会话级共享状态，避免场景切换后丢失用户登录态和房间上下文。

## 关键类
- `SessionContext`
  - 保存当前用户、JWT、房间 ID、房主标识、房间码等运行时信息。
  - 提供统一清理入口（例如离开房间或登出时重置状态）。

## 调用关系
- `AuthManager` 登录成功后写入 `SessionContext`。
- `GameSelectionController`、`TexasHoldemGameManager` 读取当前用户和房间信息。
- `TexasHoldemGameManager` 离开房间时调用清理逻辑并返回大厅。

## 维护建议
- `SessionContext` 只放“会话级临时状态”，不要放持久化配置。
- 所有字段建议通过统一方法更新，避免跨模块直接写导致状态不一致。
