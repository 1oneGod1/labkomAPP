# Student Desktop Safety and Recovery

Versi 0.5.1 menambahkan tiga mekanisme agar komputer siswa tidak tertinggal
dalam kondisi terkunci ketika proses Desktop atau koneksi Teacher bermasalah.

## 1. Watchdog Student Desktop

`LabKomStudentAgent` berjalan sebagai Windows Service (`LocalSystem`) dan
memeriksa session console aktif setiap 5 detik. Jika
`LabKom.Student.Desktop.exe` tidak ada selama 10 detik, service menjalankan
kembali Scheduled Task `LabKomStudentDesktop`. Percobaan berikutnya dibatasi
minimal setiap 30 detik agar tidak terjadi restart loop agresif.

Konfigurasi berada di `Student/Agent/appsettings.json`:

```json
"DesktopWatchdog": {
  "Enabled": true,
  "ScheduledTaskName": "LabKomStudentDesktop",
  "CheckIntervalSeconds": 5,
  "MissingGraceSeconds": 10,
  "RestartCooldownSeconds": 30
}
```

## 2. Recovery timeout

Student Desktop melepas overlay, mode broadcast fullscreen, dan keyboard hook
secara lokal apabila:

- koneksi ke Teacher terputus terus selama 180 detik; atau
- satu periode Attention lock telah aktif selama 120 menit.

Ketika koneksi Teacher pulih, snapshot kelas terbaru diterapkan kembali.
Nilai default dapat diubah di `Student/Desktop/appsettings.json`:

```json
"Recovery": {
  "MaximumLockMinutes": 120,
  "TeacherOfflineUnlockSeconds": 180,
  "PollIntervalMilliseconds": 1000
}
```

Jangan menetapkan offline timeout terlalu pendek karena siswa yang memutus LAN
akan memperoleh auto-release setelah batas tersebut.

## 3. Emergency unlock administrator

Installer dan updater Student membuat shortcut Start Menu:

`LabKom > LabKom Emergency Unlock (Admin)`

Shortcut menjalankan tool dengan manifest `requireAdministrator`, sehingga
Windows meminta kredensial/UAC administrator. Admin harus mengetik `UNLOCK`
sebelum override 15 menit dibuat. File override hanya dapat ditulis atau
dihapus oleh Administrators dan SYSTEM; akun Users hanya mendapat akses baca.

Perintah manual dari Command Prompt Administrator:

```powershell
& "C:\Program Files\LabKom\Student\Admin\LabKom.Provisioning.exe" emergency-unlock --minutes 15 --reason "Teacher rusak"
& "C:\Program Files\LabKom\Student\Admin\LabKom.Provisioning.exe" emergency-status
& "C:\Program Files\LabKom\Student\Admin\LabKom.Provisioning.exe" emergency-clear
```

Durasi emergency unlock dibatasi 1 sampai 120 menit. Saat override berakhir
atau dihapus, Student Desktop memutus-sambung ke Hub agar state kelas aktif
diambil ulang, sehingga perangkat tidak terus berada dalam mode bypass.

## Uji pilot wajib

Pada satu PC siswa cadangan:

1. Login sebagai siswa dan pastikan `LabKom.Student.Desktop.exe` berjalan.
2. Akhiri proses tersebut dari akun admin; pastikan watchdog memulihkannya
   sekitar 10-15 detik.
3. Aktifkan Attention lock lalu tutup Teacher atau putuskan jaringan; pastikan
   auto-release terjadi setelah 180 detik.
4. Aktifkan lock, jalankan shortcut emergency dengan kredensial admin, ketik
   `UNLOCK`, dan pastikan overlay/hook dilepas dalam 1-2 detik.
5. Jalankan `emergency-clear`, lalu pastikan Student tersambung ulang dan
   menerima snapshot kelas terbaru.

Lakukan pilot 3-5 PC sebelum menyebarkan versi ini ke seluruh lab.
