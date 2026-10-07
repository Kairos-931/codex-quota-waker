# 更新重启后仍执行唤醒任务

- Status: completed
- Priority: high
- Target version: 0.9.0
- Confirmed date: 2026-09-10
- Development started date: 2026-09-10
- Completed date: 2026-09-10

## Objective and user value

用户希望 Windows 更新重启、电脑处于睡眠或休眠后，即使尚未登录 Windows，计划仍能唤醒电脑并发起 Codex 请求。

已核实的故障：2026-09-10 03:24:43，Windows 按 `CodexQuotaWaker-Schedule` 的唤醒计时器恢复电脑；03:25:01，任务计划程序事件 332 因用户 `yanxu` 未登录而拒绝启动任务。更新重启造成了该登录会话缺失。休眠本身没有阻止唤醒。

## Scope

### Included

- 将目前写死的 `InteractiveToken` 运行方式改为用户可选的“即使未登录 Windows 也运行”。默认关闭，保持现有安全兼容行为。
- 开启后，在用户点击“更新并启用”时使用 Windows 原生安全凭据界面收集当前 Windows 帐户密码，并以任务计划程序的受保护凭据方式注册任务，使其可在用户注销后运行。
- 密码只在完成注册所需的内存中短暂使用；不得写进 app 配置、日志、命令行参数、任务 XML、错误消息、Git 或任何项目文件。任务计划程序自身以 Windows 加密形式保管所需凭据。
- 成功后读取已安装任务并清楚显示“无需登录也可运行”或“需要登录”；失败时保留原任务且给出下一步。
- 当用户下次更新已开启的后台运行计划时，再次要求输入密码；不得试图读取或展示已保存密码。
- 关闭该选项时，将任务恢复为当前“仅用户登录时运行”，并在界面明确警告：Windows 更新重启后可能漏跑。
- 在没有交互桌面时，如果“成功后自动睡眠”已开启，仍等待 60 秒后请求普通睡眠，但不显示不可见的取消窗；状态和日志必须注明“后台运行，无法取消”。若自动睡眠未开启、触发失败、无可靠唤醒证据或发生异常，仍不请求睡眠。
- 继续保留 WakeToRun、StartWhenAvailable、同名单一任务、现有时间线与定时触发行为。

### Not included

- 不保存或管理 ChatGPT、Codex、CC Switch 凭据，不改变模型或额度判定。
- 不绕过 Windows 密码、不以 SYSTEM 帐户运行 Codex、不修改 Windows Update 策略。
- 不自动将现有用户计划切换到后台运行；必须由用户勾选并在本机输入密码确认。
- 不用真实密码、真实注销或真实 Codex 请求作为自动化测试。

## User flow

1. 用户在“执行计划”区域勾选“即使未登录 Windows 也运行（更新重启后不漏跑）”。
2. 点击“更新并启用”，软件先说明 Windows 将加密保存当前 Windows 帐户凭据，随后显示系统凭据输入界面。
3. 输入成功后，状态摘要显示“已启用：无需登录也可运行”；取消或验证失败时，原计划保持不变并说明未修改。
4. 更新重启后，任务可按计划唤醒并运行；若没有登录会话，自动睡眠遵守后台 60 秒无取消规则。

## Data and safety boundaries

- 读取：已安装任务的 LogonType、启用状态、唤醒设置和当前用户名；不读取密码或 Codex 凭据。
- 写入：仅在用户确认并通过系统凭据验证后更新同一条 `CodexQuotaWaker-Schedule`；不得创建第二条计划。
- 失败原子性：新注册或更新失败时，恢复或保留旧任务，不能留下禁用、无动作或凭据半配置任务。

## Acceptance criteria

1. 默认和关闭状态保持 `InteractiveToken`；状态摘要及 README 明确说明更新重启后需要重新登录才能运行。
2. 选中后台运行并成功提供凭据后，任务使用适合“用户未登录仍可运行”的任务登录类型，且 `WakeToRun`、`StartWhenAvailable`、触发时间与动作不变。
3. 密码不出现在配置、日志、导出 XML、进程命令行、异常信息或 Git；取消/错误不改变原计划。
4. 已开启后台运行时再次保存会要求重新输入凭据；关闭后可靠恢复仅登录运行。
5. 后台模式下成功后的自动睡眠有明确、可测试的无界面 60 秒路径；其余睡眠安全条件不变。
6. 补充任务登录模式、配置兼容、凭据取消/失败、后台自动睡眠策略和任务 XML 不泄密的自检；`scripts/build.ps1` 与 `scripts/verify.ps1` 通过。
7. 交付时提供新版 EXE 和用户手动输入 Windows 密码、保存启用、核对状态的步骤；不自动安装用户计划、不 push。

## Development handoff

- Version impact: feat，目标 0.9.0。
- 相关代码预期包括任务注册、状态摘要、配置、自动睡眠协调器和 WinForms；开发须先更新 PRD/README，再实现。
- 若 Windows 原生凭据对话框或安全任务注册无法保证不将密码置于命令行或日志中，停止并返回具体技术证据，不以明文参数替代。

## Development execution profile

- Assessment status: approved
- Complexity and dominant cost drivers: medium；Windows Task Scheduler 安全凭据注册、旧任务原子替换、无交互睡眠和不可用密码的测试边界。
- Recommended model: gpt-5.6-luna
- Recommended reasoning effort: high
- Recommendation rationale: 涉及 Windows 安全凭据和任务调度边界，需要系统性核对；不需要更高模型做大规模架构工作。
- Lower-cost alternative and tradeoff: Luna medium；可实现界面和配置，但凭据不落盘、失败回滚和任务注册验证的漏检风险更高。
- Engineering effort range: medium，约 3–5 小时；不是交付时间承诺。
- AI usage or API cost range: 中等产品订阅用量估计；不提供 API 价格或精确 token 承诺，不含人工输入密码后的实机验证。
- Confidence: medium；假设 Windows 任务服务允许当前用户安全注册 Password Logon，且 Codex/CC Switch 的用户凭据可在已加载用户配置文件的后台任务中工作。最终真实验证需用户本机输入密码后完成。
- Escalation condition: 需要明文密码命令行、改变为 SYSTEM、绕过 Windows 安全对话框，或发现后台会话无法使用 Codex 认证时，必须停下返回产品侧。
- User decision: confirmed recommendation
- Approved model: gpt-5.6-luna
- Approved reasoning effort: high
- Approved budget or usage range: medium product-plan usage estimate; no hard token budget
- Profile approved date: 2026-09-10

## Open decisions

- None. 该电脑为用户独占，用户明确选择允许 Windows 为后台运行加密保存其 Windows 帐户凭据。
