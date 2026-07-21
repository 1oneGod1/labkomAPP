# Pilot 5-40 PC dan Telemetry Streaming

Panduan ini berlaku mulai LabKom Native `0.8.0`. Ada dua pengujian yang saling
melengkapi:

1. `LabKom.LoadTest` menguji endpoint HTTPS SignalR Teacher dengan 5-40 Agent
   virtual, autentikasi perangkat, heartbeat, dan telemetry streaming.
2. `Invoke-LabKomPilot.ps1` menguji instalasi pada 5-40 PC Windows fisik dan
   menghasilkan bukti kelulusan per perangkat.

Load test tidak menggantikan pilot fisik. Status siap lab baru boleh dinyatakan
setelah laporan pilot fisik pada 40 PC menghasilkan `Passed: true`.

## Data telemetry

Student Agent mengirim sampel setiap dua detik secara default. Sampel berisi:

- CPU, memori, ruang disk, dan uptime;
- throughput jaringan masuk/keluar;
- working set dan jumlah thread Agent;
- nomor urut dan timestamp untuk mendeteksi duplikasi, sampel terlambat, serta
  putusnya stream.

Telemetry tidak merekam isi layar, judul jendela, URL, ketikan, atau dokumen
siswa. Teacher menyimpan CSV sesi di:

```text
%LOCALAPPDATA%\LabKom\Telemetry\telemetry-yyyyMMdd-HHmmss-fff.csv
```

Dashboard menampilkan jumlah stream aktif/stale, warning/kritis, p95 latency,
jumlah sampel diterima/ditolak, serta jumlah baris CSV/drop queue.

## Prasyarat pilot fisik

- Teacher dan semua Student memakai build `0.8.0` yang sama.
- Setiap PC Student sudah memiliki credential perangkat unik.
- Waktu Windows tersinkronisasi dan semua PC berada pada LAN yang sama.
- TCP 41235 dari Student menuju Teacher dan UDP 41234 discovery diizinkan.
- WinRM/PowerShell Remoting aktif untuk akun administrator pilot.
- Teacher Console sudah berjalan sebelum skrip dimulai.
- Gunakan akun siswa non-admin untuk pengujian UI, lock, recovery, dan watchdog.

Verifikasi provisioning pada Teacher:

```powershell
& "$env:ProgramFiles\LabKom\Provisioning\LabKom.Provisioning.exe" verify
& "$env:ProgramFiles\LabKom\Provisioning\LabKom.Provisioning.exe" key-status
```

## Tahap A - load test SignalR

Jalankan dari komputer pada LAN yang menerima broadcast discovery Teacher.
Gunakan bundle provisioning yang aksesnya dibatasi; secret tidak perlu dicetak
atau diberikan sebagai argumen command line.

```powershell
$bundlePath = "E:\LabKom-Lab-Komputer.provision.json"
$bundle = Get-Content -LiteralPath $bundlePath -Raw | ConvertFrom-Json
$env:LABKOM_LOADTEST_ROOT_SECRET = $bundle.Secret
try {
    dotnet run --project .\src\LabKom.LoadTest\LabKom.LoadTest.csproj `
      -c Release --no-build -- `
      --classroom-id $bundle.ClassroomId `
      --clients 40 `
      --duration-seconds 300 `
      --interval-seconds 2 `
      --output .\artifacts\pilot\load-40.json
}
finally {
    Remove-Item Env:\LABKOM_LOADTEST_ROOT_SECRET -ErrorAction SilentlyContinue
    $bundle = $null
}
```

Load test menerima beacon discovery yang ditandatangani secret kelas, lalu
memakai TLS certificate pin dari beacon tersebut. Jika broadcast tidak melintasi
subnet, berikan `--hub-url` dan `--certificate-sha256` bersama-sama menggunakan
nilai dari kanal administrasi tepercaya. Teacher juga memublikasikan pin sesi
saat ini (bukan secret) ke:

```text
%LOCALAPPDATA%\LabKom\Security\teacher-tls-sha256.txt
```

Exit code `0` berarti semua klien terhubung, jumlah sampel minimum tercapai,
tidak ada error, dan p95 round-trip tidak melewati ambang. Exit code `2` berarti
kriteria performa gagal; exit code `1` berarti konfigurasi atau eksekusi gagal.

## Tahap B - pilot PC fisik

Mulai dengan 5 PC dan naikkan bertahap ke 10, 20, lalu 40 PC. Jangan melompati
tahap jika tahap sebelumnya gagal.

```powershell
$pcs = 1..5 | ForEach-Object { "LAB-PC-{0:D2}" -f $_ }
$credential = Get-Credential
.\scripts\Invoke-LabKomPilot.ps1 `
  -ComputerName $pcs `
  -TeacherHost "LAB-TEACHER" `
  -Credential $credential `
  -DurationMinutes 15
```

Untuk tahap 40 PC dan soak test dua jam:

```powershell
$pcs = 1..40 | ForEach-Object { "LAB-PC-{0:D2}" -f $_ }
.\scripts\Invoke-LabKomPilot.ps1 `
  -ComputerName $pcs `
  -TeacherHost "LAB-TEACHER" `
  -Credential (Get-Credential) `
  -DurationMinutes 120 `
  -MaximumP95LatencyMs 1500
```

Jika Teacher dijalankan oleh akun Windows yang berbeda, berikan
`-TelemetryCsv` dengan path CSV sesi milik akun tersebut.

Skrip memeriksa setiap PC untuk service Agent, binary Agent/Desktop, credential
perangkat, koneksi TCP 41235, versi, ruang disk, proses Desktop, jumlah sampel,
gap maksimum stream, dan p95 latency. Desktop yang tidak berjalan dicatat agar
mudah didiagnosis, sedangkan kriteria otomatis utama adalah Agent dan stream.

## Kriteria kelulusan otomatis

Setiap perangkat harus memenuhi seluruh syarat berikut:

- service `LabKomStudentAgent` berjalan;
- binary Agent dan credential perangkat tersedia;
- TCP 41235 menuju Teacher dapat dibuka;
- sedikitnya 75% sampel yang diharapkan diterima;
- gap maksimum tidak lebih dari empat kali interval telemetry;
- p95 latency tidak melebihi `MaximumP95LatencyMs`.

Selain hasil otomatis, operator wajib memeriksa pada sampel PC di setiap tahap:

- attention lock, emergency unlock, timeout recovery, dan watchdog Desktop;
- multi-monitor, chat, distribusi file, policy web/aplikasi, serta power command;
- restart Teacher, restart Agent, putus-sambung kabel LAN, logoff/login siswa;
- penggunaan CPU/memori dan stabilitas layar selama soak test.

## Artefak hasil

Setiap sesi pilot fisik menghasilkan folder
`Documents\LabKom\Pilot\pilot-<timestamp>` yang berisi:

- `summary.json`: keputusan akhir sesi;
- `preflight.csv`: status instalasi dan konektivitas tiap PC;
- `telemetry-summary.csv`: metrik dan keputusan per PC;
- `telemetry-raw.csv`: salinan bukti telemetry mentah.

Jangan hanya menyimpan tangkapan layar dashboard. Arsipkan empat file tersebut
bersama versi installer, nama jaringan/VLAN, model PC, versi Windows, dan catatan
operator. Data 40 PC inilah yang menjadi bukti pengujian nyata.

## Urutan yang direkomendasikan

1. Load test 40 klien selama 5 menit.
