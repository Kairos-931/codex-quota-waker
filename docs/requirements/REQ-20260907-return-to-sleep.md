# 计划唤醒后成功完成任务自动睡眠

- Status: implemented and verified
- Priority: P1
- Target version: next minor release
- Confirmed date: 2026-09-07; user authorized implementation

## Objective and user value

- User goal: 电脑被本次计划从低功耗状态唤醒并成功完成请求后，自动进入普通睡眠，减少无人使用时持续开机。
- Current problem: 释放保持清醒后仍须等待 Windows 电源策略超时。
- User-visible outcome: 可选的“任务唤醒电脑后，成功完成时自动睡眠”。用户已明确选择睡眠而非休眠。

## Scope

### Included

- 增加可保存的勾选，默认关闭，沿用原生 Windows 界面。
- 仅在能够确认本次计划导致系统恢复、请求成功且恢复后无人操作时，启动 60 秒睡眠倒计时。
- 倒计时提供“取消本次睡眠”；从恢复到发送睡眠指令期间检测到用户输入即取消。
- 本来已醒着、手动测试、冷启动、来源不明均不主动睡眠。
- 保存结果和日志后再请求普通睡眠；不强制挂起，不更改系统全局电源策略。
- 日志记录是否执行或跳过睡眠及具体原因。

### Not included

- 休眠、关机、强制关闭应用、常驻服务。
- 先前额度 provider 校验需求及其 GitHub Issue 的实现。
- 调整现有唤醒序列或自动发布 GitHub Issue。

## User flow

1. 用户勾选功能并保存计划。
2. 到点请求执行，系统记录可验证的本次恢复来源和用户活动。
3. 请求成功且满足条件时显示 60 秒倒计时。
4. 用户操作或取消则保持开机，否则保存记录并请求睡眠。

## Interaction specification

- Entry point: 执行设置中的勾选；成功后可取消的倒计时。
- Default state: 关闭，旧配置升级后不自动开启。
- Feedback: 显示“60 秒后睡眠”和“取消本次睡眠”；跳过时显示原因。
- 布局沿用滚动和可拖动分隔线，不遮挡计划或结果。

## Data and safety boundaries

- Reads: 与本次系统恢复关联的 Windows 状态/日志、用户活动时间，不记录输入内容。
- Writes: 本地配置、执行结果和日志。
- 不修改认证或账户信息；不改变现有计划序列；不强制挂起或终止其他任务。
- 正式验收时执行真实睡眠须与用户协调，自动化测试使用替身。

## Failure and edge cases

- 请求失败或超时：释放本程序保持清醒，按原系统策略运行，不主动睡眠。
- 恢复来源证据不足或不能归因到本次计划：跳过，不能仅凭计划时间接近恢复时间判定。
- 恢复后用户有输入：取消本次自动睡眠，即便随后又空闲。
- 锁屏场景仍须允许用户接管后取消；不能可靠检测时跳过。
- 系统拒绝睡眠：记录失败与下一步，不无限重试。
- 同时有其他后台工作：不得强制忽略系统电源限制；可靠性在技术评估时验证。

## Acceptance criteria

1. 功能关闭或旧配置升级时，行为保持不变。
2. 可确认本次计划唤醒、请求成功且无用户输入时，60 秒后发送普通睡眠请求。
3. 恢复后有用户输入或用户点击取消，不发送睡眠请求。
4. 本来开机、冷启动、手动测试、来源不明或请求失败，均跳过主动睡眠并记录原因。
5. 自动测试覆盖上述分支且不使真实电脑睡眠。
6. 现有计划序列、下一次唤醒设置及界面可访问性保持正常。

## Development handoff

- Version impact: feat
- Relevant modules: Windows 恢复来源判定、执行流程、原生界面及配置。
- Required verification: scripts/build.ps1、scripts/verify.ps1；使用替身验证睡眠动作。
- Deployment: 单文件 EXE；执行真实睡眠验收另行协调。
- Git: 不自动 push。

## Development execution profile

- Assessment status: approved
- Assessed requirement date or revision: 2026-09-07
- Complexity and dominant cost drivers: 中等；Windows 恢复来源归因、用户接管检测与安全睡眠倒计时。
- Recommended model: gpt-5.6-terra
- Recommended reasoning effort: high
- Recommendation rationale: 需要在现有无需常驻架构中验证 Windows 电源与输入状态，且错误实现会意外使用户电脑睡眠。
- Lower-cost alternative and tradeoff: gpt-5.6-luna / high；可能需要更多 Windows 行为验证回合。
- Engineering effort range: 3–6 小时。
- AI usage or API cost range: 产品计划中等用量，约 2–4 个集中回合；不含真实睡眠验收和额外额度请求。
- Estimate basis and excluded costs: 基于单文件 WinForms、Windows 计划任务与现有构建脚本；不含用户现场验收和外部服务成本。
- Confidence: medium
- Assumptions and unknowns: 必须可靠确认本次计划导致系统恢复，并检测恢复后的用户接管；不能把恢复时间接近计划时间当成充分证据。
- Escalation condition: 若可靠归因或用户接管检测需要常驻进程、额外权限，或证据不足，暂停实现并返回需求任务，不削弱保护条件。
- User decision: confirmed recommendation
- Approved model: gpt-5.6-terra
- Approved reasoning effort: high
- Approved budget or usage range: 产品计划中等用量，约 2–4 个集中回合。
- Profile approved date: 2026-09-07

## Open decisions

- 无。技术可行性不满足安全条件时，按执行配置中的升级条件返回需求任务。

## Implementation result

- Completed: 2026-09-08
- Released source version: 0.6.0
- Verification: `scripts/build.ps1` and `scripts/verify.ps1` passed; automated verification does not issue a real sleep command.
- Installation boundary: the existing Windows plan is unchanged until the user opens the new executable, selects the option, and clicks “更新并启用”.
