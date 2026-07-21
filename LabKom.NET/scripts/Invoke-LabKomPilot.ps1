[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string[]]$ComputerName,

    [Parameter(Mandatory)]
    [string]$TeacherHost,

    [pscredential]$Credential,

    [ValidateRange(1, 1440)]
    [int]$DurationMinutes = 15,

    [ValidateRange(1, 60)]
    [int]$TelemetryIntervalSeconds = 2,

    [ValidateRange(100, 30000)]
    [int]$MaximumP95LatencyMs = 1500,

    [string]$TelemetryCsv,

    [string]$OutputDirectory = (Join-Path $env:USERPROFILE "Documents\LabKom\Pilot")
)

$ErrorActionPreference = "Stop"
$targets = @($ComputerName | ForEach-Object { $_.Trim() } |
    Where-Object { $_ } | Sort-Object -Unique)
if ($targets.Count -lt 5 -or $targets.Count -gt 40) {
    throw "Pilot wajib memakai 5-40 nama PC unik; diterima $($targets.Count)."
}

New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$startedAt = [DateTimeOffset]::UtcNow
$sessionId = $startedAt.ToString("yyyyMMdd-HHmmss")
$sessionDirectory = Join-Path $OutputDirectory "pilot-$sessionId"
New-Item -ItemType Directory -Path $sessionDirectory -Force | Out-Null

Write-Host "Preflight $($targets.Count) PC menuju $TeacherHost`:41235..."
$remoteCheck = {
    param($Teacher)

    $service = Get-CimInstance Win32_Service -Filter "Name='LabKomStudentAgent'" -ErrorAction SilentlyContinue
    $agentPath = Join-Path $env:ProgramFiles "LabKom\Student\Agent\LabKom.Student.Agent.exe"
    $desktopPath = Join-Path $env:ProgramFiles "LabKom\Student\Desktop\LabKom.Student.Desktop.exe"
    $credentialPath = Join-Path $env:ProgramData "LabKom\Security\device.credential"
    $tcpReady = $false
    try {
        $tcpReady = Test-NetConnection -ComputerName $Teacher -Port 41235 `
            -InformationLevel Quiet -WarningAction SilentlyContinue
    }
    catch {
        $tcpReady = $false
    }

    $version = $null
    if (Test-Path -LiteralPath $agentPath) {
        $version = [Diagnostics.FileVersionInfo]::GetVersionInfo($agentPath).ProductVersion
    }
    $systemDrive = Get-CimInstance Win32_LogicalDisk -Filter "DeviceID='$($env:SystemDrive)'" `
        -ErrorAction SilentlyContinue
    $diskFree = if ($systemDrive) { [long]$systemDrive.FreeSpace } else { 0 }

    [pscustomobject]@{
        PcName = $env:COMPUTERNAME
        ServiceInstalled = $null -ne $service
        ServiceRunning = $service.State -eq "Running"
        ServiceStartMode = $service.StartMode
        AgentExists = Test-Path -LiteralPath $agentPath
        DesktopExists = Test-Path -LiteralPath $desktopPath
        DesktopRunning = $null -ne (Get-Process -Name "LabKom.Student.Desktop" -ErrorAction SilentlyContinue)
        DeviceCredentialExists = Test-Path -LiteralPath $credentialPath
        TeacherTcp41235 = [bool]$tcpReady
        Version = $version
        SystemDiskFreeBytes = $diskFree
        CheckedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
    }
}

$invokeParameters = @{
    ComputerName = $targets
    ScriptBlock = $remoteCheck
    ArgumentList = @($TeacherHost)
    ThrottleLimit = 40
    ErrorAction = "SilentlyContinue"
}
if ($Credential) {
    $invokeParameters.Credential = $Credential
}
$rawPreflight = @(Invoke-Command @invokeParameters)
$preflight = foreach ($target in $targets) {
    $found = $rawPreflight | Where-Object {
        $_.PcName -eq $target -or $_.PSComputerName -eq $target
    } | Select-Object -First 1
    if ($found) {
        $found | Select-Object PcName, ServiceInstalled, ServiceRunning,
            ServiceStartMode, AgentExists, DesktopExists, DesktopRunning,
            DeviceCredentialExists, TeacherTcp41235, Version,
            SystemDiskFreeBytes, CheckedAtUtc
    }
    else {
        [pscustomobject]@{
            PcName = $target
            ServiceInstalled = $false
            ServiceRunning = $false
            ServiceStartMode = $null
            AgentExists = $false
            DesktopExists = $false
            DesktopRunning = $false
            DeviceCredentialExists = $false
            TeacherTcp41235 = $false
            Version = $null
            SystemDiskFreeBytes = 0
            CheckedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
        }
    }
}
$preflight | Export-Csv (Join-Path $sessionDirectory "preflight.csv") `
    -NoTypeInformation -Encoding UTF8

$duration = [TimeSpan]::FromMinutes($DurationMinutes)
$deadline = [DateTimeOffset]::UtcNow + $duration
while ([DateTimeOffset]::UtcNow -lt $deadline) {
    $remaining = $deadline - [DateTimeOffset]::UtcNow
    $elapsed = $duration - $remaining
    $percent = [Math]::Min(100, 100 * $elapsed.TotalSeconds / $duration.TotalSeconds)
    Write-Progress -Activity "Pilot LabKom $($targets.Count) PC" `
        -Status ("Sisa {0:hh\:mm\:ss}" -f $remaining) `
        -PercentComplete $percent
    Start-Sleep -Seconds ([Math]::Min(5, [Math]::Max(1, [int]$remaining.TotalSeconds)))
}
Write-Progress -Activity "Pilot LabKom" -Completed
$endedAt = [DateTimeOffset]::UtcNow

if ([string]::IsNullOrWhiteSpace($TelemetryCsv)) {
    $telemetryDirectory = Join-Path $env:LOCALAPPDATA "LabKom\Telemetry"
    $candidate = Get-ChildItem -LiteralPath $telemetryDirectory -Filter "telemetry-*.csv" `
        -File -ErrorAction SilentlyContinue |
        Where-Object { $_.LastWriteTimeUtc -ge $startedAt.UtcDateTime } |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 1
    if (-not $candidate) {
        throw "CSV telemetry sesi ini tidak ditemukan di $telemetryDirectory."
    }
    $TelemetryCsv = $candidate.FullName
}
if (-not (Test-Path -LiteralPath $TelemetryCsv)) {
    throw "CSV telemetry tidak ditemukan: $TelemetryCsv"
}

function Get-Percentile {
    param([double[]]$Values, [double]$Percentile)
    if (-not $Values -or $Values.Count -eq 0) { return 0 }
    $sorted = @($Values | Sort-Object)
    $index = [Math]::Max(0, [Math]::Ceiling($sorted.Count * $Percentile) - 1)
    return [double]$sorted[$index]
}

$targetSet = @{}
foreach ($target in $targets) { $targetSet[$target.ToLowerInvariant()] = $true }
$rows = @(Import-Csv -LiteralPath $TelemetryCsv | Where-Object {
    $sampleTime = [DateTimeOffset]::Parse($_.sampleUtc)
    $targetSet.ContainsKey($_.pcName.ToLowerInvariant()) -and
        $sampleTime -ge $startedAt -and $sampleTime -le $endedAt
})

$minimumSamples = [Math]::Max(
    1,
    [Math]::Floor($duration.TotalSeconds / $TelemetryIntervalSeconds * 0.75))
$telemetryResults = foreach ($target in $targets) {
    $samples = @($rows | Where-Object { $_.pcName -eq $target } |
        Sort-Object { [DateTimeOffset]::Parse($_.sampleUtc) })
    $latencies = @($samples | ForEach-Object { [double]$_.latencyMs })
    $gaps = @()
    for ($index = 1; $index -lt $samples.Count; $index++) {
        $previous = [DateTimeOffset]::Parse($samples[$index - 1].sampleUtc)
        $current = [DateTimeOffset]::Parse($samples[$index].sampleUtc)
        $gaps += ($current - $previous).TotalSeconds
    }
    $p95 = Get-Percentile -Values $latencies -Percentile 0.95
    $maxGap = if ($gaps.Count) { ($gaps | Measure-Object -Maximum).Maximum } else { 0 }
    $pre = $preflight | Where-Object { $_.PcName -eq $target } | Select-Object -First 1
    $samplePass = $samples.Count -ge $minimumSamples
    $gapPass = $samples.Count -gt 1 -and $maxGap -le ($TelemetryIntervalSeconds * 4)
    $latencyPass = $p95 -le $MaximumP95LatencyMs
    $preflightPass = $pre.ServiceRunning -and $pre.AgentExists -and
        $pre.DeviceCredentialExists -and $pre.TeacherTcp41235

    [pscustomobject]@{
        PcName = $target
        Passed = $preflightPass -and $samplePass -and $gapPass -and $latencyPass
        ServiceRunning = $pre.ServiceRunning
        TeacherTcp41235 = $pre.TeacherTcp41235
        DeviceCredentialExists = $pre.DeviceCredentialExists
        Version = $pre.Version
        DesktopRunning = $pre.DesktopRunning
        Samples = $samples.Count
        MinimumSamples = $minimumSamples
        P95LatencyMs = [Math]::Round($p95, 1)
        MaximumGapSeconds = [Math]::Round([double]$maxGap, 1)
        AverageCpuPercent = if ($samples.Count) {
            [Math]::Round(($samples.cpuPercent | Measure-Object -Average).Average, 1)
        } else { 0 }
        MaximumMemoryPercent = if ($samples.Count) {
            [Math]::Round((@($samples | ForEach-Object {
                100 * [double]$_.memoryUsedBytes / [double]$_.memoryTotalBytes
            }) | Measure-Object -Maximum).Maximum, 1)
        } else { 0 }
        CriticalSamples = @($samples | Where-Object { $_.health -eq "Critical" }).Count
    }
}

$telemetryResults | Export-Csv (Join-Path $sessionDirectory "telemetry-summary.csv") `
    -NoTypeInformation -Encoding UTF8
Copy-Item -LiteralPath $TelemetryCsv -Destination (Join-Path $sessionDirectory "telemetry-raw.csv")

$failed = @($telemetryResults | Where-Object { -not $_.Passed })
$summary = [ordered]@{
    SessionId = $sessionId
    StartedAtUtc = $startedAt.ToString("O")
    EndedAtUtc = $endedAt.ToString("O")
    RequestedDevices = $targets.Count
    PassedDevices = $targets.Count - $failed.Count
    FailedDevices = $failed.Count
    Passed = $failed.Count -eq 0
    TelemetryCsv = (Resolve-Path -LiteralPath $TelemetryCsv).Path
    ReportDirectory = (Resolve-Path -LiteralPath $sessionDirectory).Path
}
$summary | ConvertTo-Json -Depth 4 | Set-Content `
    (Join-Path $sessionDirectory "summary.json") -Encoding UTF8

$telemetryResults | Format-Table PcName, Passed, Samples, P95LatencyMs,
    MaximumGapSeconds, AverageCpuPercent, CriticalSamples -AutoSize
Write-Host "Report: $sessionDirectory"
if ($failed.Count -gt 0) {
    [Console]::Error.WriteLine(
        "$($failed.Count) dari $($targets.Count) PC gagal kriteria pilot.")
    exit 2
}

Write-Host "PASS: seluruh $($targets.Count) PC memenuhi kriteria pilot."
exit 0
