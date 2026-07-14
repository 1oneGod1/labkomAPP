const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const root = path.resolve(__dirname, '..');
const pkg = JSON.parse(fs.readFileSync(path.join(root, 'package.json'), 'utf8'));
const mainSource = fs.readFileSync(path.join(root, 'electron', 'main.js'), 'utf8');
const hookSource = fs.readFileSync(path.join(root, 'electron', 'blockAltTab.ps1'), 'utf8');

const lockResource = pkg.build?.extraResources?.find((entry) =>
  entry.from === 'electron/blockAltTab.ps1'
  && entry.to === 'electron/blockAltTab.ps1'
);

assert.ok(lockResource, 'blockAltTab.ps1 wajib disalin sebagai extraResources.');
assert.ok(
  pkg.build?.files?.includes('!electron/blockAltTab.ps1'),
  'Helper lock harus dikeluarkan dari app.asar agar PowerShell dapat menjalankannya.',
);
assert.match(
  mainSource,
  /path\.join\(process\.resourcesPath, 'electron', 'blockAltTab\.ps1'\)/,
  'Path helper production harus menggunakan process.resourcesPath.',
);
assert.match(hookSource, /WM_KEYUP/, 'Hook harus memblokir key-up tombol Windows.');
assert.match(hookSource, /WM_SYSKEYUP/, 'Hook harus memblokir key-up shortcut sistem.');

console.log('Lock helper packaging: PASS');
