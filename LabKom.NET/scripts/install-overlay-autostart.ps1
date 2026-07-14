# Daftarkan LabKom.Student.Overlay untuk auto-start saat user logon.
# Pakai Task Scheduler agar berjalan di Session 1 (interactive user session)
# dengan privilege user biasa.

param(
    [string]$TaskName = 'LabKomStudentOverlay',
    [string]$ExePath
)

$ErrorActionPreference = 'Stop'

if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host "[!] Jalankan sebagai Administrator." -ForegroundColor Red
    exit 1
}

if (-not $ExePath) {
    $root = Resolve-Path (Join-Path $PSScriptRoot '..')
    $candidate = Join-Path $root 'src\LabKom.Student.Overlay\bin\Release\net8.0-windows\LabKom.Student.Overlay.exe'
    if (-not (Test-Path $candidate)) {
        $candidate = Join-Path $root 'src\LabKom.Student.Overlay\bin\Debug\net8.0-windows\LabKom.Student.Overlay.exe'
    }
    if (-not (Test-Path $candidate)) {
        Write-Host "[!] LabKom.Student.Overlay.exe tidak ditemukan. Build dulu, atau berikan -ExePath." -ForegroundColor Red
        exit 1
    }
    $ExePath = $candidate
}

Write-Host "Mendaftarkan task: $TaskName" -ForegroundColor Cyan
Write-Host "Path           : $ExePath"

# Hapus task lama jika ada
schtasks /Query /TN $TaskName 2>$null | Out-Null
if ($LASTEXITCODE -eq 0) {
    schtasks /Delete /TN $TaskName /F | Out-Null
}

# Buat task: trigger ON LOGON, jalankan untuk semua user
$xml = @"
<?xml version="1.0" encoding="UTF-16"?>
<Task version="1.4" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
  <Triggers>
    <LogonTrigger>
      <Enabled>true</Enabled>
    </LogonTrigger>
  </Triggers>
  <Principals>
    <Principal id="Author">
      <GroupId>S-1-5-32-545</GroupId>
      <RunLevel>LeastPrivilege</RunLevel>
    </Principal>
  </Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <AllowHardTerminate>false</AllowHardTerminate>
    <StartWhenAvailable>true</StartWhenAvailable>
    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
    <IdleSettings>
      <StopOnIdleEnd>false</StopOnIdleEnd>
      <RestartOnIdle>false</RestartOnIdle>
    </IdleSettings>
    <AllowStartOnDemand>true</AllowStartOnDemand>
    <Enabled>true</Enabled>
    <Hidden>true</Hidden>
    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
    <Priority>7</Priority>
  </Settings>
  <Actions Context="Author">
    <Exec>
      <Command>$ExePath</Command>
    </Exec>
  </Actions>
</Task>
"@

$tmpFile = [System.IO.Path]::GetTempFileName()
[System.IO.File]::WriteAllText($tmpFile, $xml, [System.Text.Encoding]::Unicode)
schtasks /Create /TN $TaskName /XML $tmpFile /F | Out-Null
Remove-Item $tmpFile

Write-Host "[v] Task '$TaskName' terdaftar. Akan auto-start di logon berikutnya." -ForegroundColor Green
Write-Host "    Untuk uji sekarang: Start-ScheduledTask -TaskName $TaskName"
