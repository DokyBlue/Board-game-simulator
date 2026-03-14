# Backend 模块代码说明

## 作用
`Backend/` 提供账号与大厅服务端能力（Node.js + MySQL + JWT），为 Unity 客户端提供 REST API。

## 典型职责
- 用户注册与登录。
- Token 签发与鉴权。
- 房间创建/加入/离开等大厅相关接口。

## 维护建议
- SQL 结构变更先更新 `Backend/sql/schema.sql`。
- 新增接口时同步更新 Unity 侧 `AuthApiClient`/`LobbyApiClient` 对应契约。
