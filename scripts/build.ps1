[CmdletBinding()]
param(
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$sourcePath = Join-Path $projectRoot 'src\CodexQuotaWaker.cs'
$manifestPath = Join-Path $projectRoot 'src\CodexQuotaWaker.manifest'
$canonicalOutputPath = Join-Path $projectRoot 'dist\CodexQuotaWaker.exe'
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $outputPath = $canonicalOutputPath
}
else {
    if (-not [System.IO.Path]::IsPathRooted($OutputPath)) {
        $OutputPath = Join-Path $projectRoot $OutputPath
    }
    $outputPath = [System.IO.Path]::GetFullPath($OutputPath)
}
$outputDirectory = Split-Path -Parent $outputPath
$compilerCandidates = @(
    'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe',
    'C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe'
)
$compilerPath = $compilerCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1

if (-not $compilerPath) {
    throw '找不到 Windows .NET Framework C# 编译器。请确认系统已启用 .NET Framework 4.8。'
}
if (-not (Test-Path -LiteralPath $manifestPath)) {
    throw "找不到应用 manifest：$manifestPath"
}

New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null

$arguments = @(
    '/nologo',
    '/target:winexe',
    '/optimize+',
    '/platform:anycpu',
    "/out:$outputPath",
    "/win32manifest:$manifestPath",
    '/reference:System.dll',
    '/reference:System.Core.dll',
    '/reference:System.Drawing.dll',
    '/reference:System.Windows.Forms.dll',
    '/reference:System.Web.Extensions.dll',
    '/reference:System.Xml.dll',
    $sourcePath
)

& $compilerPath @arguments
if ($LASTEXITCODE -ne 0) {
    throw "编译失败，退出码：$LASTEXITCODE"
}

$file = Get-Item -LiteralPath $outputPath
[pscustomobject]@{
    Output = $file.FullName
    SizeBytes = $file.Length
    Compiler = $compilerPath
    IsCanonicalOutput = ($file.FullName -ieq $canonicalOutputPath)
} | Format-List
