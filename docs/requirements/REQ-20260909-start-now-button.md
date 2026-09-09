# 首次时间“现在”快捷按钮

- Status: completed
- Priority: normal
- Target version: 0.8.0
- Confirmed date: 2026-09-09
- Queued date: not queued
- Development started date: 2026-09-09
- Completed date: 2026-09-09

## Objective and user value

- User goal: 一键把有限序列的首次执行时间设置为当前可执行的最近时间，不再分别调整日期和时间。
- Current problem: 用户需要手动操作两个时间控件；直接使用当前分钟又可能在保存前变成过去时间，触发现有校验失败。
- User-visible outcome: 点击“现在”后，首次日期和时间自动变为当前本地时间的下一个整分钟，计划预览同步更新。

## Scope

### Included

- 在“首次日期 / 时间”输入区域旁增加一个“现在”按钮。
- 点击时读取一次当前本地时间，去掉秒和毫秒后顺延至下一个整分钟。
- 使用同一时间值更新首次日期与首次时间，避免跨午夜时日期和时间来自不同次读取。
- 更新输入值后立即刷新序列摘要和当前计划时间线。
- 序列执行次数为 0 时，按钮与首次日期、首次时间一起禁用；恢复为大于 0 时重新启用。

### Not included

- 不自动保存或启用 Windows 计划任务。
- 不立即执行 Codex 请求。
- 不修改“插入额外唤醒”的日期、时间或按钮。
- 不改变首次执行时间必须晚于当前时间的校验规则。
- 不新增秒级调度能力。

## User flow

1. 用户把序列执行次数设为大于 0。
2. 用户点击首次时间区域的“现在”。
3. 程序将首次日期和时间设置为点击时刻的下一个整分钟，并立即刷新摘要和时间线。
4. 用户检查最终计划后，仍通过原有“保存并启用”操作写入计划任务。

## Interaction specification

- Entry point: 统一计划区域中“首次日期 / 时间”同一行，靠近两个输入控件的位置。
- Default state: 序列执行次数大于 0 时可用；等于 0 或次数输入无效时禁用。
- Actions and feedback: 点击后直接更新日期、时间、摘要和时间线；无需弹窗。按钮文字固定为“现在”。
- Responsive behavior: 保持现有 WinForms 窗口最小宽度下可见，不遮挡日期和时间值；必要时由开发者在同一区域内调整列布局，但不得扩大产品范围。
- Demo or visual references: 无；现有控件旁增加单个标准按钮即可，不需要单独演示。

## Data and safety boundaries

- Reads: 点击瞬间的 Windows 本地系统时间。
- Writes: 仅修改当前窗口中尚未保存的首次日期和时间输入值。
- Must not do: 不写配置文件、不更新 Windows 计划任务、不触发 Codex、不改变额外唤醒列表。
- Effect on original files or external systems: 点击本身无外部副作用；只有用户之后执行原有保存操作才产生既有行为。

## Failure and edge cases

- Empty state: 不适用；按钮只依赖系统时间。
- Invalid input: 序列次数为空、不是非负整数或为 0 时禁用按钮。
- Missing path or unavailable service: 不依赖文件路径或外部服务。
- Failure message and next action: 正常情况下无需错误提示；若系统时间超出控件可表示范围，保持原值并提示“无法设置当前时间，请检查 Windows 日期和时间设置”。
- Midnight boundary: 只读取一次当前时间；例如 23:59:30 点击后应得到次日 00:00，不得出现日期与时间错配。
- Save delay: 点击后若用户等待到该分钟已过再保存，沿用现有“首次执行时间必须晚于当前时间”提示，不静默再次顺延。

## Acceptance criteria

1. Given 序列次数大于 0且本地时间为 `2026-09-09 14:26:00` 至 `14:26:59`，when 点击“现在”，then 首次日期/时间显示 `2026-09-09 14:27`，摘要和时间线立即按该时间更新，且没有保存或执行计划。
2. Given 本地时间为 `2026-09-09 23:59:30`，when 点击“现在”，then 首次日期/时间显示 `2026-09-10 00:00`。
3. Given 序列执行次数为 0或次数输入无效，then “现在”按钮不可点击；恢复为大于 0的有效整数后按钮恢复可用。
4. Given 点击“现在”后等待至所填分钟已不晚于当前时间，when 保存并启用，then 沿用现有校验阻止保存并说明首次时间必须晚于当前时间，不自动改写用户已看到的值。
5. Given 用户只点击“现在”后关闭窗口，then 配置文件、Windows 计划任务和 Codex 调用均不发生变化。
6. Existing `scripts/build.ps1` and `scripts/verify.ps1` pass, including new or updated self-checks for next-minute rounding, midnight rollover, enable/disable state, and no automatic save side effect where testable.

## Development handoff

- Version impact: feat (minor version, target 0.8.0)
- Relevant modules: `src/CodexQuotaWaker.cs`; documentation/version fields as required by project rules
- Required verification: `scripts/build.ps1`, `scripts/verify.ps1`, and targeted checks for time rounding and midnight rollover
- Deployment or desktop update: generate updated single-file `dist/CodexQuotaWaker.exe`; do not install or alter the user's scheduled task
- Git and push constraints: Conventional Commit if committing; create matching `v0.8.0` tag only as part of an explicitly performed release; do not push without explicit user instruction

## Development execution profile

- Assessment status: approved
- Assessed requirement date or revision: 2026-09-09 initial handoff-ready revision
- Complexity and dominant cost drivers: small localized WinForms interaction; main risks are layout fit, single-read time rounding, midnight rollover, and keeping enable state synchronized with sequence count validation
- Recommended model: gpt-5.6-luna
- Recommended reasoning effort: medium
- Recommendation rationale: localized UI behavior with clear acceptance criteria and existing build/verification scripts; medium is sufficient to handle time boundaries and regression checks without higher-cost reasoning
- Lower-cost alternative and tradeoff: gpt-5.6-luna / low; likely sufficient for the button but more likely to miss layout or midnight/disabled-state regressions
- Engineering effort range: about 30–60 minutes including code, targeted verification, build, and documentation/version updates
- AI usage or API cost range: low product-plan usage, approximately one focused implementation and one correction pass; no reliable currency estimate because this is Codex plan usage rather than metered API billing
- Estimate basis and excluded costs: based on one C# source file, existing controls, and existing build scripts; excludes human acceptance testing, account quota consumption, deployment, and scheduled-task installation
- Confidence: high
- Assumptions and unknowns: existing layout has enough width or can be locally adjusted; no new visual design system is required; current test harness can cover pure rounding logic even if direct UI automation is limited
- Escalation condition: stop and return to the requirements task if fitting the button requires redesigning the schedule section, or if implementation would change save behavior, scheduler precision, or persisted configuration schema
- User decision: confirmed recommendation
- Approved model: gpt-5.6-luna
- Approved reasoning effort: medium
- Approved budget or usage range: low product-plan usage; approximately one focused implementation and one correction pass, without a hard quota or currency commitment
- Profile approved date: 2026-09-09

## Open decisions

- None

## Implementation notes

- Added the “现在” button beside the first sequence date/time controls.
- The button uses one local `DateTime.Now` read, rounds to the next minute, and updates both controls without saving or executing.
- Added self-check coverage for next-minute rounding, midnight rollover, and sequence-count enable state.
