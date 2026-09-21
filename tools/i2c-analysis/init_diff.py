#!/usr/bin/env python3
"""Compares the STV0910 register values MiniTioune writes at startup (I2C capture) with the register table of OpenTuner.

    python init_diff.py capture.csv [end_of_init_seconds]
"""
import re, sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import i2c_analyze as a

REPO = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "MediaSources", "Minitiouner")
csv_path = sys.argv[1]
t_end = float(sys.argv[2]) if len(sys.argv) > 2 else 7.15

names = a.load_names()                       # address -> name
addr = {n: r for r, n in names.items()}      # name -> address
ours = {}
in_comment = False
for line in open(os.path.join(REPO, "stv0910_regs_init.cs"), encoding="utf-8", errors="ignore"):
    stripped = line.strip()
    if in_comment:                       # inside a multi line /* ... */ block: not part of the table
        if "*/" in stripped:
            in_comment = False
        continue
    if stripped.startswith("/*") and "*/" not in stripped:
        in_comment = True
        continue
    if stripped.startswith("//"):
        continue
    m = re.match(r"\s*new STReg\(\s*stv0910_regs\.RSTV0910_(\w+),\s*([^)]*?)\s*\)", line)
    if not m or m.group(1) not in addr:
        continue
    expr = re.sub(r"/\*.*", "", m.group(2)).strip().rstrip(",")
    try:
        ours[addr[m.group(1)]] = eval(expr, {}, {})
    except Exception:
        pass

rows, tx = a.load_transactions(csv_path)
writes, reads = a.register_events(tx)
mt = {}
for t, r, v in writes:
    if t <= t_end:
        mt[r] = v

diff = [(r, ours[r], v) for r, v in sorted(mt.items()) if r in ours and ours[r] != v]
only_mt = [(r, v) for r, v in sorted(mt.items()) if r not in ours]
print("MiniTioune writes before %.2f s: %d registers, in our table: %d, different: %d, not in our table: %d" % (t_end, len(mt), sum(1 for r in mt if r in ours), len(diff), len(only_mt)))
print("\nDIFFERENT (register, ours, MiniTioune):")
for r, o, v in diff:
    print("  0x%04X %-26s ours 0x%02X   MT 0x%02X" % (r, names.get(r, "?"), o & 0xFF, v))
print("\nONLY IN MINITIOUNE (not in our table):")
for r, v in only_mt:
    print("  0x%04X %-26s MT 0x%02X" % (r, names.get(r, "?"), v))
