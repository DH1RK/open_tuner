// Width estimators on the saved profiles: node wbanalyze.js
const fs = require('fs');
const files = fs.readdirSync('.').filter(f => /^profile_.*\.json$/.test(f)).sort();

function crossings(p, level) { // sub-bin crossings of `level` around the maximum, walking outward from the peak
  let k = 0; for (let i = 1; i < p.length; i++) if (p[i] > p[k]) k = i;
  let l = k; while (l > 0 && p[l] >= level) l--;
  let r = k; while (r < p.length - 1 && p[r] >= level) r++;
  const left = l + (level - p[l]) / (p[l + 1] - p[l]);
  const right = r - (level - p[r]) / (p[r - 1] - p[r]);
  return { left, right, width: right - left, peakIdx: k };
}

for (const f of files) {
  const d = JSON.parse(fs.readFileSync(f));
  const binKHz = 9000 / d.len;
  const p = d.bins.map(b => Math.max(0, b.avg - d.noise));            // noise subtracted
  const peak = Math.max(...p);
  const clipped = d.bins.filter(b => b.clipped > d.frames * 0.5).length;
  const out = { label: d.label, peak: Math.round(peak), clippedBins: clipped };
  for (const frac of [0.25, 0.5, 0.75]) out['w' + frac * 100 + '%'] = +(crossings(p, frac * peak).width * binKHz).toFixed(1);
  // absolute level: 10000 above the noise floor (independent of the peak height, but not of the power)
  out['w@+10000'] = +(crossings(p, 10000).width * binKHz).toFixed(1);
  const area = p.reduce((a, b) => a + b, 0);
  out.areaOverPeak = +(area / peak * binKHz).toFixed(1);                 // equivalent width in kHz
  const mean = p.reduce((a, v, i) => a + v * i, 0) / area;
  out.rms = +(Math.sqrt(p.reduce((a, v, i) => a + v * (i - mean) * (i - mean), 0) / area) * binKHz).toFixed(1);
  console.log(JSON.stringify(out));
}
