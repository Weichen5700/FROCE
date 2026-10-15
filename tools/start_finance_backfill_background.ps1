param(
    [ValidateRange(1990, 2200)]
    [int]$LatestYear = ((Get-Date).Year - 1),

    [ValidateRange(1, 20)]
    [int]$Years = 7,

    [ValidateRange(1, 5)]
    [int]$BatchSize = 3,

    [ValidateRange(250, 10000)]
    [int]$DelayMilliseconds = 1000,

    [switch]$Worker,

    [switch]$Wait
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $PSScriptRoot "FinanceBackfill\FinanceBackfill.csproj"
$assemblyPath = Join-Path $PSScriptRoot "FinanceBackfill\bin\Release\net10.0\FinanceBackfill.dll"
$appDataDirectory = Join-Path $repoRoot "RoceTool.Web\App_Data"
$buildLogPath = Join-Path $appDataDirectory "finance-backfill-build.log"
$workerOutPath = Join-Path $appDataDirectory "finance-backfill-worker.out.log"
$workerErrorPath = Join-Path $appDataDirectory "finance-backfill-worker.err.log"
$launcherLockPath = Join-Path $appDataDirectory "finance-backfill-launcher.lock"

New-Item -ItemType Directory -Path $appDataDirectory -Force | Out-Null

if (-not $Worker -and -not $Wait) {
    $hostExecutable = (Get-Process -Id $PID).Path
    $scriptArgument = '"{0}"' -f $PSCommandPath
    $argumentLine = "-NoProfile -ExecutionPolicy Bypass -File $scriptArgument -Worker -LatestYear $LatestYear -Years $Years -BatchSize $BatchSize -DelayMilliseconds $DelayMilliseconds"
    $backgroundProcess = Start-Process -FilePath $hostExecutable `
        -ArgumentList $argumentLine `
        -WorkingDirectory $repoRoot `
        -WindowStyle Hidden `
        -RedirectStandardOutput $workerOutPath `
        -RedirectStandardError $workerErrorPath `
        -PassThru

    Write-Output "已在背景啟動金融股年度財報補齊，工作 PID=$($backgroundProcess.Id)。"
    Write-Output "進度與缺漏請查看：$(Join-Path $appDataDirectory 'finance-backfill.log')"
    exit 0
}

$launcherLock = $null
try {
    $launcherLock = [System.IO.File]::Open(
        $launcherLockPath,
        [System.IO.FileMode]::OpenOrCreate,
        [System.IO.FileAccess]::ReadWrite,
        [System.IO.FileShare]::None)
}
catch [System.IO.IOException] {
    throw "已有金融股年報補資料啟動程序，請查看 RoceTool.Web/App_Data/finance-backfill.log"
}

try {
$dotnet = Get-Command dotnet -ErrorAction Stop
Write-Output "建置金融股年報補資料工具。"
& $dotnet.Source build $projectPath -c Release -m:1 -v:minimal *> $buildLogPath
if ($LASTEXITCODE -ne 0) {
    Get-Content -LiteralPath $buildLogPath -Tail 40
    throw "工具建置失敗，詳細輸出請查看 $buildLogPath"
}

if (-not (Test-Path -LiteralPath $assemblyPath)) {
    throw "找不到建置輸出：$assemblyPath"
}

$arguments = @(
    $assemblyPath,
    "--latest-year", $LatestYear,
    "--years", $Years,
    "--batch-size", $BatchSize,
    "--delay-ms", $DelayMilliseconds
)
& $dotnet.Source @arguments
if ($LASTEXITCODE -ne 0) {
    throw "金融股年報補資料失敗，請查看 RoceTool.Web/App_Data/finance-backfill.log"
}
}
finally {
    if ($null -ne $launcherLock) { $launcherLock.Dispose() }
}
