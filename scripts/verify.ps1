[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$exePath = Join-Path $projectRoot 'dist\CodexQuotaWaker.exe'
$reportPath = Join-Path $projectRoot 'dist\self-check.txt'
$taskXmlPath = Join-Path $projectRoot 'dist\integration-task.xml'
$testTaskName = 'CodexQuotaWaker-IntegrationTest'

& (Join-Path $PSScriptRoot 'build.ps1')

if (-not (Test-Path -LiteralPath $exePath)) {
    throw "构建产物不存在：$exePath"
}

$process = Start-Process -FilePath $exePath -ArgumentList @('--self-check', $reportPath) -Wait -PassThru -WindowStyle Hidden
if (-not (Test-Path -LiteralPath $reportPath)) {
    throw '自检没有生成报告。'
}

$report = Get-Content -LiteralPath $reportPath -Raw
$report

if ($process.ExitCode -ne 0 -or $report -notmatch 'Result=PASS') {
    throw "自检失败，退出码：$($process.ExitCode)"
}

if (Get-ScheduledTask -TaskName $testTaskName -ErrorAction SilentlyContinue) {
    throw "存在同名测试任务，未覆盖：$testTaskName"
}

try {
    $export = Start-Process -FilePath $exePath -ArgumentList @('--export-task-xml', $taskXmlPath) -Wait -PassThru -WindowStyle Hidden
    if ($export.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $taskXmlPath)) {
        throw '未能导出计划任务 XML。'
    }

    schtasks.exe /Create /TN $testTaskName /XML $taskXmlPath /F | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Windows 拒绝注册测试计划任务，退出码：$LASTEXITCODE"
    }

    $task = Get-ScheduledTask -TaskName $testTaskName -ErrorAction Stop
    if (-not $task.Settings.WakeToRun) {
        throw '测试计划任务未启用 WakeToRun。'
    }
    if (-not $task.Settings.StartWhenAvailable) {
        throw '测试计划任务未启用 StartWhenAvailable。'
    }
    if ($task.Triggers.Count -ne 2) {
        throw "有限序列和插入唤醒应有两个触发器，实际数量：$($task.Triggers.Count)"
    }
    if ($task.Triggers[0].Repetition.Interval -ne 'PT2H5M') {
        throw "有限序列间隔错误：$($task.Triggers[0].Repetition.Interval)"
    }
    if ($task.Triggers[0].Repetition.Duration -ne 'PT4H11M') {
        throw "三次测试序列的持续边界错误：$($task.Triggers[0].Repetition.Duration)"
    }
    if (-not $task.Triggers[1].StartBoundary) {
        throw '插入唤醒没有生成独立的单次触发器。'
    }
    if ($task.Actions.Execute -ne $exePath) {
        throw "测试计划任务指向了错误的 EXE：$($task.Actions.Execute)"
    }

    Write-Output 'TaskRegistration=PASS'
}
finally {
    if (Get-ScheduledTask -TaskName $testTaskName -ErrorAction SilentlyContinue) {
        Unregister-ScheduledTask -TaskName $testTaskName -Confirm:$false
    }
    if (Test-Path -LiteralPath $taskXmlPath) {
        Remove-Item -LiteralPath $taskXmlPath -Force
    }
}

Write-Output 'Verification=PASS'
