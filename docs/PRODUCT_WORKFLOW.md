# Product workflow

## 2026-10-07 TIMELINE-001 执行

- 权威需求：`docs/requirements/REQ-20261007-timeline-execution-status.md`，confirmed。用户要求真实执行状态，产品已向当前开发聊天交付；批准配置 `gpt-6.1-sol / medium`，单聊天、无子代理或工作树、非视觉验证。
- 当前产品：`01a1012e-b555-7490-a340-5264a4da3c7b`；当前开发：`01a10130-e18a-7860-8690-0ed3fc4a1593`。下方旧绑定记录为历史，不用于本轮派发。
- 只做计划时间线执行关联与有限必要历史；UI-003 弹窗已交付待验收；RESULT-001、WAKE-001、STATE-001 和产品结构候选不混入。
- 2026-10-08 修复已交付，状态 `implementation_completed / awaiting_acceptance`。原 0.16.0 连续执行验收未通过，原因及纠正保留在权威需求；0.16.1 现已更新原 EXE 与任务引用后台副本，版本/哈希一致。共享后台开始证据路径先恢复旧记录再关联当前运行；无 GUI 的两次后台成功/失败及完整回归通过，build/verify/diff 通过。
- 正式历史迁移已依据两次独立日志与执行前保存的同一计划，谨慎回填 17:27、22:30 两条成功；重读历史确认为恰好两条。配置/任务定义哈希及保存时间均不变，无新请求、任务修改或桌面控制。专属验证任务和临时产物清理，无活动命令/工作进程，执行槽释放；下次真实自动执行及视觉仍待验收。关联歧义继续保留未确认。

## 2026-10-07 结果弹窗优化执行

- 用户最新执行档位覆盖：`gpt-6.1-sol / medium`，已成功向原开发聊天发送设置；本轮替代此前Luna/max，范围及验收不变。

- 当前需求：`docs/requirements/REQ-20261007-test-result-dialog.md`，用户已明确要求执行；执行档位 gpt-6.1-sol/medium、单聊天、纯代码与隐藏布局验证，无桌面控制。
- 开发：01a10130-e18a-7860-8690-0ed3fc4a1593，发送前上轮completed/无运行工作，需求发送成功。固定原EXE与启动入口。
- 本项只优化测试结果弹窗；WAKE-001与其他issue继续待办。
- 2026-10-07 开发已完成，待用户验收：`0.15.2` 已更新固定 `dist/CodexQuotaWaker.exe`。build/verify/diff 检查通过，结果弹窗布局与导航数据不变检查通过；实机视觉和真实请求未验收。详见需求中的开发交付记录。无活动工作进程/命令，执行槽释放。

## 2026-10-04 TEST-001 开发记录

- 需求：`docs/requirements/REQ-20261004-independent-link-test.md`。用户明确“继续工作”，沿用 Luna/max 单聊天；已交付版本 `0.15.1`。
- 发送前确认 MODEL-001 已完成、开发 01a10130-e18a-7860-8690-0ed3fc4a1593 idle；需求已成功送达。
- 本轮只拆分连接测试与计划校验，其余 issue 待办；固定原 EXE 与启动入口。

- Project: `Codex 额度唤醒器`
- Requirements task title: `【Codex 额度唤醒器】需求`
- Requirements task: `codex://threads/01a07f60-ce49-7091-9d5c-9e9617dcdf87`
- Developer task title: `【Codex 额度唤醒器】开发`
- Developer task: `codex://threads/01a07f70-dd43-7292-9ebb-497bc178b349`
- Relationship rebuilt on 2026-09-08 at user request: previous bound task `01a07f59-e4a7-7803-8f4e-fa99653a66c3` had workflow problems and is no longer authorized to receive requirements. Its history remains intact and is not part of the current relationship.
- Replaced on 2026-09-08 at user request: old task `01a070e8-07a6-77c3-b783-2a92ce597421`, latest turn completed, not running. User handles its archival. Do not dispatch further work to the old task.
- Shared checkout: `C:\文档\GPT使用.迁移\codex-quota-waker`
- Initialized: `2026-09-05`
- Status: initialized; MODEL-001 and TEST-001 were delivered on 2026-10-04; UI-003 result dialog was delivered on 2026-10-07 and awaits acceptance; no requirement is currently executing.

## Latest development

- 2026-10-04 完成 TEST-001：`docs/requirements/REQ-20261004-independent-link-test.md`。修复根因是手动测试复用计划全量校验，导致未填完的计划阻断请求测试。
- Developer: `codex://threads/01a10130-e18a-7860-8690-0ed3fc4a1593`；批准配置 `gpt-6-luna / max`，单聊天串行，不使用子代理或桌面控制。
- Release: `0.15.1`; canonical output `dist/CodexQuotaWaker.exe` (`ProductVersion=0.15.1`, `FileVersion=0.15.1.0`, 161792 bytes, SHA-256 `840E1035FA4D35A5EA8B8E24318A2CA2A46E2FC5E20B5E9DC2AE32AA31CF3560`)；固定原启动入口。
- Verification: `scripts/build.ps1`, `scripts/verify.ps1`, and `git diff --check` passed; manual-test isolation, full plan validation, reset-prefill gating and draft protection checks passed; task registration and full verification both passed.
- Coordination: completion evidence including root cause, tests, release status and the end-to-end limitation was sent successfully to the product task `额度唤醒器-产品` (`01a1012e-b555-7490-a340-5264a4da3c7b`).
- Boundaries: no real Codex request, user plan/config change, desktop control, or `git push`; STATE-001 remains untouched. The verification script's dedicated integration task was removed; no active worker or command remains.

## Previous delivery: MODEL-001 (0.15.0)

- 2026-10-04 完成 MODEL-001：`docs/requirements/REQ-20261004-refresh-request-model-catalog.md`。
- Developer: `codex://threads/01a10130-e18a-7860-8690-0ed3fc4a1593`；批准配置 `gpt-6-luna / max`，单聊天串行，不使用子代理或新工作树。
- Release: `0.15.0`; canonical output `dist/CodexQuotaWaker.exe` (`ProductVersion=0.15.0`, `FileVersion=0.15.0.0`, 157184 bytes).
- Verification: `scripts/build.ps1`, `scripts/verify.ps1`, and `git diff --check` passed; model filtering/deduplication, safe cache, fallback, timeout/failure, request snapshot and selection-preservation checks passed; task registration and full verification both passed.
- Coordination: completion evidence was sent successfully to the product task `额度唤醒器-产品` (`01a1012e-b555-7490-a340-5264a4da3c7b`).
- Boundaries: no real Codex request, user plan/config change, desktop control, or `git push`; TEST-001 and STATE-001 were then untouched. No active worker or command remains.

## Previous delivered requirement: flexible plan input order (0.14.1)

- Requirement: `docs/requirements/REQ-20261004-flexible-plan-input-order.md`; implementation and formal path delivery completed.
- The original `dist/CodexQuotaWaker.exe` was updated after PID 22944 exited naturally. Verified `ProductVersion=0.14.1`, `FileVersion=0.14.1.0`; `FlexiblePlanInputOrder=True`, `TaskRegistration=PASS`, `Verification=PASS`.
- Hidden WinForms coverage included blank/invalid/zero sequence input, retained start values, “现在”, invalid save rejection and zero sequence plus extra wake. No desktop control or real Codex request was used.

## Previous feature release: GPT-6 request models

- Requirement: `docs/requirements/REQ-20261003-gpt6-request-models.md`; version `0.14.0` was installed at the official path after PID 33536 exited naturally.
- Model behavior: `gpt-6-luna` is the constructor and empty-value default; only an unconfirmed legacy `gpt-5.6-luna` is migrated in the UI draft. A persisted confirmation flag preserves a deliberately selected `gpt-5.6-luna`; custom values remain supported.
- Model-catalog evidence: installed `codex-cli 0.160.0`; read-only `codex debug models` returned `gpt-6.1-sol`, `gpt-6-astra`, `gpt-6-sol`, `gpt-6-luna` with `visibility=list` and `supported_in_api=true`. No model request was sent.
- The earlier UI v2 native date/time border visual comparison remains unverified. No desktop control was used for the model update.

## Superseded UI iteration

- `docs/requirements/REQ-20261003-modern-ui.md` records the earlier visual direction that the user did not accept. The v2 requirement and demo supersede it; preserve its history, but do not treat its visual review as acceptance of v2.
- The earlier iteration's notes about desktop inspection are historical and do not authorize desktop control for the v2 work.

## Operating contract

- 产品优化待办：`docs/requirements/ISSUES.md`。用户要求当前输入顺序修复完成后逐项优化；待办未批准开发，不进入当前实现或已批准开发队列。

1. Product discussion, specifications, acceptance criteria, and demos live in the requirements task.
2. Confirmed production implementation lives in the developer task.
3. `docs/requirements/` is the authoritative handoff channel.
4. Before asking for development confirmation, the requirements task completes the current requirement's execution assessment and presents its card together with the final requirement summary.
5. The user makes one combined confirmation covering entry into development and the recommended or overridden model, reasoning effort, and cost/usage range. After that confirmation, the requirement is sent immediately without another approval step.
6. If the user asks to start development before seeing a current assessment card, the requirements task first presents the combined confirmation message; “确认开发” after seeing that card authorizes immediate handoff.
7. Material requirement changes make the prior execution profile stale and require reassessment before a refreshed single confirmation.
8. Product-impacting ambiguity returns to the requirements task; implementation details remain with the developer task.

## Latest completed development

- Requirement: `docs/requirements/REQ-20260922-align-schedule-to-quota-reset.md`
- Status: implemented and formally verified on 2026-09-23
- Developer task: `codex://threads/01a07f70-dd43-7292-9ebb-497bc178b349`
- Approved execution profile: `gpt-5.6-luna / max`
- Version: `0.12.0`
- Verification: `scripts/build.ps1` and `scripts/verify.ps1` passed; structured reset evidence, timezone conversion, strict next-minute rounding, missing-timezone, conflicting-value, zero-sequence, and past-time safeguards passed. No real Codex request, user plan change, task installation, desktop control, or git push was performed.

- Requirement: `docs/requirements/REQ-20260922-simplify-request-route-test.md`
- Status: implemented and formally verified on 2026-09-22
- Developer task: `codex://threads/01a07f70-dd43-7292-9ebb-497bc178b349`
- Approved execution profile: `gpt-5.6-luna / max`
- Version: `0.11.0`
- Verification: `scripts/build.ps1` and `scripts/verify.ps1` passed; route-test self-checks for valid response status, final-model evidence, no request-model fallback, quota-field isolation, scheduler XML, and no-secret diagnostics passed. The approved visual check was attempted but native WinForms was not exposed by the active Computer Use surface; no real Codex request, plan change, route change, or git push was performed.

- Requirement: `docs/requirements/REQ-20260910-password-pin-guidance.md`
- Status: implemented and formally verified on 2026-09-22
- Developer task: `codex://threads/01a07f70-dd43-7292-9ebb-497bc178b349`
- Approved execution profile: `gpt-5.6-luna / max`
- Version: `0.10.1`
- Verification: `scripts/build.ps1` and `scripts/verify.ps1` passed; credential diagnosis, PIN guidance, secret-safety, existing route/quota, scheduler, and task registration checks passed; no real password, user plan, security policy, real Codex request, or git push was used.

- Requirement: `docs/requirements/REQ-20260918-route-and-quota-verification.md`
- Status: implemented and formally verified on 2026-09-18
- Developer task: direct implementation exception authorized by the user in the requirements task
- Approved execution profile: current interface model / current interface reasoning effort; user override
- Version: `0.10.0`
- Verification: `scripts/build.ps1` and `scripts/verify.ps1` passed; route, quota, final-model and CC Switch upstream fixtures passed; no user plan was changed.

- Requirement: `docs/requirements/REQ-20260910-run-after-restart.md`
- Status: implemented and formally verified on 2026-09-10
- Developer task: `codex://threads/01a07f70-dd43-7292-9ebb-497bc178b349`
- Approved execution profile: `gpt-5.6-luna / high`
- Version: `0.9.0`
- Verification: `scripts/build.ps1` and `scripts/verify.ps1` passed; no real password was entered, no user plan was changed, and no git push was performed.

- Requirement: `docs/requirements/REQ-20260909-start-now-button.md`
- Status: implemented and formally verified on 2026-09-09
- Developer task: `codex://threads/01a07f70-dd43-7292-9ebb-497bc178b349`
- Approved execution profile: `gpt-5.6-luna / medium`
- Version: `0.8.0`
- Verification: `scripts/build.ps1` and `scripts/verify.ps1` passed; self-check `Result=PASS`, task registration and verification both passed; no plan installation or git push performed.

- Requirement: `docs/requirements/REQ-20260908-unified-schedule.md`
- Status: implemented and verified on 2026-09-08
- Implementation: direct requirements-task exception after replacement developer stalled
- Version: `0.7.0`
- Verification: `scripts/build.ps1` and `scripts/verify.ps1` passed; no formal task push or user-plan installation performed.

- Requirement: `docs/requirements/REQ-20260907-return-to-sleep.md`
- Status: implemented and verified on 2026-09-08
- Developer task: `codex://threads/01a070e8-07a6-77c3-b783-2a92ce597421`
- Approved on: `2026-09-07`
