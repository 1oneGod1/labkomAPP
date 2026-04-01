import React, { useState } from 'react';
import {
  CheckCircle2, AlertTriangle, ArrowRight, ShieldCheck,
  MonitorSmartphone, Trash2, UserX, Settings, FolderLock, Loader2,
} from 'lucide-react';
import { apiCall } from './api.js';

// Form checklist akhir sesi – tampil sebelum siswa logout
// Props:
//   studentData      – data siswa yang sedang login
//   serverUrl        – URL server backend
//   onLogoutConfirmed – callback setelah submit → jalankan logout sesungguhnya

export default function PostSessionCheck({ studentData, serverUrl, onLogoutConfirmed }) {
  const [hwStatus,         setHwStatus]         = useState(null);
  const [hwNote,           setHwNote]           = useState('');
  const [cleanStatus,      setCleanStatus]      = useState(null);
  const [cleanNote,        setCleanNote]        = useState('');
  const [accountStatus,    setAccountStatus]    = useState(null);
  const [accountNote,      setAccountNote]      = useState('');
  const [systemStatus,     setSystemStatus]     = useState(null);
  const [systemNote,       setSystemNote]       = useState('');
  const [fileStatus,       setFileStatus]       = useState(null);
  const [fileNote,         setFileNote]         = useState('');
  const [isSubmitting,     setIsSubmitting]     = useState(false);
  const [submitError,      setSubmitError]      = useState('');

  const isFormValid = () => {
    if (!hwStatus || !cleanStatus || !accountStatus || !systemStatus || !fileStatus) return false;
    if (hwStatus === 'bad'      && !hwNote.trim())      return false;
    if (cleanStatus === 'bad'   && !cleanNote.trim())   return false;
    if (accountStatus === 'bad' && !accountNote.trim()) return false;
    if (systemStatus === 'bad'  && !systemNote.trim())  return false;
    if (fileStatus === 'bad'    && !fileNote.trim())    return false;
    return true;
  };

  const handleSubmit = async (e) => {
    e.preventDefault();
    if (!isFormValid() || isSubmitting) return;
    setIsSubmitting(true);
    setSubmitError('');

    try {
      const res = await apiCall(`${serverUrl}/api/checks`, {
        method:  'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          session_id:          sessionStorage.getItem('session_id') || null,
          nis:                 studentData?.nis || '-',
          nama_lengkap:        studentData?.nama_lengkap || '-',
          pc_name:             studentData?.pc_name || sessionStorage.getItem('pc_name') || '-',
          check_type:          'post',
          hw_status:           hwStatus,
          hw_note:             hwNote || null,
          cleanliness_status:  cleanStatus,
          cleanliness_note:    cleanNote || null,
          account_status:      accountStatus,
          account_note:        accountNote || null,
          system_status:       systemStatus,
          system_note:         systemNote || null,
          file_status:         fileStatus,
          file_note:           fileNote || null,
        }),
      });
      if (!res.ok || !res.data?.success) throw new Error(res.data?.message || 'Gagal menyimpan');
      // Jalankan logout sesungguhnya
      onLogoutConfirmed();
    } catch (err) {
      setSubmitError('Gagal menyimpan checklist. Tetap mencoba logout…');
      console.error(err);
      // Tetap lanjut logout meski gagal simpan agar siswa tidak tertahan
      setTimeout(() => onLogoutConfirmed(), 1500);
    } finally {
      setIsSubmitting(false);
    }
  };

  // ── Komponen baris item ─────────────────────────────────────────────────
  const CheckItem = ({ icon: Icon, iconColor, title, subtitle, status, onOk, okLabel, onBad, badLabel, note, onNote, placeholder }) => (
    <div className="py-5 flex flex-col gap-3">
      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4">
        <div className="flex items-center space-x-3">
          <div className={`p-2.5 rounded-xl ${status === 'bad' ? 'bg-red-500/10' : 'bg-slate-800'}`}>
            <Icon className={`w-5 h-5 ${status === 'bad' ? 'text-red-400' : iconColor}`} />
          </div>
          <div>
            <h3 className="text-base font-semibold text-white">{title}</h3>
            <p className="text-xs text-slate-400">{subtitle}</p>
          </div>
        </div>
        <div className="flex bg-slate-950/50 p-1 rounded-xl shrink-0 border border-slate-800/50">
          <button type="button" onClick={onOk}
            className={`px-3 py-1.5 rounded-lg text-xs font-medium flex items-center space-x-1.5 transition-all
              ${status === 'ok' ? 'bg-indigo-600 text-white shadow-md' : 'text-slate-400 hover:text-white hover:bg-slate-800'}`}>
            <CheckCircle2 className="w-3.5 h-3.5" /><span>{okLabel}</span>
          </button>
          <button type="button" onClick={onBad}
            className={`px-3 py-1.5 rounded-lg text-xs font-medium flex items-center space-x-1.5 transition-all
              ${status === 'bad' ? 'bg-red-600 text-white shadow-md' : 'text-slate-400 hover:text-white hover:bg-slate-800'}`}>
            <AlertTriangle className="w-3.5 h-3.5" /><span>{badLabel}</span>
          </button>
        </div>
      </div>
      {status === 'bad' && (
        <div className="animate-in fade-in slide-in-from-top-2 mt-1">
          <input type="text" value={note} onChange={e => onNote(e.target.value)}
            placeholder={placeholder}
            className="w-full bg-slate-950/50 border border-red-500/50 text-white text-sm rounded-lg px-4 py-2.5 focus:outline-none focus:ring-1 focus:ring-red-500 placeholder-slate-500" />
        </div>
      )}
    </div>
  );

  return (
    <div className="min-h-screen bg-slate-950 font-sans relative">
      {/* Background glow */}
      <div className="fixed inset-0 opacity-10 pointer-events-none overflow-hidden">
        <div className="absolute top-1/4 left-1/4 w-96 h-96 bg-indigo-600 rounded-full mix-blend-screen filter blur-[120px]" />
        <div className="absolute bottom-1/4 right-1/4 w-96 h-96 bg-purple-600 rounded-full mix-blend-screen filter blur-[120px]" />
      </div>

      <div className="relative z-10 flex flex-col items-center px-4 py-6">

        <div className="w-full max-w-2xl bg-slate-900/80 backdrop-blur-xl border border-slate-800 p-8 rounded-[2rem] shadow-2xl animate-in zoom-in-95 duration-500">

        {/* Header */}
        <div className="text-center mb-6">
          <div className="inline-flex items-center justify-center p-3 bg-indigo-500/10 rounded-2xl mb-3">
            <ShieldCheck className="w-8 h-8 text-indigo-500" />
          </div>
          <h1 className="text-2xl font-bold text-white tracking-tight mb-1">Checklist Akhir Praktikum</h1>
          <p className="text-sm text-slate-400 max-w-lg mx-auto">
            Sebelum mengakhiri sesi, pastikan kamu meninggalkan komputer dalam keadaan aman dan rapi.
          </p>
          {studentData && (
            <div className="mt-3 inline-flex items-center space-x-2 bg-slate-800/60 border border-slate-700 px-4 py-1.5 rounded-full text-sm">
              <span className="text-white font-semibold">{studentData.nama_lengkap}</span>
              <span className="text-slate-500">·</span>
              <span className="text-indigo-400">{studentData.kelas}</span>
            </div>
          )}
        </div>

        <form onSubmit={handleSubmit} className="space-y-2">
          <div className="divide-y divide-slate-800/60 border-y border-slate-800/60">
            <CheckItem
              icon={MonitorSmartphone} iconColor="text-blue-400"
              title="Perangkat Keras (Hardware)"
              subtitle="PC, Monitor, Keyboard, & Mouse masih utuh dan normal."
              status={hwStatus}       onOk={() => setHwStatus('ok')}      okLabel="Aman"
              onBad={() => setHwStatus('bad')}    badLabel="Lapor Rusak"
              note={hwNote}           onNote={setHwNote}
              placeholder="Apa yang rusak selama praktikum?..."
            />
            <CheckItem
              icon={Trash2}           iconColor="text-emerald-400"
              title="Kebersihan & Kerapian"
              subtitle="Meja bersih dari sampah & kursi sudah dirapikan kembali."
              status={cleanStatus}    onOk={() => setCleanStatus('ok')}   okLabel="Sudah Rapi"
              onBad={() => setCleanStatus('bad')} badLabel="Ada Kendala"
              note={cleanNote}        onNote={setCleanNote}
              placeholder="Jelaskan... (Misal: Ada coretan membandel di meja)"
            />
            <CheckItem
              icon={UserX}            iconColor="text-orange-400"
              title="Akun Pribadi (Log Out)"
              subtitle="Email, WhatsApp Web, dan Sosmed sudah dikeluarkan."
              status={accountStatus}  onOk={() => setAccountStatus('ok')} okLabel="Sudah Logout"
              onBad={() => setAccountStatus('bad')} badLabel="Ada Kendala"
              note={accountNote}      onNote={setAccountNote}
              placeholder="Jelaskan... (Misal: Browser error tidak bisa log out)"
            />
            <CheckItem
              icon={Settings}         iconColor="text-pink-400"
              title="Sistem & Tampilan Desktop"
              subtitle="Wallpaper tidak diganti & tidak menginstall aplikasi tanpa izin."
              status={systemStatus}   onOk={() => setSystemStatus('ok')}  okLabel="Aman Sesuai Aturan"
              onBad={() => setSystemStatus('bad')} badLabel="Ada Pelanggaran"
              note={systemNote}       onNote={setSystemNote}
              placeholder="Apa yang terjadi? (Misal: Saya terlanjur mengganti wallpaper)"
            />
            <CheckItem
              icon={FolderLock}       iconColor="text-sky-400"
              title="File & Riwayat Browser"
              subtitle="Tidak meninggalkan foto/file sensitif & riwayat pencarian aman."
              status={fileStatus}     onOk={() => setFileStatus('ok')}    okLabel="Bersih & Aman"
              onBad={() => setFileStatus('bad')}   badLabel="Ada Pelanggaran"
              note={fileNote}         onNote={setFileNote}
              placeholder="Jelaskan... (Misal: Lupa menghapus folder tugas di D:)"
            />
          </div>

          {submitError && (
            <p className="text-center text-red-400 text-sm pt-2">{submitError}</p>
          )}

          <div className="pt-4">
            <button type="submit" disabled={!isFormValid() || isSubmitting}
              className={`w-full flex items-center justify-center space-x-2 py-3.5 rounded-xl text-base font-bold transition-all
                ${isFormValid() && !isSubmitting
                  ? 'bg-indigo-600 hover:bg-indigo-500 text-white shadow-lg shadow-indigo-600/25'
                  : 'bg-slate-800/80 text-slate-500 cursor-not-allowed'}`}>
              {isSubmitting ? (
                <><Loader2 className="w-5 h-5 animate-spin" /><span>Menyimpan...</span></>
              ) : (
                <><span>Selesai &amp; Kunci Komputer</span><ArrowRight className="w-5 h-5" /></>
              )}
            </button>
            {!isFormValid() && !isSubmitting && (
              <p className="text-center text-slate-500 text-xs mt-2">
                *Mohon konfirmasi seluruh checklist di atas untuk mengakhiri sesi.
              </p>
            )}
          </div>
        </form>
        </div>
      </div>
    </div>
  );
}
