param(
    [ValidateRange(1, 10000)][int]$Bots = 100,
    [ValidateRange(1, 10000)][int]$Rooms = 20,
    [ValidateRange(0, 86400)][int]$DurationSeconds = 600,
    [ValidateRange(1, 65535)][int]$Port = 7777,
    [ValidateRange(1, 1000)][int]$SpawnRate = 10,
    [ValidateRange(10, 240)][int]$BotFps = 30
)

$projectRoot = Split-Path -Parent $PSScriptRoot
$executable = Join-Path $projectRoot 'Builds\Windows\PixelArena.exe'
if (-not (Test-Path -LiteralPath $executable)) {
    throw "Build not found: $executable. Run 'Pixel Arena > Build Windows MVP' first."
}

$runId = Get-Date -Format 'yyyyMMdd-HHmmss'
$logRoot = Join-Path $projectRoot "Builds\Windows\StressLogs\$runId"
New-Item -ItemType Directory -Force -Path $logRoot | Out-Null
$serverLog = Join-Path $logRoot 'server.log'
$arguments = @(
    '-batchmode', '-nographics', '-logFile', $serverLog,
    '--stress-server', '--bots', $Bots, '--rooms', $Rooms,
    '--duration', $DurationSeconds, '--port', $Port,
    '--spawn-rate', $SpawnRate, '--bot-fps', $BotFps,
    '--run-id', $runId
)

Write-Host "Starting Pixel Arena stress test: $Bots bots, $Rooms rooms, $DurationSeconds seconds."
Write-Host "Server log: $serverLog"
$process = Start-Process -FilePath $executable -ArgumentList $arguments -PassThru -Wait -NoNewWindow
Write-Host "Stress test finished with exit code $($process.ExitCode)."
exit $process.ExitCode
