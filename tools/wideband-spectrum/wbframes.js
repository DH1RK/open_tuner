// Per-frame FWHM of the strongest narrow signal near 10498.5 MHz, same algorithm as signal.cs (no averaging)
const frames = parseInt(process.argv[2] || '80');
const ws = new WebSocket('wss://eshail.batc.org.uk/wb/fft', 'fft_m0dtslivetune'); ws.binaryType = 'arraybuffer';
const lin = (v) => Math.pow(10, v / 4096 / 10);
function fwhm(fft, first, last) {
  const ns = []; for (let i = Math.max(0, first - 45); i <= first - 8; i++) ns.push(fft[i]); for (let i = last + 8; i <= Math.min(fft.length - 1, last + 45); i++) ns.push(fft[i]);
  ns.sort((a, b) => a - b); const nl = lin(ns[ns.length >> 1]); const p = []; let k = 0;
  for (let i = first; i <= last; i++) { p.push(Math.max(0, lin(fft[i]) - nl)); if (p[p.length - 1] > p[k]) k = p.length - 1; }
  const lev = 0.5 * p[k]; let l = k; while (l > 0 && p[l] >= lev) l--; let r = k; while (r < p.length - 1 && p[r] >= lev) r++;
  return ((r - (lev - p[r]) / (p[r - 1] - p[r])) - (l + (lev - p[l]) / (p[l + 1] - p[l]))) * 9000 / fft.length;
}
const vals = []; let n = 0;
ws.onmessage = (ev) => {
  const fft = new Uint16Array(ev.data); const nominal = Math.round((10498.5 - 10490.5) / 9 * fft.length);
  let c = nominal; for (let i = nominal - 40; i <= nominal + 40; i++) if (fft[i] > fft[c]) c = i;
  // like the app: bounds = bins above the half-height around the peak, padded by 5 (here simply +-12 bins around the peak)
  vals.push(fwhm(fft, c - 12, c + 12));
  if (++n >= frames) {
    const m = vals.reduce((a, b) => a + b, 0) / vals.length, sd = Math.sqrt(vals.reduce((a, b) => a + (b - m) * (b - m), 0) / vals.length);
    console.log(`${n} frames: mean ${m.toFixed(1)} kHz, std ${sd.toFixed(2)}, min ${Math.min(...vals).toFixed(1)}, max ${Math.max(...vals).toFixed(1)}`);
    process.exit(0);
  }
};
ws.onerror = (e) => { console.log('ws error'); process.exit(1); };
setTimeout(() => process.exit(0), 30000);
