// Survey of all narrow signals in the BATC wideband spectrum: bins, integer width (what OpenTuner uses) and a
// sub-bin width from linear interpolation of the 75 % level crossings, averaged over several frames.
const frames = parseInt(process.argv[2] || '20');
const maxWidthKHz = parseFloat(process.argv[3] || '120');
const ws = new WebSocket('wss://eshail.batc.org.uk/wb/fft', 'fft_m0dtslivetune');
ws.binaryType = 'arraybuffer';
const acc = new Map(); // key = rounded frequency in 10 kHz steps
let n = 0, binKHz = 0;

function detect(fft) {
  const noise = 11000, thr = 16000, res = [];
  let inSig = false, start = 0;
  for (let i = 2; i < fft.length; i++) {
    const avg = (fft[i] + fft[i - 1] + fft[i - 2]) / 3.0;
    if (!inSig) { if (avg > thr) { inSig = true; start = i; } }
    else if (avg < thr) {
      inSig = false; let end = i, a = 0, cnt = 0;
      for (let j = (start + 0.3 * (end - start)) | 0; j < start + 0.8 * (end - start); j++) { a += fft[j]; cnt++; }
      const strength = a / cnt;
      const level = noise + 0.75 * (strength - noise);
      let s = start, e = end;
      for (let j = start; fft[j] < level; j++) s = j;
      for (let j = end; fft[j] < level; j--) e = j;
      // sub-bin edges: where the flanks cross the 75 % level
      const left = s + (level - fft[s]) / Math.max(1, (fft[s + 1] - fft[s]));
      const right = e - (level - fft[e]) / Math.max(1, (fft[e - 1] - fft[e]));
      const mid = s + (e - s) / 2.0;
      res.push({ s, e, strength, freq: 10490.5 + ((mid + 1) / fft.length) * 9.0, binWidth: (e - s) * (9000 / fft.length), fracWidth: (right - left) * (9000 / fft.length) });
    }
  }
  return res;
}

ws.onmessage = (ev) => {
  const fft = new Uint16Array(ev.data);
  binKHz = 9000 / fft.length; n++;
  for (const x of detect(fft)) {
    if (x.binWidth > maxWidthKHz) continue;
    const key = Math.round(x.freq * 100);
    const r = acc.get(key) || { freq: 0, cnt: 0, bins: {}, frac: 0, strength: 0 };
    r.freq += x.freq; r.cnt++; r.frac += x.fracWidth; r.strength += x.strength;
    r.bins[x.binWidth.toFixed(1)] = (r.bins[x.binWidth.toFixed(1)] || 0) + 1;
    acc.set(key, r);
  }
  if (n >= frames) done();
};
function done() {
  console.log(`${n} frames, ${binKHz.toFixed(2)} kHz/bin; signals narrower than ${maxWidthKHz} kHz (seen in >= 40% of the frames):`);
  const rows = [...acc.values()].filter(r => r.cnt >= n * 0.4).sort((a, b) => a.freq / a.cnt - b.freq / b.cnt);
  for (const r of rows) {
    const bins = Object.entries(r.bins).map(([w, c]) => `${w}kHz x${c}`).join(', ');
    console.log(`  ${(r.freq / r.cnt).toFixed(4)} MHz  frames ${r.cnt}  sub-bin width ${(r.frac / r.cnt).toFixed(1)} kHz  strength ${Math.round(r.strength / r.cnt)}  bin widths: ${bins}`);
  }
  if (!rows.length) console.log('  none');
  process.exit(0);
}
ws.onerror = (e) => { console.log('ws error', e.message || e); process.exit(1); };
setTimeout(done, 30000);
