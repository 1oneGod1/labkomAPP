# DXGI, Adaptive Streaming, dan Remote View/Control

Baseline: LabKom Native 0.8.0.

## Jalur frame

Student Desktop memilih monitor berdasarkan ID Windows (`\\.\DISPLAYn`). Capture utama memakai DXGI Desktop Duplication. Jika output tidak tersedia, rotated output belum didukung driver, session berubah, atau DXGI mengalami access-lost, capture monitor tersebut di-reset dan sementara memakai GDI. DXGI dicoba kembali setelah cooldown 15 detik.

Setiap frame tetap JPEG agar kompatibel dengan Teacher/Student lama. Metadata tambahan menunjukkan backend capture, resolusi, target FPS, kualitas JPEG, waktu capture, dan waktu kirim frame sebelumnya.

Adaptive controller menjaga satu frame in-flight. Dua sampel pressure berturut-turut menurunkan resolusi, kualitas, dan FPS; sepuluh sampel sehat menaikkan level secara bertahap. Batas default:

- Thumbnail: 480x270, 1 FPS, Q55, maksimum 750 Kbps.
- Focus/remote: 1280x720, 4 FPS, Q70, maksimum 4000 Kbps.
- Konfigurasi berada di `src/LabKom.Student.Desktop/appsettings.json`.

## Model keamanan remote

Remote view dimulai sebagai view-only. Mouse dan keyboard baru diteruskan setelah operator menekan **Aktifkan kontrol**.

- Session ID acak terikat pada target PC, mode, monitor, dan expiry.
- Sesi diperbarui tiap 45 detik dan kedaluwarsa otomatis bila Teacher terputus.
- Setiap input memiliki sequence number dan timestamp; replay, salah target, view-only, dan input kedaluwarsa ditolak.
- Role Observer/Auditor hanya dapat melihat. Remote control memerlukan permission `RemoteControl`, yang tersedia untuk Instructor dan Administrator.
- Start/stop diaudit sebagai operator. Emergency release, rejected, dan expiry dicatat sebagai status keamanan sistem.
- Student selalu menampilkan banner. Tombol **Lepas** atau Ctrl+Alt+Q menghentikan penerimaan input secara lokal.
- Windows key, Ctrl+Alt+Delete, dan Ctrl+Alt+Q tidak diinjeksi. Semua tombol yang masih pressed dilepas saat sesi berakhir.
- Koneksi tetap memakai HTTPS SignalR, certificate pinning, dan identitas perangkat LabKom yang sudah diprovision.

## Cara memakai

1. Pilih satu PC online pada Teacher Console.
2. Pilih monitor yang diinginkan.
3. Klik **Remote View / Control**.
4. Viewer mulai dalam mode view-only.
5. Klik **Aktifkan kontrol** untuk meneruskan input.
6. Klik **View-only** untuk kembali tanpa input, atau **Stop sesi** untuk menutup.
7. Pada Student, Ctrl+Alt+Q selalu dapat melepas sesi remote aktif.

## Verifikasi lab

Sebelum rollout:

1. Uji DXGI pada Intel, NVIDIA, AMD, laptop hybrid-GPU, monitor portrait, dan RDP/console.
2. Pastikan label stream menampilkan `DxgiDesktopDuplication`; fallback `Gdi` tetap harus stabil.
3. Uji perpindahan monitor dan perubahan resolusi/DPI.
4. Batasi bandwidth untuk memastikan adaptation level turun tanpa kedip/hidup-mati.
5. Uji mouse, keyboard, wheel, expiry, disconnect Teacher, Stop sesi, banner, dan Ctrl+Alt+Q.
6. Jalankan pilot 5 PC lalu 10/20/40 PC mengikuti `PILOT-TEST-5-40-PC.md`.

Build dan unit/integration test memvalidasi kontrak, replay protection, RBAC, serta adaptive state machine. Kelulusan perangkat fisik tetap harus dicatat lewat pilot; build hijau bukan pengganti uji driver/GPU nyata.
