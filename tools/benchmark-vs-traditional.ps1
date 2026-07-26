param(
  [int]$DurationSec = 30,
  [int]$WarmupSec = 12,
  [string]$Root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
)

$ErrorActionPreference = 'Continue'
$LiteExe = Join-Path $Root 'dist\tray\SyncLightLite.exe'
$TraditionalExe = 'C:\Program Files\SyncLight\SyncLight.exe'
$LogicalProcessors = [Environment]::ProcessorCount
$ReportDir = Join-Path $Root 'benchmark'
New-Item -ItemType Directory -Force -Path $ReportDir | Out-Null
$Stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$ReportJson = Join-Path $ReportDir "benchmark-$Stamp.json"
$ReportTxt = Join-Path $ReportDir "benchmark-$Stamp.txt"

function Get-InterestingProcesses {
  Get-CimInstance Win32_Process | Where-Object {
    $_.Name -in @('SyncLight.exe','SyncLightLite.exe','node.exe') -and (
      $_.Name -ne 'node.exe' -or $_.CommandLine -match 'SyncLightLite|src\\live\.js|src/live\.js'
    )
  } | Select-Object ProcessId, ParentProcessId, Name, CommandLine, ExecutablePath
}

function Stop-Lite {
  $procs = Get-InterestingProcesses | Where-Object { $_.Name -in @('SyncLightLite.exe','node.exe') }
  foreach ($p in $procs) {
    try { Stop-Process -Id $p.ProcessId -Force -ErrorAction SilentlyContinue } catch {}
  }
  Start-Sleep -Seconds 2
}

function Stop-Traditional {
  Get-Process SyncLight -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
  Start-Sleep -Seconds 3
}

function Snapshot-Group([string]$Kind) {
  $procs = @()
  if ($Kind -eq 'lite') {
    $procs = @(Get-InterestingProcesses | Where-Object { $_.Name -in @('SyncLightLite.exe','node.exe') })
  } elseif ($Kind -eq 'traditional') {
    $ids = @(Get-CimInstance Win32_Process | Where-Object { $_.Name -eq 'SyncLight.exe' } | Select-Object -ExpandProperty ProcessId)
    if ($ids.Count -gt 0) { $procs = @(Get-Process -Id $ids -ErrorAction SilentlyContinue) }
  }

  $items = @()
  foreach ($p in $procs) {
    try {
      if ($p.PSObject.Properties.Name -contains 'ProcessId') {
        $gp = Get-Process -Id $p.ProcessId -ErrorAction Stop
        $cmd = $p.CommandLine
      } else {
        $gp = $p
        $cmd = (Get-CimInstance Win32_Process -Filter "ProcessId=$($p.Id)").CommandLine
      }
      $items += [pscustomobject]@{
        pid = $gp.Id
        name = $gp.ProcessName
        cpuSeconds = if ($null -ne $gp.CPU) { [double]$gp.CPU } else { 0.0 }
        workingSetBytes = [int64]$gp.WorkingSet64
        privateBytes = [int64]$gp.PrivateMemorySize64
        commandLine = $cmd
      }
    } catch {}
  }
  return @($items)
}

function Measure-Group([string]$Name, [string]$Kind, [int]$Duration) {
  $before = Snapshot-Group $Kind
  $t0 = Get-Date
  Start-Sleep -Seconds $Duration
  $after = Snapshot-Group $Kind
  $t1 = Get-Date
  $elapsed = ($t1 - $t0).TotalSeconds
  $beforeMap = @{}
  foreach ($p in $before) { $beforeMap[[string]$p.pid] = $p.cpuSeconds }
  $cpuDelta = 0.0
  foreach ($p in $after) {
    $old = 0.0
    if ($beforeMap.ContainsKey([string]$p.pid)) { $old = [double]$beforeMap[[string]$p.pid] }
    $delta = [double]$p.cpuSeconds - $old
    if ($delta -gt 0) { $cpuDelta += $delta }
  }
  $ws = ($after | Measure-Object -Property workingSetBytes -Sum).Sum
  $priv = ($after | Measure-Object -Property privateBytes -Sum).Sum
  return [pscustomobject]@{
    name = $Name
    kind = $Kind
    elapsedSec = [math]::Round($elapsed, 2)
    processCount = @($after).Count
    cpuDeltaSeconds = [math]::Round($cpuDelta, 3)
    cpuPercentOfOneCore = [math]::Round(($cpuDelta / $elapsed) * 100, 2)
    cpuPercentTotalMachine = [math]::Round(($cpuDelta / $elapsed / $LogicalProcessors) * 100, 2)
    workingSetMB = [math]::Round(($ws / 1MB), 1)
    privateMB = [math]::Round(($priv / 1MB), 1)
    processes = $after
  }
}

$events = @()
$results = @()
$events += "logicalProcessors=$LogicalProcessors"
$events += "durationSec=$DurationSec warmupSec=$WarmupSec"

# Controlled run: Lite first, then original SyncLight, then restore Lite.
Stop-Traditional
Stop-Lite

$events += "starting Lite: $LiteExe --minimized"
$liteProc = Start-Process -FilePath $LiteExe -ArgumentList '--minimized' -WorkingDirectory (Split-Path $LiteExe) -PassThru
Start-Sleep -Seconds $WarmupSec
$events += "lite processes: $((Snapshot-Group lite | ForEach-Object { $_.name + ':' + $_.pid }) -join ', ')"
$results += Measure-Group -Name 'SyncLightLite' -Kind 'lite' -Duration $DurationSec
Stop-Lite

$events += "starting Traditional: $TraditionalExe"
$tradProc = Start-Process -FilePath $TraditionalExe -WorkingDirectory (Split-Path $TraditionalExe) -PassThru
Start-Sleep -Seconds $WarmupSec
$events += "traditional processes: $((Snapshot-Group traditional | ForEach-Object { $_.name + ':' + $_.pid }) -join ', ')"
$results += Measure-Group -Name 'SyncLight original' -Kind 'traditional' -Duration $DurationSec
Stop-Traditional

$events += "restoring Lite"
Start-Process -FilePath $LiteExe -ArgumentList '--minimized' -WorkingDirectory (Split-Path $LiteExe) | Out-Null
Start-Sleep -Seconds 5
$events += "restored processes: $((Snapshot-Group lite | ForEach-Object { $_.name + ':' + $_.pid }) -join ', ')"

$lite = $results | Where-Object { $_.kind -eq 'lite' } | Select-Object -First 1
$trad = $results | Where-Object { $_.kind -eq 'traditional' } | Select-Object -First 1
$summary = [pscustomobject]@{
  timestamp = (Get-Date).ToString('s')
  logicalProcessors = $LogicalProcessors
  durationSec = $DurationSec
  warmupSec = $WarmupSec
  results = $results
  gains = if ($lite -and $trad -and $lite.processCount -gt 0 -and $trad.processCount -gt 0) { [pscustomobject]@{
    ramWorkingSetSavedMB = [math]::Round($trad.workingSetMB - $lite.workingSetMB, 1)
    ramWorkingSetReductionPercent = if ($trad.workingSetMB -gt 0) { [math]::Round((1 - ($lite.workingSetMB / $trad.workingSetMB)) * 100, 1) } else { $null }
    privateMemorySavedMB = [math]::Round($trad.privateMB - $lite.privateMB, 1)
    privateMemoryReductionPercent = if ($trad.privateMB -gt 0) { [math]::Round((1 - ($lite.privateMB / $trad.privateMB)) * 100, 1) } else { $null }
    cpuTotalMachineSavedPercentPoints = [math]::Round($trad.cpuPercentTotalMachine - $lite.cpuPercentTotalMachine, 2)
    cpuReductionPercent = if ($trad.cpuPercentTotalMachine -gt 0) { [math]::Round((1 - ($lite.cpuPercentTotalMachine / $trad.cpuPercentTotalMachine)) * 100, 1) } else { $null }
    processCountReduction = $trad.processCount - $lite.processCount
  }} else { $null }
  events = $events
}

$summary | ConvertTo-Json -Depth 8 | Set-Content -Encoding UTF8 $ReportJson
$lines = @()
$lines += "SyncLightLite vs SyncLight original benchmark - $(Get-Date)"
$lines += "Logical processors: $LogicalProcessors"
$lines += "Window: $DurationSec sec after $WarmupSec sec warmup"
$lines += ""
foreach ($r in $results) {
  $lines += "$($r.name): processes=$($r.processCount), CPU total-machine=$($r.cpuPercentTotalMachine)%, CPU one-core=$($r.cpuPercentOfOneCore)%, WorkingSet=$($r.workingSetMB) MB, Private=$($r.privateMB) MB"
}
if ($summary.gains) {
  $g = $summary.gains
  $lines += ""
  $lines += "Gains: RAM WS saved=$($g.ramWorkingSetSavedMB) MB ($($g.ramWorkingSetReductionPercent)% reduction), private saved=$($g.privateMemorySavedMB) MB ($($g.privateMemoryReductionPercent)% reduction), CPU saved=$($g.cpuTotalMachineSavedPercentPoints) percentage points ($($g.cpuReductionPercent)% reduction), processes reduced=$($g.processCountReduction)"
}
$lines += ""
$lines += "Events:"
$lines += $events
$lines | Set-Content -Encoding UTF8 $ReportTxt

Write-Host "REPORT_JSON=$ReportJson"
Write-Host "REPORT_TXT=$ReportTxt"
Get-Content $ReportTxt
