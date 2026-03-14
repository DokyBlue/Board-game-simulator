# Auth 模块代码说明

## 作用
`Assets/Scripts/Auth` 负责账号登录与注册流程，并把认证结果写入会话上下文。

## 关键类
- `AuthManager`
  - 读取用户名/密码输入框。
  - 调用 `AuthApiClient` 执行注册、登录。
  - 根据结果更新 UI 提示并跳转场景。
- `UserDataStore`
  - 本地保存用户数据（例如最近登录用户信息）。

## 交互流程
1. 用户输入凭据。
2. `AuthManager` 调用 `AuthApiClient`。
3. 成功后写入 `SessionContext`。
4. 跳转到 `GameSelection` 场景。

## 维护建议
- UI 校验（空值、长度、格式）应在 `AuthManager` 先行拦截。
- API 错误提示保持统一口径，便于国际化和排错。
