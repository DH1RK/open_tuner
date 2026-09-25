// Fit a smoothed-rectangle model to the saved profiles and pick the symbol rate with the smallest residual.
// Clipped bins (>= 65000) only count as "model must reach at least the clip level".
const fs = require('fs'); const UPD = 4096, CLIP_DB = 65535 / UPD;
const truth = { '20_a': 20, '25_d': 25, '33_mid': 33, '33_bake': 33, '66_a': 66, '25_app': 25, '25_a': 25, '25_b': 25, '25_c': 25, '33_strong': 33 };
const cands = [20, 25, 33, 66];
const binKHz = 9000 / 922;
function Phi(x) { // normal CDF
  const t = 1 / (1 + 0.2316419 * Math.abs(x)), d = 0.3989423 * Math.exp(-x * x / 2);
  const p = d * t * (0.3193815 + t * (-0.3565638 + t * (1.781478 + t * (-1.821256 + t * 1.330274))));
  return x > 0 ? 1 - p : p; }
function loadProfile(name) {
  const d = JSON.parse(fs.readFileSync(`profile_${name}.json`));
  let k = 0; d.bins.forEach((b, i) => { if (b.avg > d.bins[k].avg) k = i; });
  const from = Math.max(0, k - 7), to = Math.min(d.bins.length - 1, k + 7);
  const bins = d.bins.slice(from, to + 1).map((b, i) => ({ x: (from + i - k), db: b.avg / UPD, clipped: b.avg >= 65000 }));
  return { noiseDb: d.noise / UPD, bins };
}
function residual(p, sr, sigma) {
  const nl = Math.pow(10, p.noiseDb / 10); let best = 1e18;
  for (let c = -1.5; c <= 1.5; c += 0.05) {
    for (let a = 6; a <= 30; a += 0.25) {
      const A = Math.pow(10, a / 10); let s = 0, n = 0;
      for (const b of p.bins) {
        const f = (b.x - c) * binKHz;
        const m = A * (Phi((f + sr / 2) / sigma) - Phi((f - sr / 2) / sigma));
        const mdb = 10 * Math.log10(m + nl);
        if (b.db < p.noiseDb + 1.0 && !b.clipped) { const e = mdb - b.db; if (e > 0) { s += e * e; n++; } continue; } // ignore noise bins unless the model predicts signal there
        if (b.clipped) { const e = Math.max(0, CLIP_DB - mdb); s += e * e; n++; continue; }
        const e = mdb - b.db; s += e * e; n++;
      }
      if (s < best) best = s;
    }
  }
  return best;
}
const profiles = {}; for (const n of Object.keys(truth)) profiles[n] = loadProfile(n);
for (const sigma of [3, 4, 4.5, 5, 6]) {
  let ok = 0; const rows = [];
  for (const [n, sr] of Object.entries(truth)) {
    const res = cands.map(c => ({ c, r: residual(profiles[n], c, sigma) })).sort((a, b) => a.r - b.r);
    const good = res[0].c === sr; if (good) ok++;
    rows.push(`${n}(${sr}):${res[0].c}${good ? '' : '!'} [${res.map(x => x.c + '=' + x.r.toFixed(1)).join(' ')}]`);
  }
  console.log(`sigma ${sigma} kHz: ${ok}/${Object.keys(truth).length} correct`);
  if (process.argv[2] === 'v' || ok >= 9) rows.forEach(r => console.log('   ' + r));
}
