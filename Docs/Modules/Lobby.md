# Lobby 模块代码说明

## 作用
`Assets/Scripts/Lobby` 管理大厅页面：欢迎信息、模式入口、房间流程入口。

## 关键类
- `GameSelectionController`
  - 展示当前用户信息。
  - 响应进入德州场景、创建/加入房间等操作。

## 调用关系
- 读取 `SessionContext` 获取当前登录用户。
- 与 `LobbyApiClient` 配合处理房间动作。
- 进入 `TexasHoldem` 场景后由牌局管理器接管。

## 维护建议
- 大厅仅负责“导航与入口”，复杂牌局逻辑不要放入该模块。
