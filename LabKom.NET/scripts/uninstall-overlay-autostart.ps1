param([string]$TaskName = 'LabKomStudentOverlay')

$ErrorActionPreference = 'Stop'

if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host "[!] Jalankan sebagai Administrator." -ForegroundColor Red
    exit 1
}

schtasks /Query /TN $TaskName 2>$null | Out-Null
if ($LASTEXITCODE -ne 0) {
    Write-Host "Task '$TaskName' tidak ditemukan." -ForegroundColor Yellow
    exit 0
}

schtasks /End /TN $TaskName 2>$null | Out-Null
schtasks /Delete /TN $TaskName /F | Out-Null
Write-Host "[v] Task '$TaskName' dihapus." -ForegroundColor Green
