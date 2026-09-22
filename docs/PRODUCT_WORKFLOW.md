# Product workflow

- Project: `Codex 额度唤醒器`
- Requirements task title: `【Codex 额度唤醒器】需求`
- Requirements task: `codex://threads/01a07f60-ce49-7091-9d5c-9e9617dcdf87`
- Developer task title: `【Codex 额度唤醒器】开发`
- Developer task: `codex://threads/01a07f70-dd43-7292-9ebb-497bc178b349`
- Relationship rebuilt on 2026-09-08 at user request: previous bound task `01a07f59-e4a7-7803-8f4e-fa99653a66c3` had workflow problems and is no longer authorized to receive requirements. Its history remains intact and is not part of the current relationship.
- Replaced on 2026-09-08 at user request: old task `01a070e8-07a6-77c3-b783-2a92ce597421`, latest turn completed, not running. User handles its archival. Do not dispatch further work to the old task.
- Shared checkout: `C:\文档\GPT使用.迁移\codex-quota-waker`
- Initialized: `2026-09-05`
- Status: initialized; current development requirement implemented and formally verified

## Active development

- Requirement: none
- Started: `2026-09-22`
- Approved execution profile: `gpt-5.6-luna / max` (confirmation option `0`)
- Visual QA: not required or authorized; deterministic build, self-check, and time-boundary tests only.
- Delivery: completed in the bound developer task; no real Codex request, route change, plan installation, desktop control, or git push was performed.
- Queue: empty

## Operating contract

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
