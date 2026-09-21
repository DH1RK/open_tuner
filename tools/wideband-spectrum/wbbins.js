// Dumps the raw FFT bins around a frequency for a few frames (averaged too), to see the real shape of a narrow signal.
const target = parseFloat(process.argv[2] || '10498.50');
const frames = parseInt(process.argv[3] || '30');
const half = parseInt(process.argv[4] || '9');
const ws = new WebSocket('wss://eshail.batc.org.uk/wb/fft', 'fft_m0dtslivetune');
ws.binaryType = 'arraybuffer';
let n = 0, sum = null, len = 0, first = null;
ws.onmessage = (ev) => {
  const fft = new Uint16Array(ev.data);
  len = fft.length;
  if (!sum) { sum = new Float64Array(len); first = Array.from(fft); }
  for (let i = 0; i < len; i++) sum[i] += fft[i];
  if (++n >= frames) {
    const centre = Math.round((target - 10490.5) / 9.0 * len);
    console.log(`${len} bins, ${(9000 / len).toFixed(2)} kHz/bin, centre bin ${centre}; averaged over ${n} frames`);
    console.log('bin   offset(kHz)   frame1   average');
    for (let i = centre - half; i <= centre + half; i++) {
      console.log(`${i}  ${((i - centre) * 9000 / len).toFixed(1).padStart(7)}   ${String(first[i]).padStart(6)}   ${Math.round(sum[i] / n)}`);
    }
    let noise = 0; for (let i = centre - 60; i < centre - 30; i++) noise += sum[i] / n; console.log('noise floor near: ' + Math.round(noise / 30));
    process.exit(0);
  }
};
ws.onerror = (e) => { console.log('ws error', e.message || e); process.exit(1); };
setTimeout(() => process.exit(0), 20000);
