# 请求模型更新为 GPT-6 系列

- 状态：implemented，用户确认更新请求模型到 6 系列后已完成代码、正式构建与验收。
- 目标/用户：额度唤醒器用户可以直接选择当前 GPT-6 请求模型。
- 核心：默认与空配置回退使用 `gpt-6-luna`，下拉选择 GPT-6 系列，保留自定义模型输入；开发先核实本地 CLI 模型目录中的精确标识。
- 旧配置：旧内置默认 `gpt-5.6-luna` 在未保存界面迁移为 `gpt-6-luna`，提示保存后生效；其他已选模型保留，避免擅自改变用户明确选择。不自动写配置或注册计划。
- 视觉/竞品：本次无需新视觉或竞品参考；沿用当前界面。
- 边界：不改变路由、请求提示词、沙盒与重试，不把请求模型当作实际响应模型。
- 验收：新配置及空模型默认 GPT-6 Luna；旧默认可迁移预填并提示；自定义值恢复；下拉展示已核实模型标识，build/verify 通过。
- 执行：沿用用户批准 gpt-6-luna / max，单开发聊天、小规模修订，不使用子代理或桌面控制；约一轮实现及验证，估计低用量。开发聊天 01a10130-e18a-7860-8690-0ed3fc4a1593 已确认空闲。
- 交付：保持固定 `dist/CodexQuotaWaker.exe` 与原启动器；保留尚未写回正式入口的 DPI 修复。原 EXE 占用时不要终止用户进程，不交付新入口，明确待关闭后更新。

## 实施与验收

- 本机目录核实：`codex-cli 0.160.0` 的只读 `codex debug models` 返回 4 个可见且 API 可用的 GPT-6 slug：`gpt-6-luna`、`gpt-6-sol`、`gpt-6.1-sol`、`gpt-6-astra`。
- 实现：默认/空配置回退改为 `gpt-6-luna`，ComboBox 仅列出上述本机目录型号但保留自由输入。未确认过的旧默认 `gpt-5.6-luna` 只迁移为界面草稿，明确提示保存并启用后生效；后台仍读取已保存配置。用户保存模型选择后写入确认标记，防止以后将其明确选回的 `gpt-5.6-luna` 再次迁移；其他自定义模型不变。
- 验证：`scripts/build.ps1` 与 `scripts/verify.ps1` 在固定 `dist/CodexQuotaWaker.exe` 路径通过；`ProductVersion=0.14.0`，`RequestModelDefaultsAndMigration=True`、`PresentationGeometryRegression=True`、`Result=PASS`、`TaskRegistration=PASS`、`Verification=PASS`。版本按 `feat` 增加 MINOR 为 `0.14.0`。
- 安全边界：构建期间 PID 33536 已自然退出；未结束用户进程，未创建版本号启动入口；没有修改用户配置、任务或发起 Codex 请求，也未启动桌面。正式 EXE 包含保留的 DPI manifest 改动。
- Lifecycle：本次实现与正式路径验收结果已发送至需求任务 `01a1012e-b555-7490-a340-5264a4da3c7b`；最终序列化标记补测后的正式路径复验也已回报。工具回执匹配 threadId 且 `isError=false`。
