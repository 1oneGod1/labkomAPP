# LabKom.NET — Setup development environment
# Memvalidasi .NET 8 SDK dan menjalankan restore + build solusi.

$ErrorActionPreference = 'Stop'

Write-Host "=== LabKom.NET dev setup ===" -ForegroundColor Cyan

# 1. Cek .NET SDK
$sdks = & dotnet --list-sdks 2>$null
if (-not $sdks -or -not ($sdks | Select-String -Pattern '^8\.')) {
    Write-Host ""
    Write-Host "[!] .NET 8 SDK tidak ditemukan." -ForegroundColor Yellow
    Write-Host "    Install dari: https://dotnet.microsoft.com/download/dotnet/8.0"
    Write-Host "    Atau via winget: winget install Microsoft.DotNet.SDK.8"
    exit 1
}
Write-Host "[v] .NET SDK terdeteksi:" -ForegroundColor Green
$sdks | ForEach-Object { Write-Host "    $_" }

# 2. Pindah ke root solusi
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
Set-Location $root
Write-Host ""
Write-Host "Working dir: $root"

# 3. Restore + build
Write-Host ""
Write-Host "Restoring packages..." -ForegroundColor Cyan
dotnet restore .\LabKom.sln

Write-Host ""
Write-Host "Building solution..." -ForegroundColor Cyan
dotnet build .\LabKom.sln -c Debug --no-restore

Write-Host ""
Write-Host "[v] Setup selesai." -ForegroundColor Green
Write-Host ""
Write-Host "Cara menjalankan:" -ForegroundColor Yellow
Write-Host "  Teacher : dotnet run --project src\LabKom.Teacher\LabKom.Teacher.csproj"
Write-Host "  Student : dotnet run --project src\LabKom.Student\LabKom.Student.csproj"
Write-Host "  Overlay : dotnet run --project src\LabKom.Student.Overlay\LabKom.Student.Overlay.csproj"
Write-Host ""
Write-Host "Test Phase 2:" -ForegroundColor Yellow
Write-Host "  1. Jalankan Teacher di satu PC"
Write-Host "  2. Jalankan Student + Overlay di PC siswa (atau VM)"
Write-Host "  3. Pilih PC di grid Teacher → klik 'Kunci Layar'"
Write-Host "  4. Overlay fullscreen muncul di PC siswa"
Write-Host "  5. Klik 'Buka Kunci' di Teacher → overlay hilang"
