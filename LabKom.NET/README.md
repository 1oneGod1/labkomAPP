# LabKom Native

LabKom Native adalah implementasi Windows clean-room dengan .NET 8, WPF, Windows Service, ASP.NET Core SignalR, dan SQLite. Kode atau protokol privat produk pihak ketiga tidak digunakan.

## Project

- src/LabKom.Teacher: Teacher Console dan host LAN HTTPS.
- src/LabKom.Student: Agent Windows Service untuk operasi privileged.
- src/LabKom.Student.Desktop: companion interaktif siswa.
- src/LabKom.Shared: kontrak dan keamanan jaringan.
- src/LabKom.Data: database Teacher.
- src/LabKom.Provisioning: pengelolaan bundle dan secret kelas DPAPI.
- src/LabKom.Updater: update bertanda tangan dan rollback.
- src/LabKom.Installer: installer self-contained Teacher/Student.
- src/LabKom.LoadTest: generator beban SignalR 5-40 Agent virtual dengan telemetry streaming.
- tests/LabKom.Tests: regression tests.

## Prasyarat development

- Windows 10/11 x64.
- .NET 8 SDK.
- Root secret development yang sama dan minimal 32 karakter pada Teacher, Agent,
  dan Desktop. Instalasi production memakai credential unik per perangkat.

Untuk development, set environment variable sebelum menjalankan ketiga proses:

```powershell
$env:LABKOM_SHARED_SECRET = -join ((1..48) | ForEach-Object {
    'abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789'[(Get-Random -Maximum 62)]
})
```

Alternatif lokal adalah appsettings.Local.json pada folder output masing-masing aplikasi dengan key Teacher:SharedSecret, Agent:SharedSecret, atau Desktop:SharedSecret. File tersebut diabaikan Git. Jangan menaruh credential asli di appsettings.json atau repository.

## Build dan test

```powershell
./scripts/setup-dev.ps1
```

Atau:

```powershell
dotnet restore LabKom.sln
dotnet build LabKom.sln -c Release --no-restore
dotnet test tests/LabKom.Tests/LabKom.Tests.csproj -c Release --no-build --no-restore
```

Seluruh project mengaktifkan nullable reference types dan warning sebagai error. Baseline saat ini: build bersih tanpa warning dan 97 regression test.

## Menjalankan development

Jalankan dengan LABKOM_SHARED_SECRET yang sama:

```powershell
dotnet run --project src/LabKom.Teacher/LabKom.Teacher.csproj
dotnet run --project src/LabKom.Student/LabKom.Student.csproj
dotnet run --project src/LabKom.Student.Desktop/LabKom.Student.Desktop.csproj
```

Release native menghasilkan installer EXE self-contained untuk Teacher dan Student. Installer mengatur Windows Service, autostart, firewall, provisioning secret DPAPI, updater terjadwal, Authenticode, health-check, rollback otomatis, dan uninstall. Lihat [DEPLOYMENT-RELEASE.md](DEPLOYMENT-RELEASE.md) serta [SECURITY-IDENTITY-RBAC-AUDIT.md](SECURITY-IDENTITY-RBAC-AUDIT.md).

## Status

Fondasi native, menu Teacher operasional, DXGI Desktop Duplication multi-monitor dengan fallback GDI dan streaming adaptif, remote view/control bersesi, lock recovery, broadcast, chat, file transfer, policy, telemetry 5-40 klien, installer, signed update, provisioning, dan rollback sudah tersedia. Monitoring aplikasi/browser mencatat judul jendela, proses, kategori, idle, dan jumlah aktivitas keyboard tanpa isi ketikan. File collect satu-file memakai persetujuan siswa, root terbatas, TTL, chunk, SHA-256, RBAC, dan audit. Room/register/lesson, assessment pilihan ganda bertimer dengan auto-submit, serta Technician Console berbasis RBAC juga sudah tersedia. Uji lapangan DXGI/control dan modul baru pada 5-40 PC fisik tetap wajib sebelum rollout produksi. Lihat [CLASSROOM-MONITORING-TECHNICIAN.md](CLASSROOM-MONITORING-TECHNICIAN.md), [DXGI-REMOTE-CONTROL.md](DXGI-REMOTE-CONTROL.md), dan [PILOT-TEST-5-40-PC.md](PILOT-TEST-5-40-PC.md).
