# LabKom Architecture Rewrite Plan

Tujuan rewrite ini adalah membuat aplikasi lebih mudah dirawat tanpa memutus workflow lab yang sudah jalan. Pendekatannya incremental: setiap tahap harus tetap bisa build dan bisa dipakai.

## Prinsip

- Backend menjadi sumber kontrak data utama.
- Client hanya boleh logout sesi miliknya sendiri.
- Operasi admin harus selalu lewat token admin.
- Realtime event dipisah berdasarkan domain: presence, screen, chat, activity, control.
- Electron main process bertugas untuk OS integration, bukan business logic UI.
- React component besar dipecah berdasarkan feature.

## Tahap 1: Stabilkan Fondasi

- Pastikan `client` dan `admin` build sukses.
- Samakan kontrak login: `student_id`, `session_id`, `pc_name`, `nama_lengkap`.
- Kirim activity monitoring dengan metadata sesi yang benar.
- Pastikan IPC admin membawa token saat memanggil endpoint protected.
- Hapus pemanggilan admin-only endpoint dari client.

## Tahap 2: Backend Contract Layer

- Tambah modul contract/validator untuk payload HTTP dan socket.
- Buat response shape konsisten: `{ success, message, data, error }`.
- Pisahkan realtime handler menjadi file:
  - `realtime/presenceHub.js`
  - `realtime/screenHub.js`
  - `realtime/chatHub.js`
  - `realtime/activityHub.js`
  - `realtime/controlHub.js`
- Tambah client identity token ringan untuk endpoint client yang sengaja terbuka.

## Tahap 3: Admin App Feature Split

- Pecah `AdminDashboard.jsx` menjadi shell dan feature modules:
  - `features/auth`
  - `features/monitoring`
  - `features/screens`
  - `features/control`
  - `features/students`
  - `features/history`
  - `features/checks`
  - `features/chat`
  - `features/server`
- Pindahkan API client admin ke `src/lib/adminApi.js`.
- Pindahkan socket setup ke hook `useAdminRealtime`.

## Tahap 4: Client App State Machine

- Ganti mode string manual dengan state machine kecil:
  - `loading`
  - `setup`
  - `login`
  - `precheck`
  - `session`
  - `postcheck`
- Pindahkan Electron IPC wrapper ke `src/lib/electronApi.js`.
- Pindahkan server discovery dan server health ke hook terpisah.
- Pindahkan realtime client ke hook `useClientRealtime`.

## Tahap 5: Electron Hardening

- Hilangkan `disable-web-security` dan `webSecurity: false` jika IPC request sudah cukup.
- Batasi `api-request` hanya ke server URL yang tersimpan dan path allowlist.
- Validasi payload IPC di main process.
- Hindari hardcoded emergency password di renderer.

## Tahap 6: Tests dan Release Safety

- Tambah smoke test untuk build admin/client.
- Tambah test backend untuk auth/logout/client command.
- Tambah checklist release: build, syntax check, manual Electron smoke, update packaging.
