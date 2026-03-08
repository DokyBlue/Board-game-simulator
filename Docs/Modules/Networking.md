# Networking 模块代码说明

## 作用
`Assets/Scripts/Networking` 负责 Unity 客户端与后端服务的数据通信。

## 关键类
- `AuthApiClient`
  - 提供注册、登录请求。
  - 统一解析认证响应。
- `LobbyApiClient`
  - 负责创建/加入/离开大厅等房间相关请求。

## 设计原则
- API 客户端仅做“请求 + 结果映射”，不承载业务流程。
- 业务流程（按钮状态、场景切换、错误文案）由上层 Controller/Manager 处理。

## 维护建议
- 所有接口地址通过 `baseUrl` 管理，便于本地、内网穿透、生产环境切换。
- 对失败响应保留后端 message，方便诊断网络和权限问题。
