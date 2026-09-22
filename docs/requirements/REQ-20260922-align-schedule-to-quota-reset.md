# 测试后按官方额度重置时间调整执行计划

- Status: implemented and formally verified
- Priority: high
- Target version: `0.12.0`
- Confirmed date: `2026-09-22`

## Objective and user value

- User goal: 测试请求链路时，如果程序拿到可靠的官方额度重置时间，自动把下一轮序列的首次执行时间对齐到额度恢复之后。
- Current problem: 用户即使看到额度重置时间，也必须手工抄写到下方计划区域，容易填错日期、时区或提前一分钟执行。
- User-visible outcome: 测试完成后，程序自动更新尚未保存的首次日期和时间，并明确提醒用户仍需点击“保存并启用”才能改变 Windows 计划。

## Scope

### Included

- 在“测试请求链路”的本次真实响应中，识别带时区的、来源可靠的官方额度重置时间。
- 将重置时间转换为 Windows 当前本地时区。
- 计划输入精度为分钟时向后取整到下一整分钟。例如 `2026-09-20 23:08:42 +08:00` 调整为 `2026-09-20 23:09`，避免计划提前执行。
- 当序列执行次数大于 `0` 且换算后的时间仍在未来时，更新界面中尚未保存的“首次日期”和“首次时间”。
- 更新时间后立即刷新计划预览和时间线，并显示：`已根据额度重置时间调整为 <时间>，尚未保存。`
- 保留单一“测试请求链路”入口，不恢复目标模式选择。

### Not included

- 不自动点击或等效执行“保存并启用”。
- 不直接修改、覆盖或重建已安装的 Windows 计划任务。
- 不修改序列间隔、执行次数、额外单次唤醒、模型或当前 Codex / CC Switch 路由。
- 不在缺乏可靠结构化证据时根据文本、模型名或渠道猜测额度重置时间。

## User flow

1. 用户设置序列次数大于 `0`，点击“测试请求链路”。
2. 程序沿当前配置完成真实请求，并照常展示链路状态和最终模型。
3. 如果本次测试得到可靠的官方额度重置时间，程序换算成本地时间并向后取整到可调度的下一整分钟。
4. 程序更新未保存的首次日期/时间、刷新时间线，并提示该变更尚未保存。
5. 用户检查后点击“保存并启用”，此时才更新统一的 Windows 计划任务。

## Interaction specification

- Entry point: 现有“测试请求链路”按钮，无新增模式或第二个测试按钮。
- Default state: 没有可靠重置时间时，计划输入保持原值。
- Actions and feedback: 自动填入后使用醒目但非错误样式提示新时间及“尚未保存”；现有保存按钮保持为唯一生效入口。
- Responsive behavior: 提示应在当前可滚动主界面中完整显示，不遮挡链路结果或计划输入。
- Demo or visual references: 不需要新增演示；沿用现有 WinForms 样式。

## Data and safety boundaries

- Reads: 仅读取本次测试产生的结构化 usage/rate-limit 元数据或与本次请求精确匹配的可信本地证据。
- Writes: 只写入当前窗口内尚未保存的首次日期和时间控件，以及既有本地测试结果/日志字段。
- Must not do: 不读取或记录 Token、Cookie；不猜测时间；不因测试自动修改任务计划程序。
- Effect on original files or external systems: 测试本身仍会消耗一次真实请求；自动调整只影响未保存的界面状态。

## Failure and edge cases

- Empty state: 没有额度重置字段时不调整，保留原计划，并显示“本次未获得额度重置时间，计划未调整”。
- Invalid input: 时间无法解析、缺少时区、换算后已过期或不晚于当前时间时不调整，并说明原因。
- Sequence count zero: 序列执行次数为 `0` 时不启用首次时间，也不自动改为非零；提示“当前未启用执行序列，计划未调整”。
- Multiple reset values: 只采用与当前官方额度窗口明确对应且证据唯一的时间；存在冲突时不调整并引导查看日志。
- Failure message and next action: 所有未调整场景必须保留原输入，给出原因；用户仍可手工设置计划。

## Acceptance criteria

1. Given 本次测试返回可靠时间 `2026-09-20 23:08:42 +08:00` 且序列次数大于 `0`，when 测试完成，then 首次执行时间更新为 `2026-09-20 23:09`，时间线同步刷新，并提示尚未保存。
2. Given 官方重置时间使用其他时区，when 解析成功，then 先换算为 Windows 当前本地时区，再向后取整到下一整分钟。
3. Given 未返回可靠时间、时间无效、时间已过期、证据冲突或序列次数为 `0`，then 原计划输入和已安装 Windows 计划均不改变，并显示具体原因。
4. Given 自动调整已经发生，when 用户未点击“保存并启用”就关闭程序，then 已安装计划保持原样。
5. Given 自动调整已经发生，when 用户点击“保存并启用”，then 仅按既有保存流程更新统一任务 `CodexQuotaWaker-Schedule`。
6. 自动化验证不得发送真实 Codex 请求、修改用户真实路由、安装计划任务或使电脑睡眠。

## Development handoff

- Version impact: feat，预计 `0.11.0` → `0.12.0`
- Relevant modules: developer task determines after inspection
- Required verification: `scripts/build.ps1`、`scripts/verify.ps1`，并新增时间解析、时区转换、分钟向后取整、不修改已安装计划的自检。
- Deployment or desktop update: 重新生成本地单文件 EXE；不自动覆盖正在运行的实例。
- Git and push constraints: Conventional Commit；未经用户明确要求不执行 `git push`。

## Development execution profile

- Assessment status: approved
- Assessed requirement date or revision: `2026-09-22` 当前修订
- Complexity and dominant cost drivers: 中等；需要恢复对明确 rate-limit 重置字段的窄范围解析，但不能恢复旧目标模式/额度成败语义，还需覆盖时区换算、分钟向后取整、序列为 0 和不触碰已安装计划等边界。
- Recommended model: `gpt-5.6-luna`
- Recommended reasoning effort: `max`
- Recommendation rationale: 代码已有重置时间兼容字段和 JSON fixture，但解析语义刚被简化，Max 更适合避免把“用于预填时间”误实现成“恢复官方额度判断”。
- Lower-cost alternative and tradeoff: `gpt-5.6-luna / high`；预计可完成，但在多种 JSON 事件形态、时区和旧字段边界上漏测的风险略高。
- Engineering effort range: 2–4 小时，主要用于结构化元数据解析、纯函数化时间调整、WinForms 状态反馈和自检。
- AI usage or API cost range: 中等产品计划用量；预计需要数轮代码检索、修改、构建和自检，不包含用户手动发起的真实 Codex 测试所消耗的额度，也不换算 API 金额。
- Estimate basis and excluded costs: 基于单文件 WinForms 项目、现有 analyzer/fixture/计划预览实现和当前验证脚本；不含真实线上请求验证、桌面视觉控制、GitHub 推送。
- Confidence: high
- Assumptions and unknowns: 当前 Codex 结构化事件仍可能提供 `rate_limits.primary.resets_at` 一类带明确窗口语义的字段；若真实事件结构已变化，则需要产品决定是否接受其他证据来源。
- Escalation condition: 实际响应无法稳定提供可验证的重置字段，需返回产品侧决定是否支持其他证据来源。
- User decision: confirmed option `0`
- Approved model: `gpt-5.6-luna`
- Approved reasoning effort: `max`
- Approved budget or usage range: medium product-plan usage; excludes any real Codex request used for manual verification
- Profile approved date: `2026-09-22`
- Development started: `2026-09-22`

## Development delivery

- Completed date: `2026-09-23`
- Developer task: `codex://threads/01a07f70-dd43-7292-9ebb-497bc178b349`
- Implementation: structured `codex` 300-minute reset evidence is parsed without restoring quota success semantics; reliable reset time is converted to Windows local time, rounded strictly forward to the next minute, and applied only to unsaved sequence controls. The installed task remains unchanged until the existing save action.
- Verification: `scripts/build.ps1` and `scripts/verify.ps1` passed. Self-checks covered Unix and explicit-offset ISO timestamps, missing timezone, conflicting values, sequence count zero, past reset time, and no real request/task installation/desktop control.

## Open decisions

- None
