# LabKom Native

LabKom Native adalah implementasi Windows clean-room dengan .NET 8, WPF, Windows Service, ASP.NET Core SignalR, dan SQLite. Kode atau protokol privat produk pihak ketiga tidak digunakan.

## Project

- src/LabKom.Teacher: Teacher Console dan host LAN HTTPS.
- src/LabKom.Student: Agent Windows Service untuk operasi privileged.
- src/LabKom.Student.Desktop: companion interaktif siswa.
- src/LabKom.Shared: kontrak dan keamanan jaringan.
- src/LabKom.Data: database Teacher.
- tests/LabKom.Tests: regression tests.

## Prasyarat development

- Windows 10/11 x64.
- .NET 8 SDK.
- Shared secret yang sama dan minimal 32 karakter pada Teacher, Agent, dan Desktop.

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

Seluruh project mengaktifkan nullable reference types dan warning sebagai error. Baseline saat ini: build Release bersih dan 40 regression test.

## Menjalankan development

Jalankan dengan LABKOM_SHARED_SECRET yang sama:

```powershell
dotnet run --project src/LabKom.Teacher/LabKom.Teacher.csproj
dotnet run --project src/LabKom.Student/LabKom.Student.csproj
dotnet run --project src/LabKom.Student.Desktop/LabKom.Student.Desktop.csproj
```

Untuk deployment sementara, Agent dipasang sebagai Windows Service dan Desktop sebagai scheduled task saat logon. Script tersedia di folder scripts. MSI terpadu, provisioning credential, signing, dan rollback masih berada pada gate release berikutnya.

## Status

Fondasi native, multi-monitor GDI, lock virtual desktop dengan recovery reconnect, acknowledgement Attention/Power, broadcast all/selected dengan pause dan late join, chat dua arah, HTTPS discovery, dan file download tervalidasi sudah tersedia. DXGI, remote control, room/group, assessment, technician tools, installer, serta uji lapangan belum selesai. Lihat ../ARCHITECTURE-REWRITE-PLAN.md dan ../CLEANROOM-NETSUPPORT-PARITY.md.
