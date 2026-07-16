# Build and test the LabKom native solution.
$ErrorActionPreference = 'Stop'

Write-Host "=== LabKom Native setup ===" -ForegroundColor Cyan
$sdks = & dotnet --list-sdks 2>$null
if (-not $sdks -or -not ($sdks | Select-String -Pattern '^8\.')) {
    Write-Host "[!] .NET 8 SDK tidak ditemukan." -ForegroundColor Yellow
    Write-Host "    https://dotnet.microsoft.com/download/dotnet/8.0"
    exit 1
}

$root = Resolve-Path (Join-Path $PSScriptRoot '..')
Set-Location $root

Write-Host "Restoring packages..." -ForegroundColor Cyan
dotnet restore .\LabKom.sln --disable-parallel -p:BuildInParallel=false

Write-Host "Building Release (warnings are errors)..." -ForegroundColor Cyan
dotnet build .\LabKom.sln -c Release --no-restore -m:1

Write-Host "Running regression tests..." -ForegroundColor Cyan
dotnet test .\tests\LabKom.Tests\LabKom.Tests.csproj -c Release --no-build --no-restore -m:1

Write-Host "[v] Build dan test selesai." -ForegroundColor Green
Write-Host ""
Write-Host "Set LABKOM_SHARED_SECRET yang sama (minimal 32 karakter), lalu jalankan:" -ForegroundColor Yellow
Write-Host "  Teacher : dotnet run --project src\LabKom.Teacher\LabKom.Teacher.csproj"
Write-Host "  Agent   : dotnet run --project src\LabKom.Student\LabKom.Student.csproj"
Write-Host "  Desktop : dotnet run --project src\LabKom.Student.Desktop\LabKom.Student.Desktop.csproj"