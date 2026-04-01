import React, { useState } from 'react';
import {
  Monitor, CheckCircle2, AlertTriangle, Armchair,
  Cpu, ArrowRight, ShieldAlert, Loader2,
} from 'lucide-react';
import { apiCall } from './api.js';

// Form pengecekan kondisi fasilitas sebelum praktikum dimulai
// Props:
//   studentData  – objek data siswa dari login
//   serverUrl    – URL server backend
//   pcName       – nama komputer
//   onComplete   – callback ketika form berhasil disubmit → pindah ke MODE_WIDGET

export default function CheckConditionForm({ studentData, serverUrl, pcName, onComplete }) {
  const [cpuStatus,     setCpuStatus]     = useState(null);
  const [cpuNote,       setCpuNote]       = useState('');
  const [monitorStatus, setMonitorStatus] = useState(null);
  const [monitorNote,   setMonitorNote]   = useState('');
  const [deskStatus,    setDeskStatus]    = useState(null);
  const [deskNote,      setDeskNote]      = useState('');
  const [isSubmitting,  setIsSubmitting]  = useState(false);
  const [submitError,   setSubmitError]   = useState('');

  const isFormValid = () => {
    if (!cpuStatus || !monitorStatus || !deskStatus) return false;
    if (cpuStatus === 'bad' && !cpuNote.trim()) return false;
    if (monitorStatus === 'bad' && !monitorNote.trim()) return false;
    if (deskStatus === 'bad' && !deskNote.trim()) return false;
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
          session_id:     sessionStorage.getItem('session_id') || null,
          nis:            studentData?.nis || '-',
          nama_lengkap:   studentData?.nama_lengkap || '-',
          pc_name:        pcName,
          check_type:     'pre',
          cpu_status:     cpuStatus,
          cpu_note:       cpuNote || null,
          monitor_status: monitorStatus,
          monitor_note:   monitorNote || null,
          desk_status:    deskStatus,
          desk_note:      deskNote || null,
        }),
      });
      if (!res.ok || !res.data?.success) throw new Error(res.data?.message || 'Gagal menyimpan');
      // Resize ke widget dan lanjut ke dashboard sesi
      window.electronAPI?.resizeWindow('regular');
      onComplete();
    } catch (err) {
      setSubmitError('Gagal menyimpan checklist. Hubungi teknisi.');
      console.error(err);
    } finally {
      setIsSubmitting(false);
    }
  };

  // ── Komponen untuk satu baris item checklist ─────────────────────────────
  const CheckItem = ({ icon: Icon, iconColor, title, subtitle, status, onOk, onBad, note, onNote, okLabel = 'Normal', badLabel = 'Bermasalah', placeholder }) => (
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
              ${status === 'ok' ? 'bg-blue-600 text-white shadow-md' : 'text-slate-400 hover:text-white hover:bg-slate-800'}`}>
            <CheckCircle2 className="w-3.5 h-3.5" /><span>{okLabel}</span>
          </button>
          <button type="button" onClick={onBad}
            className={`px-3 py-1.5 rounded-lg text-xs font-medium flex items-center space-x-1.5 transition-all
              ${status === 'bad' ? 'bg-red-600 text-white shadow-md' : 'text-slate-400 hover:text-white hover:bg-slate-800'}`}>
            <AlertTriangle className="w-3.5 h-3.5" /><span>{badLabel}</span>
          </button>
        </div>
      </div>
      <div className={`overflow-hidden transition-all duration-300 ${status === 'bad' ? 'max-h-24 opacity-100 mt-1' : 'max-h-0 opacity-0'}`}>
        <input type="text" value={note} onChange={e => onNote(e.target.value)}
          placeholder={placeholder}
          className="w-full bg-slate-950/50 border border-red-500/50 text-white text-sm rounded-lg px-4 py-2.5 focus:outline-none focus:ring-1 focus:ring-red-500 placeholder-slate-500" />
      </div>
    </div>
  );

  return (
    <div className="min-h-screen bg-slate-950 font-sans relative">
      {/* Background glow */}
      <div className="fixed inset-0 opacity-10 pointer-events-none overflow-hidden">
        <div className="absolute top-0 left-1/4 w-96 h-96 bg-blue-600 rounded-full mix-blend-screen filter blur-[100px]" />
        <div className="absolute bottom-0 right-1/4 w-96 h-96 bg-red-600 rounded-full mix-blend-screen filter blur-[100px]" />
      </div>

      <div className="relative z-10 flex flex-col items-center px-4 py-6">

        <div className="w-full max-w-2xl bg-slate-900/80 backdrop-blur-xl border border-slate-800 p-8 rounded-[2rem] shadow-2xl animate-in zoom-in-95 duration-500">

        {/* Header */}
        <div className="text-center mb-6">
          <div className="inline-flex items-center justify-center p-3 bg-blue-500/10 rounded-2xl mb-3">
            <ShieldAlert className="w-8 h-8 text-blue-500" />
          </div>
          <h1 className="text-2xl font-bold text-white tracking-tight mb-1">Checklist Fasilitas Lab</h1>
          <p className="text-sm text-slate-400 max-w-md mx-auto">
            Periksa kondisi perangkat sebelum mulai. Kerusakan yang tidak dilaporkan saat ini
            akan menjadi <span className="text-red-400 font-semibold">tanggung jawabmu</span>.
          </p>
          {studentData && (
            <div className="mt-3 inline-flex items-center space-x-2 bg-slate-800/60 border border-slate-700 px-4 py-1.5 rounded-full text-sm">
              <span className="text-slate-400">Halo,</span>
              <span className="text-white font-semibold">{studentData.nama_lengkap}</span>
              <span className="text-slate-500">·</span>
              <span className="text-blue-400 font-mono text-xs">{pcName}</span>
            </div>
          )}
        </div>

        <form onSubmit={handleSubmit} className="space-y-2">
          <div className="divide-y divide-slate-800/60 border-y border-slate-800/60">
            <CheckItem
              icon={Cpu} iconColor="text-indigo-400"
              title="PC, Keyboard & Mouse"
              subtitle="Lengkap dan berfungsi dengan normal."
              status={cpuStatus} onOk={() => setCpuStatus('ok')} onBad={() => setCpuStatus('bad')}
              note={cpuNote} onNote={setCpuNote}
              placeholder="Jelaskan kerusakannya... (Misal: Klik kanan mouse tidak merespon)"
            />
            <CheckItem
              icon={Monitor} iconColor="text-sky-400"
              title="Layar Monitor & Kabel"
              subtitle="Layar bersih, tidak pecah & kabel power aman."
              status={monitorStatus} onOk={() => setMonitorStatus('ok')} onBad={() => setMonitorStatus('bad')}
              note={monitorNote} onNote={setMonitorNote}
              placeholder="Jelaskan kerusakannya... (Misal: Layar berkedip-kedip atau bergaris)"
            />
            <CheckItem
              icon={Armchair} iconColor="text-emerald-400"
              title="Meja & Kursi"
              subtitle="Tidak ada kerusakan, bersih & kursi stabil."
              status={deskStatus} onOk={() => setDeskStatus('ok')} onBad={() => setDeskStatus('bad')}
              note={deskNote} onNote={setDeskNote}
              placeholder="Jelaskan kerusakannya... (Misal: Kursi patah atau meja kotor)"
            />
          </div>

          {submitError && (
            <p className="text-center text-red-400 text-sm pt-2">{submitError}</p>
          )}

          <div className="pt-4">
            <button type="submit" disabled={!isFormValid() || isSubmitting}
              className={`w-full flex items-center justify-center space-x-2 py-3.5 rounded-xl text-base font-bold transition-all
                ${isFormValid() && !isSubmitting
                  ? 'bg-blue-600 hover:bg-blue-500 text-white shadow-lg shadow-blue-600/25'
                  : 'bg-slate-800/80 text-slate-500 cursor-not-allowed'}`}>
              {isSubmitting ? (
                <><Loader2 className="w-5 h-5 animate-spin" /><span>Menyimpan...</span></>
              ) : (
                <><span>Mulai Praktikum</span><ArrowRight className="w-5 h-5" /></>
              )}
            </button>
            {!isFormValid() && !isSubmitting && (
              <p className="text-center text-slate-500 text-xs mt-2">
                *Mohon pilih status pada setiap perangkat untuk melanjutkan.
              </p>
            )}
          </div>
        </form>
        </div>
      </div>
    </div>
  );
}
