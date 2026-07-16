# Roadmap Clean-Room Feature Parity LabKom

Baseline native: 16 Juli 2026

## Batas implementasi

LabKom dibangun ulang sebagai aplikasi Windows native berdasarkan kebutuhan pengelolaan kelas dan perilaku yang terdokumentasi publik. Implementasi tidak menyalin source code, protokol privat, aset, merek, atau hasil dekompilasi NetSupport School.

Referensi perilaku publik:

- https://www.netsupportschool.com/features/
- https://www.netsupportschool.com/new-features/
- https://help.netsupportschool.com/en-windows/Content/Windows/Using-tutor/showing_to_students.html
- https://help.netsupportschool.com/en-windows/Content/Windows/Settings/show_settings.html
- https://help.netsupportschool.com/en-windows/Content/Windows/Tech-Console/tech_console.html

## Arsitektur native

- Teacher Console: WPF, host HTTPS/SignalR, discovery UDP, SQLite, monitoring, dan perintah kelas.
- Student Agent: Windows Service untuk power dan policy yang membutuhkan hak sistem.
- Student Desktop: WPF pada sesi pengguna untuk capture monitor, lock/attention, broadcast guru, chat, aktivitas, dan download file.
- Shared: kontrak, validasi payload, HMAC discovery, routing role/PC, dan certificate pinning.
- Data: persistence SQLite di sisi Teacher.

Control plane memakai SignalR melalui HTTPS. Secret tidak berada di query string. Beacon discovery menandatangani endpoint dan pin sertifikat dengan HMAC-SHA256. Audience dipisahkan menjadi Agent/Desktop serta per-PC.

Media plane memakai frame JPEG biner, maksimal 1,5 MiB, satu frame in-flight, StreamId, dan SequenceNumber. Frame lama dari koneksi atau urutan sebelumnya ditolak.

## Status native v0.2.0

Selesai dan sudah masuk regression test:

- Pemisahan Windows Service Session 0 dari proses desktop interaktif.
- HTTPS dengan sertifikat ephemeral, certificate pinning, discovery HMAC, dan shared secret minimal 32 karakter.
- Routing per role dan per-PC tanpa broadcast ke proses yang salah.
- Presence yang aman terhadap reconnect/disconnect terlambat.
- Thumbnail dan focus capture GDI, inventaris multi-monitor, serta pemilihan monitor dari Teacher.
- Penolakan frame lama berdasarkan koneksi Desktop, StreamId, dan SequenceNumber.
- Attention/lock di seluruh virtual desktop, di atas taskbar, dengan blok Win, Alt+Tab, Alt+F4, Alt+Esc, Ctrl+Esc, dan F11.
- Broadcast layar Teacher ke semua atau satu siswa, pause/resume, late-join state, BroadcastId/sequence, dan frame lama tetap tampil sampai frame baru valid.
- Chat broadcast Teacher, jendela pesan siswa, balasan siswa, dan feed pesan di Teacher.
- Distribusi file ke sesi pengguna dengan HTTPS pinning, batas ukuran, validasi nama, ukuran, endpoint, dan SHA-256.
- Activity window, shutdown/restart, Wake-on-LAN, serta fondasi app/web policy.
- Command ID/TTL, acknowledgement Attention/Power per-PC, audit SQLite, dan replay Attention setelah Desktop reconnect.
- Build Release memperlakukan warning sebagai error dan 40 regression test lulus.

Batas Windows: Ctrl+Alt+Delete adalah secure attention sequence dan tidak dapat diblokir oleh aplikasi user-mode. Mode lock tetap memerlukan policy Windows lab (akun siswa non-admin, Task Manager/fast user switching sesuai kebijakan sekolah) untuk kiosk yang lebih kuat.

## Matriks feature parity native

| Area | Fitur | Status native | Tahap |
|---|---|---|---|
| Monitor | Thumbnail seluruh kelas | Selesai, GDI binary | P1 |
| Monitor | Focus view kualitas tinggi | Selesai, 1280x720/4 FPS | P1 |
| Monitor | Multi-monitor per siswa | Inventaris dan pemilihan selesai | P1 |
| Monitor | DXGI/adaptive quality/telemetry | Belum | P1 |
| Monitor | Remote view/control individual | Belum | P1 |
| Instruct | Show layar instruktur ke semua/terpilih | Selesai | P0/P1 |
| Instruct | Pause/resume dan late-join state | Selesai | P1 |
| Instruct | Target group tersimpan | Belum | P2 |
| Instruct | Show satu siswa ke siswa lain | Belum | P1 |
| Instruct | Pointer, annotation, snapshot | Belum | P1 |
| Instruct | Whiteboard, aplikasi, video, audio | Belum | P2 |
| Control | Lock/blank screen | Fondasi native selesai | P0 |
| Control | Command ack dan recovery reconnect | Selesai untuk Attention/Power | P1 |
| Control | Recovery setelah Teacher restart | Belum | P1 |
| Control | Quick launch aplikasi/file/URL | Sebagian | P2 |
| Control | Allow/block aplikasi dan website | Fondasi Agent ada | P2 |
| Control | Audio, print, USB, clipboard policy | Belum | P2/P3 |
| Interaction | Broadcast chat dan balasan siswa | Selesai | P0 |
| Interaction | Help request, feedback, survey | Belum | P2 |
| Content | Distribusi file | Fondasi download selesai | P2 |
| Content | Pengumpulan file dan conflict handling | Belum | P2 |
| Classroom | Login siswa, room, group, layout | Belum | P2 |
| Assessment | Test builder, hasil, reporting | Belum | P3 |
| Technician | Inventory hardware/software | Belum | P3 |
| Technician | Process/service manager | Belum | P3 |
| Deployment | MSI/GPO, provisioning, signing, update | Belum | P4 |

## Tahapan berikutnya

### P1 - Monitoring dan demonstrasi

- DXGI Desktop Duplication dengan fallback GDI.
- Adaptive FPS/quality dan telemetry latency/drop.
- Target broadcast ke group tersimpan.
- Exhibit layar siswa, pointer, annotation, snapshot.
- Remote control dengan policy sekolah, audit, dan emergency stop.

### P2 - Workflow kelas

- Login siswa, sesi, room/group, dan layout tersimpan.
- Help request, feedback/survey, quick launch, serta policy aplikasi/web/audio dengan acknowledgement.
- Distribusi dan pengumpulan file dengan progress, retry, serta conflict handling.

### P3 - Assessment dan technician

- Test builder, hasil, reporting, inventory, process/service manager, journal, dan replay.
- Print, USB, clipboard, dan device policy.

### P4 - Deployment enterprise

- MSI/WiX untuk Teacher dan Student, secret/key provisioning, upgrade/rollback.
- Code signing, signed update manifest, RBAC admin, audit append-only, backup.
- Uji beban 40 PC, soak test satu hari sekolah, GPO/Intune guidance.

## Gate penghapusan legacy

Folder Electron/Node lama tidak dihapus sebelum P0/P1 native lulus uji pada beberapa PC/VM Windows dan installer native memiliki rollback. Setelah gate terpenuhi, legacy dipindahkan ke tag arsip dan dikeluarkan dari paket utama.

## Kriteria selesai fitur

Setiap fitur harus memiliki autentikasi/otorisasi, target eksplisit, validasi input, acknowledgement atau hasil eksekusi, cleanup disconnect, recovery, tes otomatis, build produksi, serta uji lapangan sebelum diberi status selesai.
