[CmdletBinding()]
param(
    [string]$ExePath
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$canonicalExePath = Join-Path $projectRoot 'dist\CodexQuotaWaker.exe'
if ([string]::IsNullOrWhiteSpace($ExePath)) {
    $exePath = $canonicalExePath
}
else {
    if (-not [System.IO.Path]::IsPathRooted($ExePath)) {
        $ExePath = Join-Path $projectRoot $ExePath
    }
    $exePath = [System.IO.Path]::GetFullPath($ExePath)
}
$reportPath = Join-Path $projectRoot 'dist\self-check.txt'
$taskXmlPath = Join-Path $projectRoot 'dist\integration-task.xml'
$testTaskName = 'CodexQuotaWaker-IntegrationTest'

& (Join-Path $PSScriptRoot 'build.ps1') -OutputPath $exePath

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

    Unregister-ScheduledTask -TaskName $testTaskName -Confirm:$false
    $pausedExport = Start-Process -FilePath $exePath -ArgumentList @('--export-paused-task-xml', $taskXmlPath) -Wait -PassThru -WindowStyle Hidden
    if ($pausedExport.ExitCode -ne 0) { throw '暂停计划 XML 导出失败。' }
    [xml]$pausedXml = Get-Content -LiteralPath $taskXmlPath -Raw
    $expectedTimes = @($pausedXml.SelectNodes("//*[local-name()='TimeTrigger']/*[local-name()='StartBoundary']") | ForEach-Object { [DateTime]::Parse($_.InnerText).ToString('s') })
    schtasks.exe /Create /TN $testTaskName /XML $taskXmlPath /F | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Windows 拒绝注册暂停计划验证任务。' }
    $pausedTask = Get-ScheduledTask -TaskName $testTaskName -ErrorAction Stop
    $actualTimes = @($pausedTask.Triggers | ForEach-Object { [DateTime]::Parse($_.StartBoundary).ToString('s') })
    if ($actualTimes.Count -ne 48 -or ($actualTimes -join '|') -ne ($expectedTimes -join '|')) { throw 'Windows 暂停计划触发时间与预览定义不一致。' }
    if (@($pausedTask.Triggers | Where-Object { $_.Repetition.Interval }).Count -ne 0) { throw '暂停模式错误使用了固定重复触发器。' }
    if (-not $pausedTask.Settings.WakeToRun -or $pausedTask.Actions.Execute -ne $exePath) { throw '暂停模式任务属性或 EXE 不一致。' }
    $expectedArguments = $pausedXml.SelectSingleNode("//*[local-name()='Actions']/*[local-name()='Exec']/*[local-name()='Arguments']").InnerText
    if ($pausedTask.Actions.Arguments -ne $expectedArguments) { throw '暂停模式计划版本参数不一致。' }
    Write-Output 'PausedTaskRegistration=PASS;Triggers=48;PreviewTimesMatch=True;PlanVersionMatch=True'
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
