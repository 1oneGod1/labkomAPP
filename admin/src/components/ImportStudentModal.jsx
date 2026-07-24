import { useState, useEffect } from 'react';
import {
  X,
  Upload,
  FileSpreadsheet,
  Download,
  CheckCircle2,
  AlertTriangle,
  Loader2,
  FileText,
  RefreshCw,
  Check,
  HelpCircle,
} from 'lucide-react';

const API =
  typeof window !== 'undefined' && window.location.protocol === 'file:'
    ? 'http://localhost:3001'
    : '';

/**
 * Modal untuk Import Data Login Siswa dari Excel / CSV
 */
export default function ImportStudentModal({ onClose, onImported }) {
  const [file, setFile] = useState(null);
  const [parsedRows, setParsedRows] = useState([]);
  const [parsing, setParsing] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState('');
  const [result, setResult] = useState(null);

  // Tutup dengan ESC
  useEffect(() => {
    const handler = (e) => e.key === 'Escape' && onClose();
    window.addEventListener('keydown', handler);
    return () => window.removeEventListener('keydown', handler);
  }, [onClose]);

  // Handler Download Template CSV
  const handleDownloadTemplate = async () => {
    try {
      const response = await fetch(`${API}/api/students/template`, {
        headers: {
          ...(sessionStorage.getItem('admin_token')
            ? { Authorization: `Bearer ${sessionStorage.getItem('admin_token')}` }
            : {}),
        },
      });
      if (!response.ok) throw new Error('Gagal mengunduh template');
      const blob = await response.blob();
      const url = window.URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = 'template_login_siswa.csv';
      document.body.appendChild(a);
      a.click();
      a.remove();
      window.URL.revokeObjectURL(url);
    } catch (err) {
      // Fallback client-side template download
      const csvContent =
        '\uFEFF' +
        [
          'nis,nama_lengkap,kelas,password',
          '1001,Ahmad Fauzi,X-IPA-1,siswa123',
          '1002,Budi Santoso,X-IPA-1,siswa123',
          '1003,Citra Dewi,X-IPS-2,siswa123',
        ].join('\r\n');
      const blob = new Blob([csvContent], { type: 'text/csv;charset=utf-8;' });
      const url = window.URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = 'template_login_siswa.csv';
      document.body.appendChild(a);
      a.click();
      a.remove();
      window.URL.revokeObjectURL(url);
    }
  };

  // Parser CSV / Delimited Text
  const parseCSVText = (text) => {
    const lines = text
      .split(/\r?\n/)
      .map((l) => l.trim())
      .filter(Boolean);
    if (lines.length === 0) return [];

    // Deteksi delimiter: koma, titik koma, atau tab
    const firstLine = lines[0];
    let delimiter = ',';
    if (firstLine.includes(';')) delimiter = ';';
    else if (firstLine.includes('\t')) delimiter = '\t';

    const parseLine = (line) => {
      const result = [];
      let current = '';
      let inQuotes = false;
      for (let i = 0; i < line.length; i++) {
        const char = line[i];
        if (char === '"') {
          inQuotes = !inQuotes;
        } else if (char === delimiter && !inQuotes) {
          result.push(current.trim().replace(/^"|"$/g, ''));
          current = '';
        } else {
          current += char;
        }
      }
      result.push(current.trim().replace(/^"|"$/g, ''));
      return result;
    };

    const headers = parseLine(lines[0]).map((h) =>
      h.toLowerCase().replace(/[^a-z0-9_]/g, '')
    );

    // Map indeks kolom
    const getColIndex = (names) =>
      headers.findIndex((h) => names.some((n) => h.includes(n)));

    const nisIdx = getColIndex(['nis', 'id_siswa', 'username']);
    const namaIdx = getColIndex(['nama_lengkap', 'nama', 'fullname', 'name']);
    const kelasIdx = getColIndex(['kelas', 'class', 'rombel']);
    const passIdx = getColIndex(['password', 'pass', 'kata_sandi']);

    const rows = [];
    for (let i = 1; i < lines.length; i++) {
      const cols = parseLine(lines[i]);
      if (cols.length === 0 || (cols.length === 1 && !cols[0])) continue;

      const nis = nisIdx >= 0 ? cols[nisIdx] || '' : cols[0] || '';
      const nama = namaIdx >= 0 ? cols[namaIdx] || '' : cols[1] || '';
      const kelas = kelasIdx >= 0 ? cols[kelasIdx] || '' : cols[2] || '';
      const password = passIdx >= 0 ? cols[passIdx] || '' : cols[3] || '';

      const isValid = !!(nis && nama);
      rows.push({
        rowNum: i,
        nis,
        nama_lengkap: nama,
        kelas,
        password,
        isValid,
        error: !nis
          ? 'NIS kosong'
          : !nama
          ? 'Nama kosong'
          : null,
      });
    }

    return rows;
  };

  // Handler Pilih File
  const handleFileChange = (e) => {
    const selected = e.target.files?.[0];
    if (!selected) return;

    setFile(selected);
    setError('');
    setResult(null);
    setParsing(true);

    const reader = new FileReader();
    reader.onload = (evt) => {
      try {
        const text = evt.target?.result || '';
        const rows = parseCSVText(String(text));
        if (rows.length === 0) {
          setError('File kosong atau format kolom tidak dikenali.');
        }
        setParsedRows(rows);
      } catch (err) {
        setError('Gagal membaca isi file: ' + err.message);
      } finally {
        setParsing(false);
      }
    };
    reader.onerror = () => {
      setError('Gagal membaca file.');
      setParsing(false);
    };
    reader.readAsText(selected, 'UTF-8');
  };

  // Submit Import Ke Server
  const handleImportSubmit = async () => {
    const validRows = parsedRows.filter((r) => r.isValid);
    if (validRows.length === 0) {
      setError('Tidak ada data valid untuk di-import.');
      return;
    }

    setSubmitting(true);
    setError('');

    try {
      const payload = validRows.map((r) => ({
        nis: r.nis,
        nama_lengkap: r.nama_lengkap,
        kelas: r.kelas,
        password: r.password,
      }));

      const res = await fetch(`${API}/api/students/import-batch`, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          ...(sessionStorage.getItem('admin_token')
            ? { Authorization: `Bearer ${sessionStorage.getItem('admin_token')}` }
            : {}),
        },
        body: JSON.stringify({ students: payload }),
      });

      const data = await res.json();
      if (!data.success) {
        setError(data.message || 'Gagal meng-import data.');
      } else {
        setResult(data);
        if (onImported) onImported();
      }
    } catch (err) {
      setError('Koneksi ke server gagal: ' + err.message);
    } finally {
      setSubmitting(false);
    }
  };

  const validCount = parsedRows.filter((r) => r.isValid).length;
  const invalidCount = parsedRows.filter((r) => !r.isValid).length;

  return (
    <div className="fixed inset-0 bg-slate-900/50 backdrop-blur-sm z-50 flex items-center justify-center p-4 animate-in fade-in">
      <div className="bg-white rounded-2xl shadow-2xl w-full max-w-2xl max-h-[90vh] flex flex-col animate-in zoom-in-95 duration-300 overflow-hidden">
        {/* Header Modal */}
        <div className="bg-slate-900 text-white p-5 rounded-t-2xl flex justify-between items-center shrink-0">
          <div className="flex items-center space-x-3">
            <FileSpreadsheet className="w-6 h-6 text-emerald-400" />
            <div>
              <h3 className="font-bold text-lg">Import Data Login Siswa (Excel / CSV)</h3>
              <p className="text-xs text-slate-400">
                Tambah atau update akun login siswa secara massal
              </p>
            </div>
          </div>
          <button
            onClick={onClose}
            className="text-slate-400 hover:text-white transition-colors"
          >
            <X className="w-5 h-5" />
          </button>
        </div>

        {/* Content Body */}
        <div className="p-6 overflow-y-auto space-y-5 flex-1">
          {/* Step 1: Download Template & Petunjuk */}
          <div className="bg-emerald-50 border border-emerald-200 rounded-xl p-4 flex flex-col sm:flex-row items-start sm:items-center justify-between gap-4">
            <div>
              <h4 className="font-bold text-emerald-900 text-sm flex items-center gap-1.5">
                <FileText className="w-4 h-4 text-emerald-600" />
                Format Kolom Excel / CSV:
              </h4>
              <p className="text-xs text-emerald-700 mt-1">
                Kolom wajib: <code className="font-bold">nis</code>,{' '}
                <code className="font-bold">nama_lengkap</code>,{' '}
                <code className="font-bold">kelas</code>,{' '}
                <code className="font-bold">password</code>
              </p>
            </div>
            <button
              type="button"
              onClick={handleDownloadTemplate}
              className="px-4 py-2 bg-emerald-600 hover:bg-emerald-700 text-white rounded-lg text-xs font-semibold flex items-center space-x-2 shrink-0 transition-colors shadow-sm"
            >
              <Download className="w-4 h-4" />
              <span>Download Template Excel</span>
            </button>
          </div>

          {/* Result View jika import selesai */}
          {result ? (
            <div className="space-y-4 animate-in fade-in">
              <div className="bg-green-50 border border-green-200 rounded-xl p-5 text-center">
                <CheckCircle2 className="w-10 h-10 text-green-600 mx-auto mb-2" />
                <h4 className="font-bold text-slate-800 text-base">
                  Proses Import Selesai!
                </h4>
                <p className="text-sm text-slate-600 mt-1">{result.message}</p>
              </div>

              <div className="grid grid-cols-3 gap-3 text-center">
                <div className="p-3 bg-emerald-50 border border-emerald-200 rounded-xl">
                  <p className="text-2xl font-bold text-emerald-700">
                    {result.importedCount}
                  </p>
                  <p className="text-xs font-medium text-emerald-600">Siswa Baru</p>
                </div>
                <div className="p-3 bg-blue-50 border border-blue-200 rounded-xl">
                  <p className="text-2xl font-bold text-blue-700">
                    {result.updatedCount}
                  </p>
                  <p className="text-xs font-medium text-blue-600">Di-Update</p>
                </div>
                <div className="p-3 bg-red-50 border border-red-200 rounded-xl">
                  <p className="text-2xl font-bold text-red-700">
                    {result.failedCount}
                  </p>
                  <p className="text-xs font-medium text-red-600">Gagal</p>
                </div>
              </div>

              {result.errors && result.errors.length > 0 && (
                <div className="bg-amber-50 border border-amber-200 rounded-xl p-4 max-h-40 overflow-y-auto">
                  <p className="text-xs font-bold text-amber-800 mb-2">
                    Detail Baris Gagal ({result.errors.length}):
                  </p>
                  <ul className="text-xs text-amber-700 space-y-1 list-disc pl-4 font-mono">
                    {result.errors.map((err, i) => (
                      <li key={i}>{err}</li>
                    ))}
                  </ul>
                </div>
              )}

              <div className="pt-2">
                <button
                  type="button"
                  onClick={onClose}
                  className="w-full py-2.5 bg-slate-900 hover:bg-slate-800 text-white rounded-xl font-medium transition-colors"
                >
                  Selesai & Tutup
                </button>
              </div>
            </div>
          ) : (
            /* Upload & Preview View */
            <div className="space-y-4">
              {/* File Upload Area */}
              <div>
                <label className="block text-sm font-medium text-slate-700 mb-1.5">
                  Pilih File Excel / CSV (.csv, .xlsx, .txt)
                </label>
                <div className="border-2 border-dashed border-slate-300 hover:border-emerald-500 rounded-2xl p-6 text-center transition-colors bg-slate-50 relative cursor-pointer">
                  <input
                    type="file"
                    accept=".csv, .xlsx, .xls, .txt"
                    onChange={handleFileChange}
                    className="absolute inset-0 w-full h-full opacity-0 cursor-pointer"
                  />
                  <Upload className="w-8 h-8 text-slate-400 mx-auto mb-2" />
                  <p className="text-sm font-semibold text-slate-700">
                    {file ? file.name : 'Klik atau tarik file Excel / CSV ke sini'}
                  </p>
                  <p className="text-xs text-slate-400 mt-1">
                    Mendukung format .csv atau file spreadsheet teks
                  </p>
                </div>
              </div>

              {/* Parsing Indicator */}
              {parsing && (
                <div className="flex items-center justify-center py-6 text-emerald-600 space-x-2">
                  <Loader2 className="w-5 h-5 animate-spin" />
                  <span className="text-sm font-medium">Memproses file Excel/CSV…</span>
                </div>
              )}

              {/* Error Alert */}
              {error && (
                <div className="p-3.5 bg-red-50 border border-red-200 text-red-700 rounded-xl text-sm flex items-start gap-2">
                  <AlertTriangle className="w-4 h-4 shrink-0 mt-0.5" />
                  <span>{error}</span>
                </div>
              )}

              {/* Preview Data Table */}
              {parsedRows.length > 0 && !parsing && (
                <div className="space-y-3">
                  <div className="flex items-center justify-between text-xs text-slate-600">
                    <span className="font-bold">
                      Pratinjau Data ({parsedRows.length} baris terdeteksi):
                    </span>
                    <div className="flex gap-2">
                      <span className="bg-emerald-100 text-emerald-700 px-2 py-0.5 rounded-full font-semibold">
                        ✓ {validCount} valid
                      </span>
                      {invalidCount > 0 && (
                        <span className="bg-red-100 text-red-700 px-2 py-0.5 rounded-full font-semibold">
                          ⚠ {invalidCount} tidak valid
                        </span>
                      )}
                    </div>
                  </div>

                  <div className="border border-slate-200 rounded-xl overflow-hidden max-h-56 overflow-y-auto">
                    <table className="w-full text-left text-xs border-collapse">
                      <thead className="bg-slate-100 sticky top-0 text-slate-600 font-semibold border-b border-slate-200">
                        <tr>
                          <th className="p-2.5">#</th>
                          <th className="p-2.5">NIS</th>
                          <th className="p-2.5">Nama Lengkap</th>
                          <th className="p-2.5">Kelas</th>
                          <th className="p-2.5">Password</th>
                          <th className="p-2.5 text-center">Status</th>
                        </tr>
                      </thead>
                      <tbody className="divide-y divide-slate-200 font-mono">
                        {parsedRows.map((r, i) => (
                          <tr
                            key={i}
                            className={r.isValid ? 'hover:bg-slate-50' : 'bg-red-50/70'}
                          >
                            <td className="p-2.5 text-slate-400">{r.rowNum}</td>
                            <td className="p-2.5 font-bold text-slate-800">{r.nis || '-'}</td>
                            <td className="p-2.5 text-slate-700">{r.nama_lengkap || '-'}</td>
                            <td className="p-2.5 text-slate-600">{r.kelas || '-'}</td>
                            <td className="p-2.5 text-slate-500">
                              {r.password ? '••••••' : '(kosong)'}
                            </td>
                            <td className="p-2.5 text-center">
                              {r.isValid ? (
                                <span className="inline-flex items-center gap-1 text-emerald-600 font-sans font-semibold">
                                  <Check className="w-3.5 h-3.5" /> Valid
                                </span>
                              ) : (
                                <span className="inline-flex items-center gap-1 text-red-600 font-sans font-semibold" title={r.error}>
                                  <AlertTriangle className="w-3.5 h-3.5" /> {r.error}
                                </span>
                              )}
                            </td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                </div>
              )}
            </div>
          )}
        </div>

        {/* Footer Actions */}
        {!result && (
          <div className="p-4 bg-slate-50 border-t border-slate-200 flex justify-end space-x-3 shrink-0 rounded-b-2xl">
            <button
              type="button"
              onClick={onClose}
              className="px-5 py-2.5 bg-white border border-slate-300 text-slate-700 hover:bg-slate-100 rounded-xl text-sm font-medium transition-colors"
            >
              Batal
            </button>
            <button
              type="button"
              onClick={handleImportSubmit}
              disabled={submitting || validCount === 0}
              className="px-6 py-2.5 bg-emerald-600 hover:bg-emerald-700 disabled:bg-emerald-300 text-white rounded-xl text-sm font-semibold transition-colors flex items-center space-x-2 shadow-sm"
            >
              {submitting && <Loader2 className="w-4 h-4 animate-spin" />}
              <span>Import {validCount > 0 ? `(${validCount} Siswa)` : ''}</span>
            </button>
          </div>
        )}
      </div>
    </div>
  );
}
