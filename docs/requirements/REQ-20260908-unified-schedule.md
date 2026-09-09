# 统一计划模式与自动防睡眠管理

- Status: completed
- Priority: high
- Target version: 0.7.0
- Confirmed date: 2026-09-08
- Revision: 1
- Development started date: 2026-09-08
- Completed date: 2026-09-08
- Implementation note: original replacement developer task remained active without producing code; the requirements task used the explicitly authorized direct-implementation exception to finish this same confirmed revision.

## Objective and user value

面向非技术用户，消除取消有限序列后插入一次却执行多次的风险；不让用户管理任务防睡眠时长。用户报告的重复执行原因尚待开发复现，不应预先断言根因。

## Scope and interaction

- 删除一次性有限序列勾选框及其关闭时的另一套时间设置，只保留统一计划表单。
- 字段名为“序列执行次数（含首次，可为 0）”。0 次不生成序列，仅使用额外插入时间；1 次只生成首次；2 次以上按间隔生成有限序列。
- 0 次时首次日期、时间、间隔置灰；1 次时间隔置灰。无日期跨度限制。
- 保留额外插入唤醒，每个时间只触发一次。序列和额外时间合并、排序、按相同触发时间去重。
- 保存前展示序列 X 次、额外 Y 次、去重后共 Z 次以及最终时间列表；已安装计划状态仍独立展示。
- 0 次且无额外时间时不能启用，明确提示至少添加一个唤醒时间。负数和非法次数需校验。
- 删除保持清醒输入框。内部保留有限执行超时及运行期间防睡眠，成功提前结束，不为保持清醒耗尽时间而等待。
- 失败、超时或异常退出执行流程后恢复正常 Windows 电源策略，不主动睡眠。成功仍按已有自动睡眠复选框与安全条件执行，不改变默认关闭、明确任务唤醒证据、无用户输入、60 秒可取消倒计时。
- 内部超时由开发按既有单次最多 4 分钟、尝试间等待 15 秒、网络等待最多 90 秒制定有限预算，并覆盖所有允许的尝试次数；计划任务运行上限同步匹配并为清理和倒计时预留时间。旧 KeepAwakeMinutes 不得成为隐藏的用户可配置限制。
- “重试次数”改为“最多尝试次数（含首次）”，不改变次数含义。

## Data and safety boundaries

- 不自动修改实际已安装计划；仅用户点击更新并启用后更新同一个 CodexQuotaWaker-Schedule，不新增第二条任务。
- 读取旧配置时不得把关闭序列状态隐式转换成多次序列。无法无歧义转换的旧每日模式应提示用户检查新预览再保存，不悄悄启用新计划。旧配置保留可恢复性。
- 不修改凭据、CC Switch、额度判定；不承诺模型响应成功意味着服务端额度窗口已更新。
- 不实际让电脑睡眠，不用真实模型请求做自动验证，不自动 push。

## Acceptance criteria

1. 0 次加一个额外时间：预览、保存后配置、生成 XML、安装后读回均仅有一次触发，无重复间隔。
2. 0 次加多个额外时间：仅各执行一次；0 次无额外时间不能启用。
3. 1 次和多次序列正确；与额外时间重合时只执行一次；跨天及十天后时间不截断。
4. 修改保存后任务仍只有同名一条，预览和实际任务触发时间一致；关闭窗口不删除任务。
5. 旧模式、旧次数和旧时长配置加载不产生隐藏重复；必要的迁移提示可理解。
6. UI 不再出现模式勾选和保持清醒输入框；保持滚动条、可拖动分隔线及无明显遮挡。
7. 所有成功、失败、超时、异常分支释放防睡眠请求；未启用自动睡眠不发睡眠指令，失败不发睡眠指令。
8. build.ps1、verify.ps1 通过，补充零次、去重、旧配置及执行预算边界测试；报告新版 EXE 路径和用户安装生效步骤。

## Development handoff

- Version impact: feat，目标 0.7.0；更新文档、源码版本，按规则提交和打 tag，不 push。
- 先核对 PRD 并同步，再修改代码。遵守 AGENTS.md；不得覆盖无关用户改动。
- 视觉风格：保持现有 WinForms 风格，简化入口；无需新竞品或独立 demo。
- 交付构建产物，不擅自替换正在使用的任务安装。

## Development execution profile

- Assessment status: approved
- Assessed revision: 1
- Recommended model/reasoning: gpt-5.6-luna / high
- User decision: confirmed override
- Approved model: gpt-5.6-luna
- Approved reasoning effort: max
- Profile approved date: 2026-09-08
- Engineering effort: small，预计 1–2 小时；主要成本为配置兼容、调度一致性和验证，非交付时间承诺。
- AI usage: 低至中等的初始估计；用户选择 max 后可能更高。订阅用量定性估算，无硬 token 预算，不是 API 价格或额度承诺，不含真实请求、人工验证与基础设施成本。
- Confidence: medium；假设仍为单文件 WinForms，主要未知是旧模式转换和 XML 重复触发根因。
- Lower-cost alternative: Luna medium，复杂边界漏测风险更高；用户已选 max。
- Escalation: 若需改变用户已有计划语义、扩大系统权限或变更模型，先返回产品侧，不自行扩展。

## Open decisions

- None for approved behavior; ambiguous legacy migration must return for product decision.
