# Identitas Perangkat, Rotasi Kunci, RBAC, dan Audit

Dokumen ini berlaku mulai LabKom `0.6.0`. Implementasi dibuat kompatibel
bertahap agar Student versi lama tidak langsung kehilangan koneksi saat Teacher
lebih dahulu diperbarui.

## 1. Identitas dan credential per perangkat

Setiap PC Student memiliki:

- `ClassroomId` yang mengikat perangkat ke satu lab;
- `DeviceId` acak dan permanen;
- nama PC yang ikut ditandatangani;
- versi kunci;
- secret perangkat unik hasil HMAC-SHA256 dari root secret kelas.

Credential disimpan di:

```text
%ProgramData%\LabKom\Security\device.credential
```

Isi file dilindungi DPAPI `LocalMachine`. ACL memberi `SYSTEM` dan Local
Administrators hak tulis, sedangkan pengguna biasa hanya dapat membaca untuk
kebutuhan Student Desktop. Server memvalidasi kombinasi `ClassroomId`,
`DeviceId`, nama PC, versi, dan secret; menyalin credential ke PC dengan nama
berbeda tidak menghasilkan identitas yang valid.

Ini adalah identitas perangkat berbasis credential terkelola, bukan hardware
attestation TPM. Administrator lokal tetap merupakan batas kepercayaan sistem
Windows dan secara teknis memiliki kendali akhir atas mesin.

## 2. Rotasi kunci

Teacher menerima versi aktif dan satu versi sebelumnya sebagai grace window.
Agent yang masih memakai versi grace menerima challenge acak. Agent menurunkan
secret versi baru dari root provisioning, menyimpannya atomik, lalu mengirim
receipt HMAC. Teacher baru menandai rotasi selesai setelah bukti kepemilikan
kunci baru valid.

Jalankan dari terminal **Run as administrator** pada PC Teacher:

```powershell
.\LabKom.Provisioning.exe key-status
.\LabKom.Provisioning.exe key-rotate
```

Jangan melakukan rotasi kedua sebelum semua PC yang diharapkan telah online dan
menyelesaikan rotasi pertama. PC yang tertinggal lebih dari grace window tidak
dapat terhubung untuk menerima challenge.

Untuk memulihkan PC yang tertinggal, lihat versi aktif di Teacher, kemudian
jalankan pada PC Student sebagai administrator:

```powershell
.\LabKom.Provisioning.exe device-status
.\LabKom.Provisioning.exe device-enroll --key-version 3
```

Ganti `3` dengan versi aktif yang ditampilkan oleh `key-status`. Bundle/root
provisioning kelas harus masih terpasang pada PC tersebut.

## 3. RBAC berbasis identitas Windows

Password dashboard dan RBAC adalah dua lapisan berbeda. Setelah login dashboard
berhasil, setiap operasi sensitif tetap diperiksa terhadap SID akun Windows yang
menjalankan Teacher.

| Role | Hak utama |
|---|---|
| `Observer` | melihat kelas dan layar |
| `Auditor` | melihat kelas dan audit, tanpa kontrol siswa |
| `Instructor` | perhatian/lock, chat, file, share layar, policy, dan power |
Perintah machine-level melalui `LabKom.Provisioning.exe` tetap wajib dijalankan
dengan elevasi Windows Local Administrator, termasuk bila SID sudah memperoleh
role aplikasi `Administrator`.

| `Administrator` | seluruh hak, termasuk perangkat dan emergency unlock |

Anggota grup Windows Local Administrators selalu memperoleh role
`Administrator`. Akun lain tanpa assignment eksplisit menjadi `Observer`.

Cari SID akun:

```powershell
whoami /user
```

Kelola assignment dari terminal **Run as administrator**:

```powershell
.\LabKom.Provisioning.exe rbac-list
.\LabKom.Provisioning.exe rbac-grant --sid S-1-5-21-... --role Instructor
.\LabKom.Provisioning.exe rbac-revoke --sid S-1-5-21-...
```

Policy disimpan di `%ProgramData%\LabKom\Security\rbac-policy.json` dengan ACL
yang hanya memberi Local Administrators dan `SYSTEM` hak tulis.

## 4. Audit tamper-evident dan fail-closed

Setiap record audit memiliki nomor urut, hash record sebelumnya, dan HMAC. Anchor
terproteksi DPAPI mendeteksi modifikasi, pengurutan ulang, dan pemotongan log.
Rantai diverifikasi lagi sebelum setiap append. Jika integritas gagal, Teacher
tidak membuka kontrol kelas dan operasi administratif tidak dijalankan.

Ada dua jurnal:

- operasi Teacher dan autentikasi Hub:
  `%LOCALAPPDATA%\LabKom\Audit\security-audit.jsonl`;
- perubahan keamanan mesin seperti RBAC, rotasi, dan emergency unlock:
  `%ProgramData%\LabKom\Security\AdminAudit\security-admin-audit.jsonl`.

Jurnal administratif dan anchor-nya menggunakan ACL khusus
`SYSTEM`/Administrators serta DPAPI `LocalMachine`. Verifikasi dan baca record
terakhir dengan:

```powershell
.\LabKom.Provisioning.exe audit-verify
.\LabKom.Provisioning.exe audit-tail --count 100
```

Audit ini tamper-evident pada host dan membuat aplikasi gagal tertutup. Untuk
non-repudiation terhadap administrator lokal, salin log secara berkala ke
server log terpisah/WORM karena administrator Windows tetap dapat menguasai
seluruh file lokal.

Jika audit dinyatakan rusak, jangan menghapus file. Salin jurnal dan anchor
sebagai bukti, bandingkan dengan backup terakhir yang valid, lalu pulihkan
pasangan jurnal+anchor bersama-sama.

## 5. Urutan rollout aman dari versi lama

1. Backup bundle provisioning, konfigurasi lokal, database, dan log audit.
2. Pasang Teacher `0.6.0` dengan `Security:AllowLegacySharedSecret=true`.
3. Pasang Student `0.6.0` bertahap. Agent otomatis membuat credential perangkat.
4. Pastikan PC pilot dapat reconnect, menerima kontrol, dan mengunduh file.
5. Lakukan satu rotasi, lalu verifikasi seluruh PC pilot reconnect normal.
6. Tambahkan assignment SID untuk akun guru non-administrator.
7. Setelah seluruh lab sudah `0.6.0` dan masa rollback berakhir, ubah Teacher ke
   `Security:AllowLegacySharedSecret=false`.
8. Setelah langkah 7 stabil, ubah Student ke
   `Security:RestrictBootstrapSecret=true` lalu restart Agent.

Kedua opsi keamanan terakhir sengaja default ke mode kompatibel. Mengaktifkan
`RestrictBootstrapSecret` membuat versi Student lama tidak dapat membaca root
secret lagi; lakukan hanya setelah jendela rollback benar-benar ditutup.

Contoh `appsettings.Local.json` Teacher setelah migrasi selesai:

```json
{
  "Security": {
    "AllowLegacySharedSecret": false
  }
}
```

Contoh `appsettings.Local.json` Student setelah migrasi selesai:

```json
{
  "Security": {
    "RestrictBootstrapSecret": true
  }
}
```
