// Live check of the BATC wideband spectrum around a frequency: measured signal width and the label each
// implementation would show (OpenTuner signal.cs, BATC page index.js).
const target = parseFloat(process.argv[2] || '10498.50');
const frames = parseInt(process.argv[3] || '6');
const ws = new WebSocket('wss://eshail.batc.org.uk/wb/fft', 'fft_m0dtslivetune');
ws.binaryType = 'arraybuffer';
let n = 0;

function alignOpenTuner(w) {
  if (w < 0.022) return 0; if (w < 0.065) return 0.035; if (w < 0.086) return 0.066; if (w < 0.195) return 0.125;
  if (w < 0.277) return 0.250; if (w < 0.388) return 0.333; if (w < 0.700) return 0.500; if (w < 1.2) return 1.0;
  if (w < 1.6) return 1.5; if (w < 2.2) return 2.0; return Math.round(w * 5) / 5.0;
}
function alignBatc(w) {
  if (w < 0.022) return 0; if (w < 0.060) return 0.035; if (w < 0.086) return 0.066; if (w < 0.185) return 0.125;
  if (w < 0.277) return 0.250; if (w < 0.388) return 0.333; if (w < 0.700) return 0.500; if (w < 1.2) return 1.0;
  if (w < 1.6) return 1.5; if (w < 2.2) return 2.0; return Math.round(w * 5) / 5.0;
}

function detect(fft) {
  const noise = 11000, thr = 16000, res = [];
  let inSig = false, start = 0;
  for (let i = 2; i < fft.length; i++) {
    const avg = (fft[i] + fft[i - 1] + fft[i - 2]) / 3.0;
    if (!inSig) { if (avg > thr) { inSig = true; start = i; } }
    else if (avg < thr) {
      inSig = false; let end = i, acc = 0, cnt = 0;
      for (let j = (start + 0.3 * (end - start)) | 0; j < start + 0.8 * (end - start); j++) { acc += fft[j]; cnt++; }
      const strength = acc / cnt;
      let s = start, e = end;
      for (let j = start; (fft[j] - noise) < 0.75 * (strength - noise); j++) s = j;
      for (let j = end; (fft[j] - noise) < 0.75 * (strength - noise); j--) e = j;
      const mid = s + (e - s) / 2.0;
      res.push({ s, e, mid, strength, widthMHz: (e - s) * (9.0 / fft.length), freq: 10490.5 + ((mid + 1) / fft.length) * 9.0 });
    }
  }
  return res;
}

ws.onmessage = (ev) => {
  const fft = new Uint16Array(ev.data);
  n++;
  const sigs = detect(fft).filter(x => Math.abs(x.freq - target) < 0.25);
  console.log(`frame ${n}: ${fft.length} bins (${(9000 / fft.length).toFixed(2)} kHz/bin), ${sigs.length} signal(s) within 250 kHz of ${target}`);
  for (const x of sigs) {
    console.log(`   f=${x.freq.toFixed(4)} MHz  bins ${x.s}..${x.e}  width=${(x.widthMHz * 1000).toFixed(1)} kHz  strength=${Math.round(x.strength)}` +
                `  -> OpenTuner ${Math.round(alignOpenTuner(x.widthMHz) * 1000)}KS, BATC page ${Math.round(alignBatc(x.widthMHz) * 1000)}KS`);
  }
  if (n >= frames) { ws.close(); process.exit(0); }
};
ws.onerror = (e) => { console.log('ws error', e.message || e); process.exit(1); };
setTimeout(() => { console.log('timeout, frames received: ' + n); process.exit(0); }, 20000);
