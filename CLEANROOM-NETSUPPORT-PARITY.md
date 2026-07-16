# Roadmap Clean-Room Feature Parity LabKom

Tanggal baseline: 16 Juli 2026

## Batas implementasi

LabKom akan meniru perilaku dan alur kerja yang terdokumentasi publik, bukan menyalin source code, protokol privat, aset, merek, atau hasil dekompilasi NetSupport School. Installer referensi tidak diperlukan untuk membangun implementasi ini.

Referensi perilaku resmi:

- https://www.netsupportschool.com/features/
- https://www.netsupportschool.com/new-features/
- https://help.netsupportschool.com/en-windows/Content/Windows/Using-tutor/showing_to_students.html
- https://help.netsupportschool.com/en-windows/Content/Windows/Settings/show_settings.html
- https://help.netsupportschool.com/en-windows/Content/Windows/Tech-Console/tech_console.html

## Arsitektur target

### Control plane

- Socket.IO terautentikasi untuk presence, room/kelas, policy, perintah, acknowledgement, chat, dan status fitur.
- Token admin dan device tetap dipisahkan.
- Semua target PC dinormalisasi dan diverifikasi server; renderer tidak memilih endpoint arbitrer.
- Perintah penting memiliki audit log, TTL, acknowledgement, dan status per-PC.

### Media plane

- Frame biner JPEG/WebP, bukan data-URI base64 pada pengirim baru.
- Satu frame in-flight untuk mencegah antrian tanpa batas.
- Profil Performance, Balanced, dan Quality; kualitas JPEG menyesuaikan latency acknowledgement.
- Satu upload Admin ke server, kemudian fan-out ke semua PC atau daftar target terpilih.
- Client 1.0.x mendapat fallback base64 selama masa migrasi.
- Tahap lanjutan: abstraksi transport agar broadcast LAN multicast atau WebRTC SFU dapat ditambahkan tanpa mengganti UI.

### State dan persistence

- State realtime/ephemeral disimpan di memory dengan TTL.
- Konfigurasi kelas, policy, hasil tes, audit, inventory, dan histori disimpan persisten.
- File credential, service account, dan secret tidak pernah masuk repository atau paket installer.

## Baseline v1.1.0

Sudah tersedia:

- Discovery server LAN dan device authentication.
- Presence PC, thumbnail layar siswa, dan mode focus HQ.
- Broadcast layar instruktur v2: frame biner, target semua/terpilih, backpressure, kualitas adaptif, statistik FPS/latency.
- Fallback broadcast untuk client 1.0.x.
- Attention/blank overlay, kiosk lock, chat, aktivitas aplikasi, remote power, Wake-on-LAN, dan screen broadcast dasar.
- Exit Kepala Lab yang memakai server tersimpan dan tetap tersedia saat Attention Mode.

## Increment v1.2.0

- Monitoring siswa memakai frame JPEG binary dengan acknowledgement dan satu frame in-flight.
- Overview tetap 480x270/1 FPS; Focus HQ menjadi 1280x720/4 FPS.
- Client mengirim inventaris monitor dan Admin dapat memilih monitor yang ingin ditampilkan.
- Server memvalidasi frame/metadata dan menyediakan fallback base64 untuk Admin atau Client lama.
- Dashboard menampilkan resolusi, latency, jumlah monitor, dan status transport binary.

## Matriks feature parity

| Area | Fitur | Status | Tahap |
|---|---|---:|---:|
| Monitor | Thumbnail seluruh kelas | Binary v2 selesai | P1 |
| Monitor | Focus view kualitas tinggi | Binary 1280x720/4 FPS selesai | P1 |
| Monitor | Multi-monitor per siswa | Pemilihan monitor selesai | P1 |
| Monitor | Remote view/control individual | Belum | P1 |
| Instruct | Show layar instruktur ke semua/terpilih | v2 selesai | P0 |
| Instruct | Show satu siswa ke siswa lain | Belum | P1 |
| Instruct | Pause/resume dan late join | Sebagian (late join server) | P1 |
| Instruct | Annotation dan laser pointer | Belum | P1 |
| Instruct | Whiteboard kolaboratif | Belum | P2 |
| Instruct | Show aplikasi/video/audio | Belum | P2 |
| Control | Lock/blank screen | Ada | P0 |
| Control | Quick launch aplikasi/file/URL | Sebagian | P2 |
| Control | Allow/block aplikasi dan website | Sebagian | P2 |
| Control | Mute audio/volume | Belum | P2 |
| Control | Print/USB/clipboard policy | Belum | P3 |
| Interaction | Chat dan broadcast message | Ada | P0 |
| Interaction | Help request per siswa | Belum | P2 |
| Interaction | Feedback/survey cepat | Belum | P2 |
| Assessment | Test text/gambar/audio/video | Belum | P3 |
| Content | Distribusi dan pengumpulan file | Fondasi .NET ada | P2 |
| Content | Student journal/replay | Belum | P3 |
| Technician | Inventory hardware/software | Belum | P3 |
| Technician | Process/service manager | Belum | P3 |
| Technician | Security policy/compliance | Belum | P4 |
| Classroom | Room, group, saved layout/profile | Belum | P2 |
| Deployment | MSI/GPO deployment dan signed update | Belum | P4 |

## Tahapan implementasi

### P0 — Media foundation

- Transport broadcast v2, compatibility fallback, target selection, quality profiles, telemetry.
- Regression contract Admin–Server–Client.
- Paket Admin dan Client 1.1.0.

### P1 — Monitoring dan demonstrasi

- Binary client-to-admin untuk grid/focus.
- Pemilihan monitor siswa.
- View/control individual dengan consent dan audit.
- Exhibit: tampilkan layar siswa terpilih ke kelas.
- Pause/resume, pointer, annotation, dan snapshot.

### P2 — Classroom workflow

- Room/group dan layout kelas tersimpan.
- Quick launch aplikasi, file, URL.
- Policy aplikasi/website, audio control.
- Help request, feedback, survey, whiteboard.
- File distribute/collect dengan progress dan checksum.

### P3 — Assessment dan technician tools

- Test builder dan result dashboard.
- Inventory hardware/software.
- Process/service/task manager terkontrol.
- Print, USB, clipboard, dan device policy.
- Journal, replay, dan lesson plan.

### P4 — Enterprise deployment

- MSI/GPO/Intune deployment.
- Code signing, signed update manifest, rollback.
- Role-based access, multi-admin audit, security policy baseline.
- Optional gateway untuk lintas subnet.

## Anggaran performa P0/P1

- Overview: 480x270, 1 FPS per siswa, target maksimal 40 PC per server lab.
- Focus: 1280x720 atau lebih, target 4–8 FPS adaptif.
- Broadcast instruktur Balanced: 1600x900, target 10 FPS, median latency LAN di bawah 250 ms.
- Maksimum satu frame in-flight per publisher dan maksimum frame 1.5 MiB.
- Tidak ada antrian frame lama; frame boleh dilewati saat jaringan lambat.

## Kriteria selesai

Setiap fitur wajib memiliki autentikasi/otorisasi, target yang eksplisit, acknowledgement, cleanup saat disconnect, fallback yang terdokumentasi, tes kontrak, build produksi, dan skenario recovery sebelum masuk installer rilis.
