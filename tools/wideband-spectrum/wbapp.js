// 1:1 mirror of OpenTuner's signal.cs detect_signals + narrow measure, run on live frames.
const frames = parseInt(process.argv[2] || '30');
const ws = new WebSocket('wss://eshail.batc.org.uk/wb/fft', 'fft_m0dtslivetune'); ws.binaryType = 'arraybuffer';
const lin = (v) => Math.pow(10, v / 4096 / 10);
function classify(w) { if (w < 0.0170) return 0; if (w < 0.0250) return 0.020; if (w < 0.0309) return 0.025; if (w < 0.0508) return 0.033; if (w < 0.0966) return 0.066; if (w < 0.187) return 0.125; return -1; }
function alignOld(w) { if (w < 0.022) return 0; if (w < 0.065) return 0.035; if (w < 0.086) return 0.066; if (w < 0.195) return 0.125; if (w < 0.277) return 0.250; if (w < 0.388) return 0.333; if (w < 0.7) return 0.5; if (w < 1.2) return 1; if (w < 1.6) return 1.5; if (w < 2.2) return 2; return Math.round(w * 5) / 5; }
function measure(fft, first, last) {
  first = Math.max(1, first); last = Math.min(fft.length - 2, last); if (last - first < 2) return 0;
  const ns = []; for (let i = Math.max(0, first - 45); i <= first - 8; i++) ns.push(fft[i]); for (let i = last + 8; i <= Math.min(fft.length - 1, last + 45); i++) ns.push(fft[i]);
  let nu = 11500; if (ns.length >= 6) { ns.sort((a, b) => a - b); nu = ns[ns.length >> 1]; }
  const nl = lin(nu), count = last - first + 1, p = new Array(count); let pk = 0;
  for (let i = 0; i < count; i++) { p[i] = Math.max(0, lin(fft[first + i]) - nl); if (p[i] > p[pk]) pk = i; }
  if (p[pk] <= 0) return 0; const level = 0.5 * p[pk]; let l = pk; while (l > 0 && p[l] >= level) l--; let r = pk; while (r < count - 1 && p[r] >= level) r++;
  if (p[l] >= level || p[r] >= level) return -count; // window too small
  return ((r - (level - p[r]) / (p[r - 1] - p[r])) - (l + (level - p[l]) / (p[l + 1] - p[l]))) * (9.0 / fft.length);
}
function detect(fft) {
  const noise = 11000, thr = 16000, out = []; let inSig = false, start = 0;
  for (let i = 2; i < fft.length; i++) {
    const avg = (fft[i] + fft[i - 1] + fft[i - 2]) / 3.0;
    if (!inSig) { if (avg > thr) { inSig = true; start = i; } }
    else if (avg < thr) {
      inSig = false; let end = i, acc = 0, cnt = 0;
      for (let j = Math.trunc(start + 0.3 * (end - start)); j < start + 0.8 * (end - start); j++) { acc += fft[j]; cnt++; }
      const strength = acc / cnt; let s = start, e = end;
      for (let j = start; (fft[j] - noise) < 0.75 * (strength - noise); j++) s = j;
      for (let j = end; (fft[j] - noise) < 0.75 * (strength - noise); j--) e = j;
      const mid = s + (e - s) / 2.0, freq = 10490.5 + ((mid + 1) / fft.length) * 9.0;
      const fw = measure(fft, s - 5, e + 5), oldw = (e - s) * (9.0 / fft.length);
      out.push({ freq, s, e, fw, cls: fw > 0 ? classify(fw) : -1, old: alignOld(oldw), strength });
    }
  }
  return out;
}
let n = 0; const seen = {};
ws.onmessage = (ev) => {
  const fft = new Uint16Array(ev.data); n++;
  for (const x of detect(fft)) {
    if (x.freq < 10498.3 || x.freq > 10498.7) continue;
    const k = `${x.freq.toFixed(3)} bins ${x.s}..${x.e} fwhm ${(x.fw * 1000).toFixed(1)}kHz cls ${x.cls} old ${x.old}`;
    seen[k] = (seen[k] || 0) + 1;
  }
  if (n >= frames) { console.log(`${n} frames, what the app computes near 10498.5:`); for (const [k, v] of Object.entries(seen)) console.log(`  x${v}  ${k}`); process.exit(0); }
};
ws.onerror = () => { console.log('ws error'); process.exit(1); };
setTimeout(() => process.exit(0), 30000);
