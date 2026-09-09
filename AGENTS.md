# Codex 额度唤醒器开发规则

## 目标

- 为 Windows 用户提供一个无需常驻的图形化工具，可靠地在指定时间唤醒电脑并发起真实的 Codex CLI 请求。
- 以成功的模型响应和本地日志作为执行成功依据，不以“计划任务已启动”代替业务成功。

## 用户体验

- 面向非程序员，核心操作必须在一个窗口内完成。
- 默认值应安全可用：GPT-5.6 Luna、只读沙盒、程序内部管理防睡眠与执行超时、最多尝试 3 次（含首次）。不再提供保持清醒时长输入框。
- 计划统一为有限序列加额外单次时间，不再提供模式勾选和另一套每日时间入口。序列执行次数允许为 0；为 0 时仅执行额外时间，为 1 时仅执行首次序列时间。合并时间必须去重；空计划不能启用。
- 一次性有限序列的间隔由用户设置；默认 5 小时 10 分钟。次数包含首次执行，最后一次后停止，不按天重置。
- 用户可为任意未来日期时间插入额外的单次唤醒；每条额外唤醒同样执行一次 Codex 触发，并与有限序列合并写入同一条 Windows 计划任务。
- 主界面必须以可滚动时间线展示当前计划：时间、来源（序列或插入唤醒）和相对状态；不得只显示一条“下次触发”摘要。
- 当内容高度超过窗口时，主窗口必须提供右侧垂直滚动；“当前执行序列”和“最近一次结果”必须通过可拖动分隔线分配高度，不能因窗口高度不足而被压缩或遮挡。
- 主界面必须把 Windows 中实际安装的计划状态作为独立摘要展示：是否存在、是否启用、是否可唤醒、下次与最后一次触发时间；不能只依赖配置文件中的启用标记。
- 保存并启用始终更新统一命名的 `CodexQuotaWaker-Schedule`，不得创建第二条计划任务；界面必须在操作前后清楚说明这一点。
- 错误信息必须说明原因和下一步，日志中不得记录令牌、Cookie 或其他凭据。
- 程序本身不常驻；Windows 计划任务直接调用同一个 EXE 的后台模式。

## 技术约束

- 使用 Windows 自带的 .NET Framework 4.8 WinForms，产物为单文件 `CodexQuotaWaker.exe`。
- 不依赖第三方 NuGet 包，不要求用户安装开发环境或运行时。
- 计划任务必须启用 `WakeToRun`，以当前登录用户的交互令牌运行。
- 后台执行期间使用 `SetThreadExecutionState` 保持系统清醒，结束后恢复正常电源策略。
- “任务唤醒电脑后自动睡眠”默认关闭；仅当 Power-Troubleshooter 明确记录由 `CodexQuotaWaker-Schedule` 唤醒、Codex 请求成功且唤醒后无用户输入时，才可在 60 秒可取消倒计时后请求普通睡眠。
- Codex 调用使用 `codex exec`、显式模型和 `read-only` 沙盒。
- 配置与日志保存在 `%LOCALAPPDATA%\CodexQuotaWaker`，不得写入 Codex 凭据目录。

## 目录结构

```text
codex-quota-waker/
├─ AGENTS.md
├─ .gitignore
├─ README.md
├─ docs/
│  ├─ PRD.md
│  ├─ PRODUCT_WORKFLOW.md
│  └─ requirements/
│     └─ _TEMPLATE.md
├─ src/
│  └─ CodexQuotaWaker.cs
├─ scripts/
│  ├─ build.ps1
│  └─ verify.ps1
└─ dist/
   ├─ .gitkeep
   └─ CodexQuotaWaker.exe   # 本地生成，不纳入 Git
```

## 修改与验证

- 先更新本文件或 `docs/PRD.md`，再改变行为或目录约定。
- 每次修改后至少运行 `scripts/build.ps1` 和 `scripts/verify.ps1`。
- 不自动安装、删除或覆盖用户现有的 Codex 定时任务。
- 不自动执行 `git push`。

## Product and development task roles

- Project: `Codex 额度唤醒器`
- Requirements task: owns requirement clarification, prioritization, PRD/specification, user flows, acceptance criteria, approved demos, and the pre-development execution recommendation.
- Developer task: `codex://threads/01a07f70-dd43-7292-9ebb-497bc178b349`; owns technical design, production code, tests, versioning, Git, build, and deployment. This task directly implements confirmed requirements; it must not forward its own implementation work back to itself or act as the requirements coordinator.
- The requirements task does not edit production code unless the user explicitly asks for an exception.
- Draft requirements do not enter development. When submitting a handoff-ready requirement for confirmation, the requirements task presents the final requirement summary and its recommended model, reasoning effort, engineering effort, and AI cost/usage range together. The user's one confirmation approves development and that profile; the requirement is then sent without a second confirmation.
- The developer task must not expand confirmed scope. Product-impacting ambiguity returns to the requirements task for a decision.
- The developer task follows the approved execution profile and returns for approval before changing its model/budget when the recorded escalation condition is reached.
