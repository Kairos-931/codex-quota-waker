# 额度窗口服务端验证

- Status: draft
- Priority: P0
- Target version: 0.5.1
- Confirmed date: not confirmed

## Objective and user value

- User goal: 每次自动触发后，直接知道请求是否计入 ChatGPT Codex 五小时额度窗口，而不是根据一句模型回复猜测。
- Current problem: 程序只以 `codex exec` 退出码为 0 且有文本输出作为成功条件，既不校验请求实际使用的 provider，也无法区分“新窗口已开启”“请求落在现有窗口”“请求成功但额度状态无法确认”。因此请求可能成功触发另一条认证/转发链路的额度窗口，却被误报为目标窗口成功。
- User-visible outcome: 最近结果和日志同时展示实际 provider、模型请求状态与服务端额度验证状态，包括额度桶、五小时窗口重置时间、是否新开窗口，以及 provider 不匹配或无法确认时的下一步。

## Diagnosis evidence

- 2026-09-05 16:13:03 的计划任务使用 Codex CLI 0.153.4 执行成功；对应会话 ID 为 `01a070a0-e04b-7092-9bc0-4a24969c6e7e`。
- 该会话的 `session_meta` 明确记录 `model_provider=openai`、`originator=codex_exec`，并非 `cc-switch-official`。服务端返回 `limit_id=codex`、`plan_type=plus`、五小时窗口 `resets_at=2026-09-05 21:13:17 +08:00`。
- CC Switch 日志显示直到 17:27:10 才“接管 Codex Live 配置”，同时 `%USERPROFILE%\.codex\config.toml` 与 `auth.json` 被重写；17:29:41 才出现接管后的第一条代理请求。
- 17:30 后桌面客户端会话的 `session_meta` 记录 `model_provider=cc-switch-official`，其服务端额度事件为 `used_percent=5`、`resets_at=2026-09-05 22:29:55 +08:00`，与 CC Switch 和 ChatGPT 客户端显示一致。
- 结论：16:13 请求确实触发了一个五小时窗口，但触发的是当时的 `openai` 直连链路，不是 17:27 后启用的 CC Switch Live 链路。目标链路的窗口实际由 17:29 的请求开启，所以显示 22:29 是正确的；这不是单纯的 Dashboard 显示问题。
- 现有历史文件无法证明 17:27 覆盖前的认证身份是否与当前账号完全相同，因此不得进一步声称两个窗口属于同一额度桶。

## Scope

### Included

- 调用 `codex exec --json`，捕获本次运行的 thread/session ID 和结构化事件，同时保留最终模型回复。
- 增加“目标请求链路”配置；本次产品默认选择并锁定 `cc-switch-official`，同时保留“跟随当前 Codex 配置”作为可选模式。
- 锁定 CC Switch 时，在发送请求前检查当前 provider 配置和 `http://127.0.0.1:15721` 是否可用；短暂不可用时在现有保持清醒期限内等待，仍不可用则明确失败，不静默退回 `openai` 直连。
- 锁定 CC Switch 时，通过 Codex CLI 的显式配置参数指定目标 provider；运行后再从本次会话 `session_meta.model_provider` 反向验证，只有实际 provider 匹配才允许判定目标窗口成功。
- 从本次会话的额度事件中读取 `limit_id`、五小时窗口分钟数、`used_percent`、`resets_at` 和计划类型；开发者先确认 `--json` 是否直接包含所需事件，若不包含，再凭 thread ID 定位该会话文件读取最终 `token_count.rate_limits`。
- 将请求结果拆成两个状态：`Codex 请求成功/失败` 与 `额度窗口已确认/使用现有窗口/无法确认`。
- 当 `limit_id=codex` 且五小时窗口重置时间约等于本次请求后五小时时，显示“新五小时窗口已触发”。
- 当额度元数据存在但重置时间表明窗口已在运行时，显示“请求已计入现有五小时窗口”，不得声称新开窗口。
- 当模型回复成功但缺少 `codex` 额度元数据时，显示黄色警告“请求成功，但额度状态未确认”，不得记录为“额度触发完成”。
- 当模型回复成功但实际 provider 与目标 provider 不一致时，显示红色“请求发往非目标链路，目标额度窗口未确认”，并同时展示目标 provider 和实际 provider。
- 最近结果和日志展示本地时间格式的窗口重置时间，并解释 `used_percent=0` 可能来自百分比取整。
- 修复 Codex 标准输出的 UTF-8 解码，消除日志中的中文乱码。
- 使用固定 JSONL 测试样本覆盖新窗口、现有窗口、无额度元数据和格式异常，不通过测试发送真实模型请求。

### Not included

- 不调用或抓取 Usage Dashboard 的私有接口。
- 不修改 CC Switch 的账号、Token 或已有 provider 定义；只读取当前接管状态并通过 Codex CLI 选择已经存在的 provider。
- 不承诺第三方代理永远保持内部事件格式兼容。
- 不自动重新登录、切换账号或消耗额度重置券。
- 不更改现有唤醒时间、计划任务名称或触发序列。

## User flow

1. Windows 计划任务到点运行，程序先确认目标 provider 和 CC Switch 本地代理可用。
2. 程序通过目标 provider 执行一次 Codex 请求，并反向验证实际 provider。
3. 程序获得模型结果后读取同一次运行的服务端额度元数据。
4. 最近结果显示请求是否成功、实际请求链路，以及额度是“新窗口”“现有窗口”还是“未确认”。
5. 用户无需依赖 Dashboard 的即时刷新；若链路不匹配或额度未确认，界面提供检查 CC Switch 接管状态、登录账号和日志的下一步。

## Interaction specification

- Entry point: 主窗口“最近一次结果”和活动日志。
- Default state: 未运行时显示“暂无额度验证记录”。
- Actions and feedback: 成功确认使用绿色；请求成功但额度未确认使用黄色；请求失败使用红色。
- Responsive behavior: 新增字段不得破坏现有可滚动布局和可拖动分隔线。
- Demo or visual references: 延续 v0.5.0 原生 Windows 风格，不新增独立页面。

## Data and safety boundaries

- Reads: 本次 `codex exec --json` 输出；必要时只读取与 thread ID 精确匹配的本地 Codex 会话 JSONL。
- Writes: `%LOCALAPPDATA%\CodexQuotaWaker\last-run.json` 和现有活动日志，新增非敏感额度元数据字段。
- Must not do: 不读取、打印或写入 `auth.json` Token；不记录账号 ID、Cookie、API Key 或完整授权头。
- Effect on original files or external systems: 只增加诊断记录；计划任务与 CC Switch 配置保持不变。

## Failure and edge cases

- Empty state: 没有额度事件时显示“额度状态未确认”，但保留模型回复。
- Invalid input: JSONL 某一行异常时跳过该行并继续寻找同一 thread 的最终额度事件。
- Missing path or unavailable service: 找不到会话文件时不得扫描或输出其他会话内容，只记录本次 thread ID 和未确认原因。
- Failure message and next action: 指引用户核对 `codex login status`、CC Switch 当前启用账号，并稍后刷新 Usage Dashboard。
- Existing active window: 明确显示“计入现有窗口”，避免把重置时间未变化误报为失败。
- Rounded percentage: `used_percent=0` 但存在有效五小时重置时间时，仍可确认窗口，并解释百分比取整。
- Provider changed after schedule save: 每次执行都重新检查目标 provider，不依赖保存计划时的临时配置。
- CC Switch unavailable after wake or boot: 等待本地代理恢复；超时后失败并提示启动或重新接管 CC Switch，不得自动改走直连。

## Acceptance criteria

1. Given 目标 provider 为 `cc-switch-official` 且本地代理可用，when 计划执行，then 本次会话 `session_meta.model_provider` 必须为 `cc-switch-official`，并在结果中展示实际 provider。
2. Given 实际 provider 与目标 provider 不一致，when 模型回复成功，then 不得显示目标额度触发成功，必须显示“请求发往非目标链路，目标额度窗口未确认”。
3. Given 服务端返回 `limit_id=codex` 且重置时间约为请求后五小时，when provider 校验也通过，then 界面与日志显示“新五小时窗口已触发”和准确的本地重置时间。
4. Given 服务端返回有效的现有五小时窗口，when 运行完成，then 显示“请求已计入现有五小时窗口”，不声称窗口失败或新开。
5. Given 模型回复成功但没有有效额度元数据，when 运行完成，then 请求状态为成功、额度状态为未确认，并提供下一步。
6. Given `used_percent=0.0` 且五小时重置时间有效，when 展示结果，then 不把 0% 解释为未触发。
7. Given 输出包含中文，when 写入 `last-run.json` 和日志，then 内容为正确 UTF-8，不出现乱码。
8. Given 固定 JSONL 测试样本，when 运行验证脚本，then 新窗口、现有窗口、无额度元数据、格式异常和 provider 不匹配场景全部通过且不发送真实 Codex 请求。
9. Given 用户现有计划已启用，when 安装 0.5.1，then 任务名称、时间序列和 WakeToRun 保持不变，且不得改写 CC Switch 的账号与 Token。

## Development handoff

- Version impact: fix（0.5.0 → 0.5.1）
- Relevant modules: developer task determines after inspection; likely `src/CodexQuotaWaker.cs` and `scripts/verify.ps1`.
- Required verification: `scripts/build.ps1`、`scripts/verify.ps1`，并验证现有计划任务未被测试覆盖。
- Deployment or desktop update: 构建单文件 EXE；若旧窗口占用产物，先引导关闭，不擅自改变计划。
- Git and push constraints: Conventional Commit 建议 `fix: verify Codex quota window metadata`；不自动 `git push`。

## Development execution profile

- Assessment status: ready for single confirmation
- Assessed requirement date or revision: 2026-09-05 / revision 2
- Complexity and dominant cost drivers: 中等；主要成本是目标 provider 锁定与反向验证、CC Switch 唤醒后可用性检查、Codex JSONL 事件兼容、同一次 thread 的精确关联、UTF-8 处理和无真实请求的测试夹具。
- Recommended model: gpt-5.6-terra
- Recommended reasoning effort: high
- Recommendation rationale: 单文件 WinForms 改动范围有限，但认证、额度语义和非公开会话落盘格式需要谨慎处理，Terra high 能兼顾可靠性与成本。
- Lower-cost alternative and tradeoff: gpt-5.6-luna high；成本更低、速度更快，但更容易遗漏 JSONL 变体和现有窗口边界，需要更多人工复核。
- Engineering effort range: 3–5 小时，包括实现、夹具测试、构建与一次不消耗额度的回归验证。
- AI usage or API cost range: ChatGPT 产品计划中的低到中等用量，预计 1–3 个集中开发/验证回合；不预计产生单独 API Key 账单。真实额度触发验收请求不包含在估算内，默认不执行。
- Estimate basis and excluded costs: 基于约 2200 行 .NET Framework WinForms 单文件、现有构建/验证脚本和本机 CLI 0.153.4；不含 OpenAI 或 CC Switch 后续协议变化、人工 Dashboard 验证和第三方服务费用。
- Confidence: medium
- Assumptions and unknowns: `codex exec --json` 能稳定提供 thread ID；Codex CLI 支持通过显式配置覆盖选择现有 provider；额度事件可直接从 JSON 输出或精确匹配的本次会话文件取得；现有 Plus 登录保持有效。
- Escalation condition: 若 CLI 无法可靠锁定 provider、`--json` 无法关联会话、额度事件格式在同版本内不稳定，或必须调用私有 Dashboard API 才能验证，开发者停止实现并返回需求任务，不得扩大读取范围或抓取凭据。
- User decision: pending
- Approved model:
- Approved reasoning effort:
- Approved budget or usage range:
- Profile approved date: not approved

## Open decisions

- None
