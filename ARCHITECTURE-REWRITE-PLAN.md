# Rencana Migrasi Native LabKom

## Tujuan

LabKom dipindahkan dari Electron/Node ke aplikasi Windows native .NET 8 secara clean-room. Tidak ada klaim "tanpa bug" hanya karena build berhasil; sebuah fitur dianggap selesai setelah validasi, security, targeting, recovery, tes, dan uji Windows nyata terpenuhi.

## Batas proses

- LabKom.Teacher: Teacher Console WPF, HTTPS/SignalR host, discovery, SQLite, monitoring, dan orkestrasi.
- LabKom.Student.Agent: Windows Service untuk power dan policy privileged.
- LabKom.Student.Desktop: proses sesi pengguna untuk capture, aktivitas, lock, broadcast, chat, dan file.
- LabKom.Shared: contract, validation, identity, routing, discovery, dan certificate pinning.
- LabKom.Data: persistence lokal Teacher.

Windows Service tidak menangkap layar atau menulis ke Documents siswa karena berjalan di Session 0.

## Security baseline

- Shared secret minimal 32 karakter dan tidak dikirim melalui query string.
- SignalR dan file transfer melalui HTTPS.
- Student memverifikasi sertifikat Teacher menggunakan pin dari beacon HMAC.
- Beacon membawa timestamp dan ditolak ketika stale/tampered.
- Koneksi dipisahkan menjadi role:agent, role:desktop, pc:<name>:agent, dan pc:<name>:desktop.
- Frame, activity, chat, file progress, PC name, serta monitor inventory dibatasi.
- Endpoint URL dan certificate pin dibaca melalui satu snapshot atomik.

## P0 - Fondasi native

- [x] Solution .NET, WPF Teacher, Windows Service Agent, WPF Student Desktop.
- [x] Pemisahan Session 0 dan desktop interaktif.
- [x] Routing per role dan per-PC.
- [x] HTTPS, HMAC discovery, certificate pinning, dan secret header.
- [x] Multi-monitor GDI, frame StreamId/SequenceNumber, dan stale-connection rejection.
- [x] Attention fullscreen virtual desktop dan keyboard policy dasar.
- [x] Broadcast layar Teacher dasar dan chat dua arah.
- [x] File download ber-checksum pada sesi pengguna.
- [x] Warning-as-error serta 54 regression test.
- [x] Command ID/TTL, acknowledgement Attention/Power, audit SQLite, serta snapshot atomik untuk recovery Desktop reconnect/Teacher restart.
- [ ] Persisted desired lesson state setelah Teacher restart (snapshot fail-safe selesai).
- [ ] Provisioning credential saat installer dan rotasi key.
- [ ] MSI/WiX, upgrade/rollback, dan code signing.

## P1 - Monitoring dan demonstrasi

- [x] Thumbnail/focus serta pemilihan monitor.
- [ ] DXGI Desktop Duplication, adaptive FPS/quality, dan telemetry.
- [x] Target broadcast semua/terpilih, pause/resume, late-join state, dan sequence.
- [x] Multi-select dan target broadcast group tersimpan.
- [ ] Exhibit layar siswa, pointer, annotation, snapshot.
- [ ] Remote view/control dengan audit dan policy.

## P2 - Workflow kelas

- [x] Activity window, chat dasar, dan distribusi file download.
- [ ] Login siswa, sesi, room, dan layout (grup tersimpan selesai).
- [ ] Help request, feedback/survey, quick launch.
- [ ] App/web/audio policy lengkap dengan status per-PC.
- [ ] File collect dan retry (progress realtime serta konflik nama download selesai).

## P3 - Assessment dan technician

- [ ] Test builder, hasil, dan reporting.
- [ ] Inventory hardware/software.
- [ ] Process/service manager.
- [ ] Print/USB/clipboard/device policy.
- [ ] Journal, replay, dan lesson plan.

## P4 - Release enterprise

- [ ] MSI/GPO/Intune deployment.
- [ ] Certificate signing dan signed update manifest.
- [ ] Role-based admin, audit append-only, backup, disaster recovery.
- [ ] Uji beban 40 PC dan soak test satu hari sekolah.

## Jalur transisi

Folder admin, client, dan server adalah implementasi legacy v1.2.1. Folder tersebut dibekukan sebagai rollback dan tidak menjadi arsitektur target. Jangan hapus sebelum P0/P1 native lulus uji lapangan dan installer native dapat rollback. Setelah itu, pindahkan ke tag arsip dan keluarkan dari paket rilis utama.
