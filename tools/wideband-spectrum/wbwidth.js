// Linear-power width of the signal at a frequency: node wbwidth.js <label> [frames]  (saves profile_<label>.json too)
const fs = require('fs');
const label = process.argv[2] || 'x', frames = parseInt(process.argv[3] || '60'), target = 10498.50, UPD = 4096;
const ws = new WebSocket('wss://eshail.batc.org.uk/wb/fft', 'fft_m0dtslivetune'); ws.binaryType = 'arraybuffer';
let n = 0, sum = null, len = 0, clip = null;
function cross(p, level) { let k = 0; for (let i = 1; i < p.length; i++) if (p[i] > p[k]) k = i; let l = k; while (l > 0 && p[l] >= level) l--; let r = k; while (r < p.length - 1 && p[r] >= level) r++;
  return (r - (level - p[r]) / (p[r - 1] - p[r])) - (l + (level - p[l]) / (p[l + 1] - p[l])); }
ws.onmessage = (ev) => {
  const fft = new Uint16Array(ev.data); len = fft.length;
  if (!sum) { sum = new Float64Array(len); clip = new Uint16Array(len); }
  for (let i = 0; i < len; i++) { sum[i] += fft[i]; if (fft[i] >= 65535) clip[i]++; }
  if (++n >= frames) {
    const nominal = Math.round((target - 10490.5) / 9.0 * len);
    let centre = nominal; for (let i = nominal - 40; i <= nominal + 40; i++) if (sum[i] > sum[centre]) centre = i;   // follow the peak (+-40 bins)
    let noise = 0, c = 0; for (let i = centre - 80; i < centre - 30; i++) { noise += sum[i] / n; c++; } noise /= c;
    const bins = []; for (let i = centre - 25; i <= centre + 25; i++) bins.push({ bin: i, avg: Math.round(sum[i] / n), clipped: clip[i] });
    fs.writeFileSync(`profile_${label}.json`, JSON.stringify({ label, target, frames: n, len, noise, bins }));
    const kHz = 9000 / len, nl = Math.pow(10, noise / UPD / 10);
    const lin = bins.map(b => Math.max(0, Math.pow(10, b.avg / UPD / 10) - nl)), pk = Math.max(...lin);
    const peakDb = Math.max(...bins.map(b => b.avg)) / UPD;
    console.log(`${label}: centre bin ${centre} = ${(10490.5 + (centre + 1) / len * 9.0).toFixed(4)} MHz, peak ${peakDb.toFixed(2)} dB, noise ${(noise / UPD).toFixed(2)} dB, clipped bins ${bins.filter(b => b.clipped > n / 2).length}`);
    console.log(`  linear widths: FWHM ${(cross(lin, 0.5 * pk) * kHz).toFixed(1)}  35% ${(cross(lin, 0.35 * pk) * kHz).toFixed(1)}  25% ${(cross(lin, 0.25 * pk) * kHz).toFixed(1)}  15% ${(cross(lin, 0.15 * pk) * kHz).toFixed(1)}  area/peak ${(lin.reduce((a, b) => a + b, 0) / pk * kHz).toFixed(1)} kHz`);
    console.log('  bins (dB): ' + bins.filter(b => Math.abs(b.bin - centre) <= 5).map(b => (b.avg / UPD).toFixed(1)).join(' '));
    process.exit(0);
  }
};
ws.onerror = (e) => { console.log('ws error', e.message || e); process.exit(1); };
setTimeout(() => process.exit(0), 25000);
