param(
    [ValidateRange(1990, 2200)]
    [int]$LatestYear = ((Get-Date).Year - 1),

    [ValidateRange(1, 20)]
    [int]$Years = 7,

    [string]$BaseUrl = "http://127.0.0.1:5050",

    [switch]$Worker,

    [switch]$Wait
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

$repoRoot = Split-Path -Parent $PSScriptRoot
$webDirectory = Join-Path $repoRoot "RoceTool.Web"
$appDataDirectory = Join-Path $webDirectory "App_Data"
$logPath = Join-Path $appDataDirectory "market-scan-background.log"
$buildLogPath = Join-Path $appDataDirectory "market-scan-background-build.log"
$serverOutPath = Join-Path $appDataDirectory "market-scan-background-server.out.log"
$serverErrorPath = Join-Path $appDataDirectory "market-scan-background-server.err.log"
$lockPath = Join-Path $appDataDirectory "market-scan-background.lock"
$ownedServer = $null
$lockStream = $null
$scanCompleted = $false

function Write-Log([string]$Message) {
    $line = "{0:yyyy-MM-dd HH:mm:ss} {1}" -f (Get-Date), $Message
    Add-Content -LiteralPath $logPath -Value $line -Encoding UTF8
    if ($Wait -or $Worker) { Write-Host $line }
}

function Get-StatusName($Value) {
    if ($Value -is [string]) { return $Value }
    switch ([int]$Value) {
        0 { return "Idle" }
        1 { return "Running" }
        2 { return "Paused" }
        3 { return "Completed" }
        default { return "Unknown($Value)" }
    }
}

function Get-ScanStatus {
    Invoke-RestMethod -Method Get -Uri "$BaseUrl/api/market-scan/status" -TimeoutSec 10
}

function Normalize-BaseUrl([string]$Value) {
    $parsedUri = $null
    if (-not [Uri]::TryCreate($Value, [UriKind]::Absolute, [ref]$parsedUri) -or
        $parsedUri.Scheme -ne "http" -or -not $parsedUri.IsLoopback -or
        -not [string]::IsNullOrEmpty($parsedUri.UserInfo) -or
        -not [string]::IsNullOrEmpty($parsedUri.Query) -or
        -not [string]::IsNullOrEmpty($parsedUri.Fragment)) {
        throw "BaseUrl 僅接受本機 HTTP 位址，例如 http://127.0.0.1:5050。"
    }
    return $parsedUri.GetLeftPart([UriPartial]::Authority).TrimEnd("/")
}

function Test-ScanApi {
    try {
        $null = Get-ScanStatus
        return $true
    }
    catch {
        if ($null -ne $_.Exception.Response) { throw }
        return $false
    }
}

function Start-TemporaryWebHost {
    $dotnet = Get-Command dotnet -ErrorAction Stop
    $buildArguments = @(
        "build",
        (Join-Path $webDirectory "RoceTool.Web.csproj"),
        "-c", "Release",
        "-m:1",
        "--verbosity", "quiet"
    )

    Write-Log "本機網站未啟動，開始建置背景收集所需的網站 Host。"
    & $dotnet.Source @buildArguments *> $buildLogPath
    if ($LASTEXITCODE -ne 0) {
        Get-Content -LiteralPath $buildLogPath -Tail 40 | ForEach-Object { Write-Log "BUILD $_" }
        throw "網站建置失敗，詳細輸出請查看 $buildLogPath"
    }

    $assemblyPath = Join-Path $webDirectory "bin\Release\net10.0\RoceTool.Web.dll"
    if (-not (Test-Path -LiteralPath $assemblyPath)) {
        throw "找不到建置輸出：$assemblyPath"
    }

    $previousAppUrl = $env:AppUrl
    $previousOpenBrowser = $env:OpenBrowser
    try {
        $env:AppUrl = $BaseUrl
        $env:OpenBrowser = "false"
        $arguments = '"{0}"' -f $assemblyPath
        $script:ownedServer = Start-Process -FilePath $dotnet.Source `
            -ArgumentList $arguments `
            -WorkingDirectory $webDirectory `
            -WindowStyle Hidden `
            -RedirectStandardOutput $serverOutPath `
            -RedirectStandardError $serverErrorPath `
            -PassThru
    }
    finally {
        $env:AppUrl = $previousAppUrl
        $env:OpenBrowser = $previousOpenBrowser
    }

    Write-Log "已在背景啟動本次專用網站 Host，PID=$($ownedServer.Id)。"
    for ($attempt = 1; $attempt -le 60; $attempt++) {
        if (Test-ScanApi) { return }
        if ($ownedServer.HasExited) {
            throw "背景網站 Host 提前結束，請查看 $serverErrorPath"
        }
        Start-Sleep -Seconds 1
    }

    throw "等待本機掃描 API 啟動逾時，請查看 $serverOutPath 與 $serverErrorPath"
}

$BaseUrl = Normalize-BaseUrl $BaseUrl

if (-not $Worker -and -not $Wait) {
    $hostExecutable = (Get-Process -Id $PID).Path
    $scriptArgument = '"{0}"' -f $PSCommandPath
    $argumentLine = "-NoProfile -ExecutionPolicy Bypass -File $scriptArgument -Worker -LatestYear $LatestYear -Years $Years -BaseUrl `"$BaseUrl`""
    $worker = Start-Process -FilePath $hostExecutable `
        -ArgumentList $argumentLine `
        -WorkingDirectory $repoRoot `
        -WindowStyle Hidden `
        -PassThru

    Write-Output "已在背景啟動全市場年報收集，工作 PID=$($worker.Id)。"
    Write-Output "完成狀態與進度請查看：$logPath"
    exit 0
}

New-Item -ItemType Directory -Path $appDataDirectory -Force | Out-Null

try {
    try {
        $lockStream = [System.IO.File]::Open(
            $lockPath,
            [System.IO.FileMode]::OpenOrCreate,
            [System.IO.FileAccess]::ReadWrite,
            [System.IO.FileShare]::None)
    }
    catch [System.IO.IOException] {
        throw "另一個背景收集工具正在執行；請查看 $logPath"
    }

    Write-Log "收集工具啟動；目標年度 $LatestYear 起算 $Years 年。"

    if (-not (Test-ScanApi)) {
        Start-TemporaryWebHost
    }
    else {
        Write-Log "沿用已啟動的本機網站 Host。"
    }

    $progress = Get-ScanStatus
    $statusName = Get-StatusName $progress.status
    if ($statusName -eq "Running") {
        $actualEnd = $progress.rangeEndFiscalYear
        if ($null -eq $actualEnd) { $actualEnd = $progress.fiscalYear }
        $actualStart = $progress.rangeStartFiscalYear
        if ($null -eq $actualStart) { $actualStart = $actualEnd }
        Write-Log "已有掃描進行中（$actualEnd 至 $actualStart），不另開掃描，改為監看現有進度。"
    }
    else {
        $startUri = "$BaseUrl/api/market-scan/start-range/$LatestYear/$Years`?retryFailed=true"
        $progress = Invoke-RestMethod -Method Post -Uri $startUri -TimeoutSec 300
        Write-Log "已要求掃描 $LatestYear 起算 $Years 年；初始狀態=$(Get-StatusName $progress.status)。"
    }

    $lastCompleted = -1
    $lastStatus = ""
    while ($true) {
        $progress = Get-ScanStatus
        $statusName = Get-StatusName $progress.status
        if ($progress.completed -ne $lastCompleted -or $statusName -ne $lastStatus) {
            Write-Log ("狀態={0}，進度={1}/{2}，成功={3}，未取得={4}，目前年度={5}，股票={6}" -f `
                $statusName, $progress.completed, $progress.total, $progress.success, $progress.failed,
                $progress.fiscalYear, $progress.currentStockNo)
            $lastCompleted = $progress.completed
            $lastStatus = $statusName
        }

        if ($statusName -eq "Completed") {
            $scanCompleted = $true
            Write-Log "資料收集完成。"
            break
        }
        if ($statusName -eq "Paused") {
            throw "掃描已暫停，進度已保存在資料庫；重新執行此工具即可續跑。"
        }
        if ($statusName -notin @("Running", "Idle")) {
            throw "掃描狀態無法辨識：$statusName"
        }

        Start-Sleep -Seconds 10
    }
}
catch {
    Write-Log "錯誤：$($_.Exception.Message)"
    throw
}
finally {
    if ($null -ne $ownedServer) {
        if (-not $scanCompleted) {
            $pauseRequested = $false
            try {
                $null = Invoke-RestMethod -Method Post -Uri "$BaseUrl/api/market-scan/pause" -TimeoutSec 10
                $pauseRequested = $true
                Write-Log "本次專用 Host 即將停止，已保存掃描暫停狀態。"
            }
            catch {
                Write-Log "停止前無法寫入暫停狀態；重新啟動網站時背景工作可依資料庫狀態恢復。"
            }
            if ($pauseRequested) {
                # Let any in-flight MOPS request (45-second timeout) finish its database write.
                Start-Sleep -Seconds 50
            }
        }
        if (-not $ownedServer.HasExited) {
            Stop-Process -Id $ownedServer.Id -Force -ErrorAction SilentlyContinue
            Write-Log "已停止本次工具啟動的網站 Host。"
        }
    }
    if ($null -ne $lockStream) { $lockStream.Dispose() }
}
