# 后台运行密码与错误引导改进

- Status: completed
- Priority: high
- Target version: 0.10.1
- Confirmed date: 2026-09-22
- Queued date: not queued
- Development started date: 2026-09-22
- Completed date: 2026-09-22

## Objective and user value

- User goal: 不处理或不清楚本地账户密码时，也能理解该选项是否必要，并知道失败的真实原因和可执行的下一步。
- Current problem: 界面没有说明 Windows Hello PIN 不能用于任务计划的密码登录；注册失败时程序只显示统一的“确认密码”提示并仅记录 `TargetInvocationException`，掩盖了 Windows 底层错误。
- User-visible outcome: 用户在勾选前就知道“普通睡眠不需要开启、重启或注销后无人登录才需要”，凭据窗口明确要求账户密码而非 PIN；失败时按原因给出具体处理方式，原计划保持不变。

## Scope

### Included

- 将勾选项文案改为更直接的“重启或注销后仍运行”。
- 在勾选项附近常驻简短说明：“普通睡眠唤醒不需要；开启后保存时需输入当前 Windows 帐户密码，开机 PIN 不可用。”
- 凭据窗口显示当前账户名称，并明确写明“请输入账户密码，不能使用 PIN、指纹或人脸”。
- 递归解包 `TargetInvocationException` 等包装异常，提取底层 `COMException`/HRESULT 或 Win32 错误码。
- 至少映射以下错误并提供下一步：凭据错误 `0x8007052E`；未授予批处理登录权限 `0x80070569`；访问被拒绝 `0x80070005`；其他未知错误显示可复制的错误码。
- 日志仅记录异常类型、HRESULT/Win32 错误码和安全的阶段名称，不记录密码、PIN、Cookie、令牌或凭据内容。
- 保持现有失败原子性：取消或注册失败时，不保存本次勾选状态、不替换原任务、不改变原计划。
- 增加纯逻辑自检，覆盖异常解包、错误码映射、PIN提示文案与敏感信息不落盘边界。

### Not included

- 不替用户查找、设置或重置 Windows 账户密码。
- 不接受 PIN 作为任务计划凭据，也不尝试绕过 Windows 验证。
- 不自动提升权限、不修改“作为批处理作业登录”等本地安全策略。
- 不改变后台运行的任务注册方式、Codex 认证、唤醒和自动睡眠逻辑。
- 不自动修改或安装用户现有计划任务，不执行真实凭据验证。

## User flow

1. 用户看到“重启或注销后仍运行”及常驻说明，知道普通睡眠场景无需开启。
2. 用户暂时不处理账户密码时保持未勾选，照常保存仅登录运行的计划。
3. 用户未来需要该能力时勾选并保存；凭据窗口展示当前账户并要求账户密码，明确排除 PIN。
4. 注册失败时，程序显示具体原因、错误码（适用时）、原计划未修改及下一步；用户可取消勾选继续使用普通模式。

## Interaction specification

- Entry point: 现有后台运行勾选项、其下方说明文字、Windows 凭据输入窗口和失败消息框。
- Default state: 选项保持默认关闭；说明文字始终可见，不要求用户先失败一次才理解。
- Actions and feedback: 勾选本身不弹窗、不改计划；点击保存时才打开凭据窗口。失败提示首句给原因，末句给下一步并确认原计划未修改。
- Responsive behavior: 说明文字允许换行，主窗口继续遵守现有滚动规则，不挤压计划时间线和最近结果区域。
- Demo or visual references: 无需独立演示；遵循现有 WinForms 视觉样式。

## Data and safety boundaries

- Reads: 当前 Windows 账户显示名；注册失败返回的异常类型和错误码。
- Writes: 仅在凭据注册成功后沿用现有逻辑保存配置并更新同一条计划任务。
- Must not do: 不记录、回显、持久化或复制密码/PIN；不把异常对象完整文本直接写入日志；不修改账户或安全策略。
- Effect on original files or external systems: 仅界面说明和诊断改善；失败路径对原任务与配置无副作用。

## Failure and edge cases

- Empty state: 用户不勾选时无需密码，继续按“仅登录时运行”保存。
- Invalid input: 空密码沿用明确提示；PIN被 Windows 判为错误凭据时映射为“账户密码不正确或输入了 PIN”。
- Missing path or unavailable service: 任务计划组件不可用时保持现有专用提示，不误报为密码错误。
- Failure message and next action: `0x8007052E` 提示使用当前账户完整密码、PIN不可用，也可取消勾选继续；`0x80070569` 提示账户缺少批处理登录权限且软件未修改策略；`0x80070005` 提示权限被拒绝并建议以管理员身份启动后重试；未知错误显示代码并引导查看日志。
- Nested exceptions: 无论底层异常被反射包装一层或多层，都应定位最具体的 HRESULT，不再只显示 `TargetInvocationException`。

## Acceptance criteria

1. Given 用户首次打开 0.10.1，then 后台选项显示“重启或注销后仍运行”，旁边常驻说明普通睡眠无需开启且 PIN 不可用。
2. Given 用户未勾选该项，when 保存计划，then 不要求密码并保持现有仅登录运行行为。
3. Given 用户勾选并保存，then 凭据窗口显示当前账户，并明确要求账户密码、不能使用 PIN/指纹/人脸。
4. Given 注册底层返回 `0x8007052E`，then 提示“账户密码不正确或输入了 PIN”、原计划未修改及可取消勾选继续使用；日志包含脱敏错误码但不含凭据。
5. Given 底层返回 `0x80070569` 或 `0x80070005`，then 分别显示批处理登录权限或访问被拒绝的具体下一步，不误报为密码错误。
6. Given 未识别 HRESULT，then 显示可复制错误码和日志入口，原计划与已保存配置保持不变。
7. Given 包装异常包含一层或多层 `TargetInvocationException`，then 自检证明程序能提取底层错误码；日志和所有持久化文件均不包含测试密码、PIN或其他秘密。
8. `scripts/build.ps1` 和 `scripts/verify.ps1` 通过；不得用真实密码、真实计划更新或账户策略修改作为自动测试。

## Development handoff

- Version impact: fix，基于当前 0.10.0 发布 0.10.1
- Relevant modules: `src/CodexQuotaWaker.cs`，以及 README/PRD/自检和版本字段
- Required verification: 项目正式构建、验证脚本及错误映射/异常解包/不泄密自检
- Deployment or desktop update: 生成新版单文件 EXE；不自动安装、不更新用户计划
- Git and push constraints: Conventional Commit `fix:`；发布时同步源码版本与 `v0.9.1` 标签；未经明确要求不得 push

## Development execution profile

- Assessment status: approved
- Assessed requirement date or revision: 2026-09-22 handoff revision; behavior unchanged, target rebased from 0.9.1 to 0.10.1
- Complexity and dominant cost drivers: small-to-medium; WinForms 文案布局较小，主要风险在反射包装异常的递归解包、HRESULT/Win32映射和日志脱敏回归
- Recommended model: gpt-5.6-luna
- Recommended reasoning effort: max
- Recommendation rationale: 涉及 Windows 凭据错误、安全提示和不泄密边界，代码量不大但错误映射必须可靠
- Lower-cost alternative and tradeoff: gpt-5.6-luna / medium；可完成界面文案，但更可能遗漏多层异常或错误码/日志安全边界
- Engineering effort range: 1–2 小时，包括实现、纯逻辑自检、正式构建和文档/版本更新
- AI usage or API cost range: low-to-medium product-plan usage; approximately one implementation pass plus diagnostic/test iteration, no currency estimate or hard quota
- Estimate basis and excluded costs: 基于现有 0.9.0 实现和单文件 C# 项目；不含用户真实密码验证、账户重置、安全策略修改、计划安装和人工跨重启验收
- Confidence: medium-high
- Assumptions and unknowns: 常见 HRESULT 在该 Windows 版本上保持标准值；凭据框支持现有自定义说明；当前未提交 0.9.0 代码是合法基线且将被保留
- Escalation condition: 若必须记录原始异常文本才能诊断、需要修改账户/安全策略、或发现错误码无法稳定区分且会误导用户，停止并返回需求侧决策
- User decision: confirmed override
- Approved model: gpt-5.6-luna
- Approved reasoning effort: max
- Approved budget or usage range: low-to-medium product-plan usage estimate; no hard token or currency commitment
- Profile approved date: 2026-09-22

## Open decisions

- None
