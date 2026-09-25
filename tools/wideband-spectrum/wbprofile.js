// Saves the averaged FFT profile around a frequency for one symbol rate: node wbprofile.js <MHz> <frames> <label>
const target = parseFloat(process.argv[2] || '10498.50');
const frames = parseInt(process.argv[3] || '60');
const label = process.argv[4] || 'x';
const fs = require('fs');
const ws = new WebSocket('wss://eshail.batc.org.uk/wb/fft', 'fft_m0dtslivetune');
ws.binaryType = 'arraybuffer';
let n = 0, sum = null, len = 0, clip = null;
ws.onmessage = (ev) => {
  const fft = new Uint16Array(ev.data);
  len = fft.length;
  if (!sum) { sum = new Float64Array(len); clip = new Uint16Array(len); }
  for (let i = 0; i < len; i++) { sum[i] += fft[i]; if (fft[i] >= 65535) clip[i]++; }
  if (++n >= frames) {
    const centre = Math.round((target - 10490.5) / 9.0 * len);
    const bins = [];
    for (let i = centre - 15; i <= centre + 15; i++) bins.push({ bin: i, avg: Math.round(sum[i] / n), clipped: clip[i] });
    let noise = 0, c = 0; for (let i = centre - 80; i < centre - 30; i++) { noise += sum[i] / n; c++; }
    fs.writeFileSync(`profile_${label}.json`, JSON.stringify({ label, target, frames: n, len, noise: noise / c, bins }));
    console.log(`saved profile_${label}.json (${n} frames, noise ${Math.round(noise / c)})`);
    process.exit(0);
  }
};
ws.onerror = (e) => { console.log('ws error', e.message || e); process.exit(1); };
setTimeout(() => process.exit(0), 25000);
