# Monitoring, File Collect, Classroom, dan Technician Console

Dokumen ini menjelaskan modul classroom yang tersedia pada LabKom Native 0.9.x.

## Monitoring aktivitas

Student Desktop mengirim dua jenis event:

- perubahan jendela aktif: judul dan nama proses;
- sampel 15 detik: kategori Desktop/Application/WebBrowser/System, durasi idle, dan jumlah key-down sejak sampel sebelumnya.

Counter keyboard tidak menyimpan virtual-key, scan-code, karakter, urutan tombol, clipboard, atau isi field. Implementasinya tidak dapat dipakai untuk merekonstruksi teks/password. Monitoring web saat ini berarti browser aktif dan judul tab/jendela; URL lengkap, query, form, dan riwayat browser tidak dikumpulkan.

Event masuk ke activity feed Teacher dan SQLite. Sampel browser dipetakan ke jenis BrowserUrl, tetapi detail yang tersimpan tetap judul tab/jendela dan proses, bukan URL penuh.

## File collect

Teacher memilih PC lalu membuka File > Kumpulkan File dari PC Terpilih atau menu Siswa.

Batas keamanan:

- hanya satu path relatif eksak, tanpa wildcard/rekursi;
- root hanya Desktop, Documents, atau Downloads pengguna;
- path traversal, path absolut, reparse point/symlink, dan ekstensi material kunci ditolak;
- ukuran maksimal 20 MB;
- request target-bound dan kedaluwarsa setelah 3 menit;
- siswa melihat dialog persetujuan per request;
- transport menggunakan chunk maksimal 256 KB;
- total byte, sequence, nama file, dan SHA-256 diverifikasi Teacher;
- permission CollectFiles dan hasil akhir dicatat ke security audit.

Hasil tersimpan di:

    %LOCALAPPDATA%\LabKom\CollectedFiles\yyyy-MM-dd\<PC>\<request>-<file>

Permintaan yang tidak dijawab dibersihkan saat siklus request berikutnya.

## Room, register, lesson, dan assessment

Buka Kelas > Room, Register, Lesson & Assessment.

Alur:

1. Pilih PC pada dashboard; tanpa pilihan berarti semua Desktop siswa.
2. Isi nama room dan judul lesson, lalu pilih Mulai Lesson.
3. Student Desktop menerima window register dan mengirim NIS/ID serta nama lengkap.
4. Gunakan Fase Mengajar saat register selesai.
5. Buat soal pilihan ganda dan mulai assessment.
6. Jawaban dikirim manual atau otomatis saat waktu habis.
7. Teacher menghitung skor dari kunci yang tidak pernah dikirim ke Student.
8. Pilih Akhiri Lesson untuk menutup state aktif.

Format satu soal pada editor Teacher:

    Pertanyaan | Pilihan A | Pilihan B | Pilihan C | nomor jawaban benar

Nomor jawaban mulai dari 1. State aktif direplay setelah Student reconnect. Register di-upsert per lesson+PC dan submission assessment di-upsert per assessment+PC. State, kunci lokal, register, dan hasil disimpan atomik di:

    %LOCALAPPDATA%\LabKom\Classroom\lessons.json

Payload lesson dan assessment mempunyai audience, TTL, ukuran/entry limit, identitas GUID, serta validasi jawaban terhadap question ID yang aktif.

## Technician Console

Buka Tools > Technician Console. Hanya role Administrator memiliki permission TechnicianConsole; Instructor tidak.

Console menampilkan:

- koneksi Agent dan Desktop per PC;
- last-seen, status, health telemetry, CPU, RAM, dan latency;
- backend capture dan jumlah monitor;
- versi aplikasi, classroom ID/nama, versi rotasi kunci, dan integritas audit.

Aksi yang tersedia sengaja dibatasi:

- refresh stream satu PC;
- emergency unlock satu PC;
- export diagnostik CSV.

Console tidak menyediakan remote shell, upload executable, registry editor, atau arbitrary command. Semua pembukaan console dan aksi ditolak secara fail-closed bila RBAC/audit tidak valid serta dicatat ke security audit.

## Verifikasi

Pemeriksaan otomatis yang harus tetap lulus:

    dotnet build LabKom.sln --no-restore
    dotnet test tests/LabKom.Tests/LabKom.Tests.csproj --no-build --no-restore
    dotnet format LabKom.sln --verify-no-changes --no-restore

Pemeriksaan otomatis tidak menggantikan pilot fisik. Sebelum rollout, uji 5-40 PC untuk consent file collect, file kosong/besar/berubah saat dibaca, reconnect lesson, register duplikat, expiry dan auto-submit assessment, RBAC Instructor/Admin, emergency unlock, serta export Technician Console.
