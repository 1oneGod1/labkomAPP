# Installer, Signing, Update, dan Rollback LabKom Native

Dokumen ini berlaku untuk aplikasi native .NET di folder `LabKom.NET`. Installer Electron lama tidak digunakan.
Untuk rollout identitas perangkat, rotasi kunci, RBAC, dan audit versi 0.6.0,
ikuti [SECURITY-IDENTITY-RBAC-AUDIT.md](SECURITY-IDENTITY-RBAC-AUDIT.md).


## Hasil release

Pipeline menghasilkan:

- `LabKom-Teacher-Setup-<versi>.exe`
- `LabKom-Student-Setup-<versi>.exe`
- paket update ZIP Teacher dan Student
- manifest update bertanda tangan RSA-PSS
- `LabKom-Publisher.cer`
- `SHA256SUMS.txt`

Installer bersifat self-contained x64 sehingga PC target tidak perlu memasang .NET Runtime.

## 1. Membuat identitas penerbit sendiri

Jalankan sekali di komputer build tepercaya:

```powershell
cd C:\Labkom\LabKom.NET
$password = Read-Host "Password PFX" -AsSecureString
.\scripts\New-CodeSigningCertificate.ps1 -PfxPassword $password -Publisher "Nama Anda"
```

Hasil default berada di `Documents\LabKom-Signing`:

- `LabKom-CodeSigning.pfx`: private key penerbit. Rahasiakan dan backup offline.
- `LabKom-Publisher.cer`: public certificate. Aman dibagikan ke PC sekolah.

Sertifikat buatan sendiri tidak otomatis dipercaya Windows. Sebelum menjalankan installer, distribusikan public CER lewat Group Policy, Intune, atau jalankan sebagai Administrator pada setiap PC:

```powershell
.\scripts\Install-PublisherCertificate.ps1 `
  -CertificatePath .\LabKom-Publisher.cer `
  -IUnderstandSelfSignedTrustRisk
```

Jangan pernah memasang atau menyalin PFX ke PC siswa. Jika ingin Windows/SmartScreen mempercayai publisher tanpa distribusi CER internal, gunakan sertifikat code-signing dari CA publik atau layanan signing tepercaya.

## 2. Build release lokal

Prasyarat komputer build:

- PowerShell 7.2+
- .NET 8 SDK
- Windows 10/11 SDK yang menyediakan `signtool.exe`

```powershell
cd C:\Labkom\LabKom.NET
$env:LABKOM_SIGNING_PASSWORD = "password-PFX"
.\scripts\build-release.ps1 `
  -Version 0.8.0 `
  -PfxPath "$HOME\Documents\LabKom-Signing\LabKom-CodeSigning.pfx"
Remove-Item Env:\LABKOM_SIGNING_PASSWORD
```

Artefak berada di `LabKom.NET\artifacts\release`. Folder tersebut dan seluruh PFX/bundle provisioning diabaikan Git.

## 3. Signing otomatis melalui GitHub

Tambahkan dua GitHub Actions secrets:

- `LABKOM_SIGNING_PFX_BASE64`
- `LABKOM_SIGNING_PFX_PASSWORD`

Buat Base64 PFX tanpa menampilkannya di terminal:

```powershell
$bytes = [IO.File]::ReadAllBytes("$HOME\Documents\LabKom-Signing\LabKom-CodeSigning.pfx")
[Convert]::ToBase64String($bytes) | Set-Clipboard
```

Masukkan clipboard sebagai `LABKOM_SIGNING_PFX_BASE64`, lalu password PFX sebagai secret kedua. Workflow `.github/workflows/release-native.yml` akan build, test, sign, dan menerbitkan GitHub Release ketika tag berikut di-push:

```powershell
git tag native-v0.8.0
git push origin native-v0.8.0
```

Private key tidak ditaruh di repository maupun release asset.

## 4. Instalasi Teacher dan pembuatan provisioning

Jalankan setup Teacher sebagai Administrator:

```powershell
.\LabKom-Teacher-Setup-0.8.0.exe --classroom "Lab Komputer"
```

Pada instalasi pertama, setup:

1. menghasilkan secret acak 48-byte;
2. menyimpannya dengan Windows DPAPI LocalMachine di `%ProgramData%\LabKom\Security\classroom.secret`;
3. membuat bundle di `C:\Users\Public\Documents\LabKom\Provisioning`;
4. mendaftarkan firewall, shortcut, uninstaller, dan task update.

Bundle `*.provision.json` berisi credential plaintext. Simpan offline, batasi ACL/akses USB, jangan commit ke Git, dan hapus salinannya dari PC/USB yang tidak lagi diperlukan.

Jika Teacher lama sudah memiliki system environment `LABKOM_SHARED_SECRET`, installer akan memigrasikan nilainya ke DPAPI agar Client lama tetap kompatibel.

## 5. Instalasi Student

Untuk PC baru:

```powershell
.\LabKom-Student-Setup-0.8.0.exe --bundle "E:\LabKom-Lab-Komputer-xxxxxxxx.provision.json"
```

Untuk deployment tanpa UI:

```powershell
.\LabKom-Student-Setup-0.8.0.exe --silent --bundle "E:\LabKom.provision.json"
```

Installer mendaftarkan:

- `LabKomStudentAgent` sebagai Windows Service dengan automatic recovery;
- `LabKomStudentDesktop` sebagai scheduled task setiap user logon;
- firewall discovery;
- updater bertanda tangan setiap satu jam;
- entri uninstall Windows.

Pada PC lama yang sudah memiliki `LABKOM_SHARED_SECRET`, installer dapat dijalankan tanpa `--bundle` dan akan memigrasikan secret lama. Bundle tetap disarankan untuk menjaga metadata kelas konsisten.

## 6. Cara updater bekerja

Updater hanya menerima manifest melalui HTTPS dan akan:

1. memverifikasi signature RSA-PSS menggunakan public certificate yang tertanam di installer;
2. memverifikasi SHA-256 ZIP;
3. menolak path traversal dan symbolic link dalam ZIP;
4. menghentikan komponen dengan aman;
5. memindahkan versi aktif menjadi backup;
6. memasang versi baru dan menjalankan health-check;
7. mengembalikan backup otomatis jika service/startup gagal.

Update Teacher ditunda jika Teacher masih terbuka. Log tersedia di:

```text
%ProgramData%\LabKom\Updates\Logs
%ProgramData%\LabKom\Installer\Logs
```

## 7. Rollback manual

Jalankan PowerShell sebagai Administrator.

Teacher:

```powershell
& "$env:ProgramFiles\LabKom\Updater\LabKom.Updater.exe" rollback `
  --component Teacher `
  --install-dir "$env:ProgramFiles\LabKom\Teacher"
```

Student:

```powershell
& "$env:ProgramFiles\LabKom\Updater\LabKom.Updater.exe" rollback `
  --component Student `
  --install-dir "$env:ProgramFiles\LabKom\Student" `
  --service LabKomStudentAgent `
  --desktop-task LabKomStudentDesktop
```

Satu versi sebelumnya dipertahankan. Backup lama baru dibuang setelah versi berikutnya lolos health-check.

## 8. Uninstall

Gunakan Settings > Apps, atau:

```powershell
.\LabKom-Student-Setup-0.8.0.exe --uninstall
.\LabKom-Teacher-Setup-0.8.0.exe --uninstall
```

Secret dipertahankan untuk reinstall. Hapus secret hanya jika komputer benar-benar keluar dari kelas:

```powershell
.\LabKom-Student-Setup-0.8.0.exe --uninstall --purge-secret
```

## Batas keamanan yang perlu diketahui

DPAPI melindungi secret saat tersimpan, tetapi Student Desktop harus dapat membacanya pada mesin yang sama. Karena itu akun siswa tetap wajib non-admin, software execution policy perlu diterapkan, dan rotasi secret harus dilakukan dengan bundle baru ke seluruh Teacher/Student secara terkoordinasi.

Mengganti sertifikat signing berarti mengganti trust anchor updater. Lakukan rotasi sertifikat melalui installer penuh yang ditandatangani sertifikat lama, bukan hanya melalui paket update.
