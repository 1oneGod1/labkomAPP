# Install LabKom Student Agent as a Windows Service.
# Run this script from an elevated PowerShell session.

param(
    [string]$ServiceName = 'LabKomStudentAgent',
    [string]$DisplayName = 'LabKom Student Agent',
    [string]$Description = 'Agent pengelolaan komputer lab untuk siswa',
    [string]$ExePath
)

$ErrorActionPreference = 'Stop'

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Error 'Jalankan PowerShell sebagai Administrator.'
    exit 1
}

if (-not $ExePath) {
    $root = Resolve-Path (Join-Path $PSScriptRoot '..')
    $candidates = @(
        (Join-Path $root 'src\LabKom.Student\bin\Release\net8.0-windows\LabKom.Student.Agent.exe'),
        (Join-Path $root 'src\LabKom.Student\bin\Release\net8.0-windows\win-x64\LabKom.Student.Agent.exe'),
        (Join-Path $root 'src\LabKom.Student\bin\Debug\net8.0-windows\LabKom.Student.Agent.exe')
    )
    $ExePath = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}

if (-not $ExePath -or -not (Test-Path -LiteralPath $ExePath -PathType Leaf)) {
    Write-Error 'LabKom.Student.Agent.exe tidak ditemukan. Build Release atau berikan -ExePath.'
    exit 1
}

$ExePath = [IO.Path]::GetFullPath($ExePath)
Write-Host "Memasang service: $ServiceName" -ForegroundColor Cyan
Write-Host "Path             : $ExePath"

$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host 'Service lama dihentikan dan dihapus.' -ForegroundColor Yellow
    Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
    sc.exe delete $ServiceName | Out-Null
    Start-Sleep -Seconds 1
}

$quotedPath = '"' + $ExePath + '"'
New-Service -Name $ServiceName -BinaryPathName $quotedPath -DisplayName $DisplayName -Description $Description -StartupType Automatic | Out-Null
sc.exe failure $ServiceName reset= 60 actions= restart/5000/restart/5000/restart/10000 | Out-Null

Start-Service -Name $ServiceName
Get-Service -Name $ServiceName
