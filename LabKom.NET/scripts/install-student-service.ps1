# Install LabKom Student Agent sebagai Windows Service.
# Jalankan PowerShell sebagai Administrator.

param(
    [string]$ServiceName = 'LabKomStudentAgent',
    [string]$DisplayName = 'LabKom Student Agent',
    [string]$Description = 'Agent monitor lab komputer untuk siswa',
    [string]$ExePath
)

$ErrorActionPreference = 'Stop'

if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host "[!] Jalankan sebagai Administrator." -ForegroundColor Red
    exit 1
}

if (-not $ExePath) {
    $root = Resolve-Path (Join-Path $PSScriptRoot '..')
    $candidate = Join-Path $root 'src\LabKom.Student\bin\Release\net8.0-windows\win-x64\LabKom.Student.Agent.exe'
    if (-not (Test-Path $candidate)) {
        $candidate = Join-Path $root 'src\LabKom.Student\bin\Debug\net8.0-windows\LabKom.Student.Agent.exe'
    }
    if (-not (Test-Path $candidate)) {
        Write-Host "[!] LabKom.Student.Agent.exe tidak ditemukan. Build dulu via setup-dev.ps1, atau berikan -ExePath." -ForegroundColor Red
        exit 1
    }
    $ExePath = $candidate
}

Write-Host "Menginstall service: $ServiceName" -ForegroundColor Cyan
Write-Host "Path           : $ExePath"

$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "Service sudah ada — stop & hapus dulu..." -ForegroundColor Yellow
    Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
    sc.exe delete $ServiceName | Out-Null
    Start-Sleep -Seconds 1
}

sc.exe create $ServiceName binPath= "`"$ExePath`"" start= auto DisplayName= "`"$DisplayName`"" | Out-Null
sc.exe description $ServiceName "$Description" | Out-Null
sc.exe failure $ServiceName reset= 60 actions= restart/5000/restart/5000/restart/10000 | Out-Null

Write-Host "[v] Service terdaftar. Memulai..." -ForegroundColor Green
Start-Service -Name $ServiceName

Write-Host ""
Get-Service -Name $ServiceName
