// Can the clipped top (65535 = 16 dB) be reconstructed? Fit a parabola (dB domain) to the flank bins next to the clipped bins.
const fs = require('fs'); const UPD = 4096;
function cross(p, level) { let k = 0; for (let i = 1; i < p.length; i++) if (p[i] > p[k]) k = i; let l = k; while (l > 0 && p[l] >= level) l--; let r = k; while (r < p.length - 1 && p[r] >= level) r++;
  return (r - (level - p[r]) / (p[r - 1] - p[r])) - (l + (level - p[l]) / (p[l + 1] - p[l])); }
function fit(xs, ys) { // least squares parabola y = a x^2 + b x + c
  const n = xs.length; let s = [0, 0, 0, 0, 0], t = [0, 0, 0];
  for (let i = 0; i < n; i++) { const x = xs[i]; s[0] += 1; s[1] += x; s[2] += x * x; s[3] += x ** 3; s[4] += x ** 4; t[0] += ys[i]; t[1] += ys[i] * x; t[2] += ys[i] * x * x; }
  const M = [[s[4], s[3], s[2], t[2]], [s[3], s[2], s[1], t[1]], [s[2], s[1], s[0], t[0]]];
  for (let i = 0; i < 3; i++) { let p = i; for (let j = i + 1; j < 3; j++) if (Math.abs(M[j][i]) > Math.abs(M[p][i])) p = j; [M[i], M[p]] = [M[p], M[i]];
    for (let j = i + 1; j < 3; j++) { const f = M[j][i] / M[i][i]; for (let k = i; k < 4; k++) M[j][k] -= f * M[i][k]; } }
  const c = M[2][3] / M[2][2], b = (M[1][3] - M[1][2] * c) / M[1][1], a = (M[0][3] - M[0][2] * c - M[0][1] * b) / M[0][0]; return [a, b, c]; }
for (const name of process.argv.slice(2)) {
  const d = JSON.parse(fs.readFileSync(`profile_${name}.json`)); const kHz = 9000 / d.len;
  const db = d.bins.map(b => b.avg / UPD); const clipped = d.bins.map(b => b.avg >= 65000);
  const nl = Math.pow(10, d.noise / UPD / 10);
  const lin = (v) => Math.max(0, Math.pow(10, v / 10) - nl);
  const plain = db.map(lin); let pk = Math.max(...plain);
  const raw = cross(plain, 0.5 * pk) * kHz;
  const ci = clipped.map((c, i) => c ? i : -1).filter(i => i >= 0);
  if (!ci.length) { console.log(`${name}: not clipped, FWHM ${raw.toFixed(1)}`); continue; }
  const lo = Math.min(...ci), hi = Math.max(...ci); const xs = [], ys = [];
  for (const i of [lo - 2, lo - 1, hi + 1, hi + 2]) { xs.push(i); ys.push(db[i]); }
  const [a, b, c] = fit(xs, ys); const rec = db.slice();
  for (let i = lo; i <= hi; i++) rec[i] = Math.max(db[i], a * i * i + b * i + c);
  const p2 = rec.map(lin); const pk2 = Math.max(...p2);
  console.log(`${name}: clipped bins ${ci.length}, FWHM raw ${raw.toFixed(1)} -> reconstructed ${(cross(p2, 0.5 * pk2) * kHz).toFixed(1)}  (recovered peak ${(Math.max(...rec)).toFixed(2)} dB)`);
}
