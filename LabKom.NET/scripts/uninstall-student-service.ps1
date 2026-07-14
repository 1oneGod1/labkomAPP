param(
    [string]$ServiceName = 'LabKomStudentAgent'
)

$ErrorActionPreference = 'Stop'

if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host "[!] Jalankan sebagai Administrator." -ForegroundColor Red
    exit 1
}

$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if (-not $existing) {
    Write-Host "Service '$ServiceName' tidak ditemukan." -ForegroundColor Yellow
    exit 0
}

Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
sc.exe delete $ServiceName | Out-Null
Write-Host "[v] Service '$ServiceName' dihapus." -ForegroundColor Green
