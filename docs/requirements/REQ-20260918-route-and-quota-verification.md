# 请求链路可见性与官方额度验证

- Status: implemented and verified
- Priority: P0
- Target version: 0.10.0
- Confirmed date: 2026-09-18
- Implementation exception: user explicitly authorized development in the current requirements task using the current interface model

## Objective and user value

- User goal: 每次测试或计划执行后，用户都能明确知道请求实际走了哪类 Codex 链路，以及是否命中官方 Codex 五小时额度，而不是只看到模型回复成功。
- Current problem: 程序当前直接跟随 `%USERPROFILE%\.codex\config.toml` 中的 provider 和 `base_url`。当 CC Switch 正在本地接管且上游是其他渠道时，模型请求可能成功，却不会触发用户目标的官方 Codex 额度；现有结果只记录退出码和文本，因此会把“请求成功”误当成“目标额度完成”。
- User-visible outcome: 主界面启动后展示“目标模式”和“当前检测到的请求链路”；最近一次结果分别展示请求状态、实际链路诊断、官方额度状态和下一步。请求成功但官方额度未确认时使用警告状态，不显示成功绿勾。

## Diagnosis evidence

- 当前机器上的 Codex 配置为 `model_provider = "custom"`、`base_url = "http://127.0.0.1:15721/v1"`，说明 Codex CLI 会经过本地路由，而不是 OpenAI 官方直连。
- 现有 `codex exec` 命令只显式指定模型和提示词，没有锁定或验证 provider。
- 2026-09-05 的历史诊断已经证明：请求成功并不等于目标额度窗口完成，实际 provider 和额度元数据必须单独验证。
- 既有 `docs/requirements/REQ-20260905-quota-verification.md` 已覆盖部分 provider 校验，但写死了 `cc-switch-official`，且尚未实现；本需求取代该草稿，改为动态识别链路并验证官方额度元数据。

## Product principles

- “Codex CLI 请求成功”和“官方 Codex 额度已命中”是两个独立结果。
- 目标默认是官方 Codex 五小时额度，不以某个固定 provider 名称代表官方链路。
- 启动提示只用于主动告知；由于 CC Switch 可能在程序关闭后改变路由，每次测试和计划执行都必须重新检测并验证。
- 不静默回退、改写或切换认证链路。
- 不读取、保存或展示 Token、Cookie、API Key、完整授权头或账号 ID。

## Scope

### Included

- 增加显式的“目标模式”：
  - `官方 Codex 额度（推荐，默认）`：只有本次运行确认到官方 Codex 额度元数据，才显示目标完成。
  - `跟随当前 Codex 配置（高级）`：允许任意当前 provider；只保证请求结果，不把官方额度未命中误报为完成。
- 在主窗口增加常驻“请求链路”状态卡，至少展示：
  - 已选择的目标模式。
  - 当前检测到的链路类型：官方直连、CC Switch 本地路由、本地路由或自定义链路。
  - 当前 provider 的非敏感标识和 `base_url` 类型；不展示凭据。
  - 最近一次实际链路与官方额度验证时间。
  - 刷新配置动作和真实测试入口。
  - 尚未真实测试时明确显示“最终模型尚未验证”，不得把请求模型当作最终模型。
  - 已完成真实测试时展示最后一次最终模型、CC Switch 实际上游和官方额度结果。
- 启动时不弹阻塞式对话框；检测结果通过状态卡和风险提示条呈现。发现目标与实际链路存在明显风险时，在测试或保存动作前给出明确确认。
- `测试触发`和计划执行改为使用 `codex exec --json`，保留最终模型回复，同时捕获本次运行的 thread/session ID 和结构化事件。
- 从同一次运行中读取实际 provider 和官方 Codex 额度元数据；优先使用 JSON 输出，只有在 JSON 不含所需信息时，才按本次 thread ID 精确读取对应的本地 Codex 会话文件。
- 当当前链路为 CC Switch 本地路由时，尽力读取 CC Switch 日志尾部中本次时间窗和模型对应的“请求目标”行，识别实际上游 host、provider 和请求模型。
- 结果区展示请求模型与最终模型；最终模型不是 `gpt-*` 官方 Codex GPT 系列，或 CC Switch 上游不是 OpenAI/ChatGPT 官方时，明确显示实际最终模型和上游，并建议切换 CC Switch 路由。
- CC Switch 日志探测必须容错：文件不存在、格式变化、时间或模型无法匹配时显示“上游未确认”，不得根据模型名或端口猜测。
- CC Switch 日志探测只记录请求 URL、目标 host、provider 标签和匹配结果，不记录请求头、Token、Cookie、账号 ID 或消息正文。
- 最近结果拆分为：
  - 请求状态：成功、失败、超时。
  - 链路状态：实际 provider、链路类型及与目标模式是否一致。
  - 官方额度状态：新窗口已触发、计入现有五小时窗口、未确认。
- 当存在 `limit_id=codex` 和有效的五小时窗口重置时间时，才允许显示官方额度已确认。
- 重置时间约等于请求完成后五小时时显示“新五小时窗口已触发”；否则显示“请求已计入现有五小时窗口”。
- 请求成功但没有有效官方额度元数据时显示黄色警告“请求成功，官方额度未确认”，不得显示“额度窗口触发成功”。
- 主界面和日志展示重置时间的本地时间格式；日志只记录路由诊断和额度状态，不记录账号身份或凭据。
- 每次计划执行都重新读取当前 Codex 配置和本次会话结果，不依赖保存计划时缓存的路由状态。
- 保持现有有限序列、额外单次时间、计划任务名称、WakeToRun、重试、防睡眠和自动睡眠行为不变。
- 修复 `codex exec` 标准输出的 UTF-8 解码，避免中文回复和日志乱码。
- 增加不发送真实模型请求的固定 JSONL/会话夹具测试，覆盖成功、未确认、现有窗口、新窗口、格式异常和 provider 变化场景。

### Not included

- 不调用或抓取 Usage Dashboard 私有接口。
- 不修改 CC Switch 的配置、账号、Token、模型映射或上游渠道。
- 不自动关闭、重启或切换 CC Switch。
- 不自动执行 `codex login`、重新登录账号或消耗额度重置券。
- 不新增常驻服务，不改变现有任务名称或唤醒时间。
- 不保证第三方代理或 CC Switch 的内部文件格式永远兼容。
- 不把缺少官方额度元数据武断解释为“第三方渠道”；没有证据时统一显示“官方额度未确认”。

## User flow

1. 用户打开程序，顶部请求链路卡展示当前选择的模式、检测到的链路和最近一次验证结果。
2. 默认选择“官方 Codex 额度”。如果当前配置包含本地 `base_url`，状态卡显示“CC Switch 本地路由，上游待本次请求验证”，并提示请求成功不一定命中官方额度。
3. 用户点击“测试触发”，确认后程序按当前目标模式发送一次真实请求，并同步捕获链路与额度元数据。
4. 结果区分别显示请求状态、实际 provider、官方额度状态和重置时间。
5. 若官方额度未确认，程序给出下一步：在 CC Switch 中切换到官方账号或官方直连，也可以明确选择“跟随当前 Codex 配置”模式后重新测试。
6. 用户保存并启用后，计划任务仍在原时间唤醒；每次后台执行都会重新检测当前路由并重复同样的验证。
7. 用户下次打开主窗口时，可以直接看到上一次计划执行的真实链路和目标额度结果。

## Interaction specification

- Entry point: 主窗口顶部新增请求链路卡；执行设置中新增目标模式；最近一次结果中新增链路和额度字段。
- Default state: 旧配置升级后默认目标模式为“官方 Codex 额度”；尚未执行时显示“暂无链路验证记录”。
- Actions and feedback:
  - `重新检测`只读取非敏感本地配置，不发送模型请求。
  - 当前检测到本地路由但目标为官方额度时，显示黄色风险条和清晰的下一步。
  - 请求成功且官方额度已确认使用绿色。
  - 请求成功但官方额度未确认使用黄色。
  - 请求失败、超时或额度解析异常使用红色。
- Save behavior: 保存“官方 Codex 额度”模式时，若当前没有可证明官方额度的最近验证，必须提示“尚未验证，首个计划执行可能无法命中目标额度”，但仍允许用户确认保存；用户可选择先测试。
- Follow-current behavior: 选择“跟随当前 Codex 配置”后，界面持续显示“仅验证请求成功，不承诺官方额度”的提示。
- Background behavior: 后台运行不弹阻塞式提示；按策略执行、记录结果并返回可解释的状态。官方额度未确认不触发自动睡眠成功条件。
- Responsive behavior: 新增卡片和结果字段必须适配现有右侧垂直滚动；不得压缩、遮挡当前执行序列、最近结果和可拖动分隔线。
- Visual references: 延续现有原生 Windows 浅色风格，不新增独立设置页面。

## Data and safety boundaries

- Reads:
  - 当前 Codex 配置中的非敏感路由字段：provider 标识、`base_url` 是否存在及是否为本机地址。
  - 本次 `codex exec --json` 输出。
  - 必要时只读取与本次 thread ID 精确匹配的本地 Codex 会话 JSONL。
- Writes:
  - `%LOCALAPPDATA%\CodexQuotaWaker\config.json` 中的目标模式。
  - `last-run.json` 和活动日志中的路由类型、provider 标识、额度桶、计划类型、窗口状态、重置时间和错误摘要。
- Must not do:
  - 不读取、打印、持久化或复制 `auth.json`、Token、Cookie、API Key、授权头或账号 ID。
  - 不修改 `%USERPROFILE%\.codex\config.toml`、`auth.json` 或 CC Switch 数据。
  - 不扫描与本次 thread 无关的会话文件。
- Effect on original files or external systems: 只增加本程序的配置、结果和日志字段；计划任务、Codex 登录和 CC Switch 状态保持不变。

## Failure and edge cases

- Empty state: 没有额度事件时显示“请求成功，官方额度未确认”，保留模型回复和实际 provider。
- Official quota confirmed: 显示额度桶、五小时窗口重置时间和本地时区。
- Existing active window: 明确显示“请求已计入现有五小时窗口”，不得误报为新窗口或失败。
- Reset time approximately five hours after request: 才显示“新五小时窗口已触发”。
- Provider or config changed after schedule save: 每次执行重新检测，并展示本次实际结果。
- CC Switch unavailable after wake or boot: 在现有执行预算内等待恢复；仍不可用时按请求失败处理并提示启动或重新接管 CC Switch，不自动改走直连。
- Route changes to another upstream: 请求仍成功时保留“请求成功”，但官方额度显示未确认，并提示检查当前上游，不自动重试消耗额外额度。
- JSONL line invalid or session file missing: 跳过异常行，继续寻找同一 thread 的有效事件；找不到时显示未确认及可执行下一步。
- `used_percent=0` but valid reset time: 仍可确认窗口存在，并解释百分比可能经过取整。
- Output contains Chinese: 日志和结果使用正确 UTF-8，不出现乱码。
- Background auto-sleep: 只有请求状态成功且官方额度已确认时，才允许满足自动睡眠的成功条件。

## Acceptance criteria

1. Given 旧配置没有目标模式，when 程序升级并首次打开，then 默认显示“官方 Codex 额度”，且不修改原计划或 Codex/CC Switch 配置。
2. Given 当前 Codex 配置包含本机 `base_url`，when 主窗口打开，then 请求链路卡显示本地路由及“上游待本次请求验证”，并明确提示请求成功不等于官方额度完成。
3. Given 请求成功且本次事件包含 `limit_id=codex` 和有效五小时重置时间，when 结果展示，then 显示官方额度已确认、实际 provider 和本地重置时间。
4. Given 请求成功但缺少官方 Codex 额度元数据，when 结果展示，then 显示黄色“请求成功，官方额度未确认”，不得显示成功绿勾或“额度窗口触发成功”。当 CC Switch 日志可证明上游为非官方目标时，同时显示实际 provider、目标 host 和最终模型，并建议切换 CC Switch 路由。
4a. Given CC Switch 日志不可用或无法可靠匹配，when 本次请求结束，then 仍可依据额度元数据完成判定，并将实际上游显示为未确认。
5. Given 官方额度元数据对应已有窗口，when 结果展示，then 显示“请求已计入现有五小时窗口”，不误报为新窗口。
6. Given 用户选择“跟随当前 Codex 配置”，when 请求成功但没有官方额度元数据，then 只显示请求成功和当前配置模式，不承诺官方额度。
7. Given 保存后 CC Switch 更换上游，when 下一次计划执行，then 程序重新检测并显示本次实际结果，不使用旧缓存状态。
8. Given 官方额度未确认，when 自动睡眠功能开启，then 本次不得满足自动睡眠成功条件。
9. Given 固定 JSONL 测试样本，when `scripts/verify.ps1` 运行，then 新窗口、现有窗口、未确认、格式异常和 provider 变化场景全部通过，且不发送真实模型请求。
10. Given 输出或日志包含中文，when 写入 `last-run.json` 和活动日志，then 内容为正确 UTF-8，不出现乱码。
11. Given 现有计划已启用，when 新版本构建并验证，then 任务名称、时间序列、WakeToRun、防睡眠和自动睡眠行为保持不变。
12. Given 扫描配置或读取会话数据，when 自动化测试检查文件和日志，then 不出现 Token、Cookie、API Key、授权头或账号 ID。

## Development handoff

- Version impact: feat（0.9.0 → 0.10.0）
- Relevant modules: developer task determines after inspection; likely `src/CodexQuotaWaker.cs`、`scripts/verify.ps1`、`README.md` 和 `docs/PRD.md`。
- Required verification: `scripts/build.ps1`、`scripts/verify.ps1`；使用固定夹具覆盖额度与路由状态，不发送真实 Codex 请求；核对 EXE 版本为 0.10.0。
- Deployment or desktop update: 构建单文件 EXE；不自动安装、覆盖或修改用户现有 Windows 计划任务；不自动打开或切换 CC Switch。
- Git and push constraints: Conventional Commit 建议 `feat: verify Codex route and quota status`；不自动 `git push`。

## Development execution profile

- Assessment status: approved
- Assessed requirement date or revision: 2026-09-18 / revision 1
- Complexity and dominant cost drivers: 中高；主要成本是动态路由分类、`codex exec --json` 与会话 JSONL 兼容、官方额度语义、WinForms 状态卡和结果布局、UTF-8 处理，以及无真实请求的固定夹具测试。
- Recommended model: gpt-5.6-terra
- Recommended reasoning effort: high
- Recommendation rationale: 改动集中在单文件 WinForms，但涉及认证链路、外部工具输出格式和成功判定语义；一旦把未确认额度误报为成功，会直接误导用户的唤醒结果。Terra high 更适合减少这些可靠性风险。
- Lower-cost alternative and tradeoff: gpt-5.6-luna / high；成本和速度更低，但可能需要更多人工复核，尤其是 CC Switch/custom provider 与 JSONL 边界。
- Engineering effort range: 4–7 小时，包括实现、固定夹具、构建和回归验证；不包含真实额度请求验收或人工切换 CC Switch。
- AI usage or API cost range: ChatGPT 产品计划中的中等用量，预计 2–4 个集中开发/验证回合；不预计产生单独 API Key 账单。真实模型请求不在自动化验收内。
- Estimate basis and excluded costs: 基于当前约 3000 行单文件 WinForms、既有构建/验证脚本、Codex CLI 会话结构和本机 CC Switch 路由现状；不含外部工具未来格式变化、真实额度消耗和人工 Dashboard 复核。
- Confidence: medium
- Assumptions and unknowns: `codex exec --json` 能稳定提供本次 thread 标识，或能按 thread ID 精确关联会话文件；官方额度事件包含 `limit_id`、窗口长度和 `resets_at`；CLI 版本与现有 JSONL 结构兼容。
- Escalation condition: 若可靠验证 provider 或额度必须读取 `auth.json`、调用私有 Dashboard 接口、修改 CC Switch 内部数据库，或只能依赖不稳定的第三方内部实现，开发者停止实现并返回需求任务，不得扩大到凭据读取或外部配置修改。
- User decision: confirmed override
- Approved model: current interface model (user override; do not switch model)
- Approved reasoning effort: current interface reasoning effort
- Approved budget or usage range: inherit current interface usage; no model or budget escalation without user approval
- Profile approved date: 2026-09-18

## Open decisions

- None. 推荐默认采用“官方 Codex 额度”模式；“跟随当前 Codex 配置”作为高级模式保留。

## Implementation result

- Completed: 2026-09-18
- Released source version: 0.10.0
- Implementation context: user authorized direct implementation in the requirements task using the current interface model.
- Verification: `scripts/build.ps1` and `scripts/verify.ps1` passed; EXE version is 0.10.0.
- Automated evidence: new-window, existing-window, unconfirmed, follow-current, malformed JSONL, route detection, CC Switch upstream evidence and CC Switch remote provider/error self-checks passed.
- Real model request: not executed by the automated verifier. Two explicit manual diagnostics were run on 2026-09-18: `gpt-5.6-terra` was forwarded by CC Switch to `api.deepseek.com` and rejected with HTTP 400; `deepseek-flash` succeeded but returned no official `codex` rate-limit metadata. Current live route is therefore non-official.
- Installation boundary: no user Windows plan was installed, replaced, paused or deleted.
