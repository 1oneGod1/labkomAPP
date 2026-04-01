/**
 * api.js — Utility HTTP request untuk client LabKom
 *
 * Di Electron (file:// protocol), fetch diblokir Chromium untuk request ke
 * http://IP:3001. Solusi: route semua request lewat IPC ke main process
 * yang menggunakan Node.js http module secara langsung.
 *
 * Fallback: fetch biasa (dev mode / browser biasa).
 */

export async function apiRequest(url, options = {}) {
  // Gunakan IPC jika tersedia (Electron production/dev)
  if (window.electronAPI?.apiRequest) {
    const result = await window.electronAPI.apiRequest(url, {
      method:  options.method  || 'GET',
      headers: options.headers || {},
      body:    options.body    || null,
    });
    // Lempar error jika server return 4xx/5xx agar catch block di caller bekerja
    if (!result.ok) {
      const err = new Error(result.data?.message || `HTTP ${result.status}`);
      err.status   = result.status;
      err.response = result.data;
      throw err;
    }
    return result.data;
  }

  // Fallback: fetch biasa untuk dev mode browser
  const res = await fetch(url, {
    headers: { 'Content-Type': 'application/json', ...(options.headers || {}) },
    ...options,
  });
  const data = await res.json();
  if (!res.ok) {
    const err = new Error(data?.message || `HTTP ${res.status}`);
    err.status   = res.status;
    err.response = data;
    throw err;
  }
  return data;
}

/**
 * Versi yang mengembalikan { ok, status, data } tanpa throw —
 * cocok untuk tempat yang butuh cek manual res.ok
 */
export async function apiCall(url, options = {}) {
  try {
    if (window.electronAPI?.apiRequest) {
      return await window.electronAPI.apiRequest(url, {
        method:  options.method  || 'GET',
        headers: options.headers || {},
        body:    options.body    || null,
      });
    }
    const res  = await fetch(url, {
      headers: { 'Content-Type': 'application/json', ...(options.headers || {}) },
      ...options,
    });
    const data = await res.json();
    return { ok: res.ok, status: res.status, data };
  } catch {
    return { ok: false, status: 0, data: null };
  }
}
