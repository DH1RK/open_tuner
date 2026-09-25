#!/usr/bin/env python3
"""Turns a Saleae Logic 2 I2C analyzer export (CSV) into register accesses of the STV0910.

Export from Logic 2: add an I2C analyzer (SDA / SCL), then export the analyzer data as CSV.
Columns: Time [s], Packet ID, Address, Data, Read/Write, ACK/NAK - one row per byte.

Usage:
    python i2c_analyze.py capture.csv                    overview + first write of every register
    python i2c_analyze.py capture.csv --writes 17 19     all register writes between 17 s and 19 s
    python i2c_analyze.py capture.csv --reads 0xF26B     changes of one register read back (e.g. TMGLOCK1)
    python i2c_analyze.py capture.csv --reads 0xF26B --from 28 --to 42

Register names come from MediaSources/Minitiouner/stv0910_regs.cs (RSTV0910_* constants).
The STV0910 uses 16 bit register addresses; a write is [reg hi][reg lo][data ...] with auto increment, a read is
a write of the two address bytes followed by a repeated start and the read bytes.
I2C addresses (7 bit): 0x69 STV0910 (8 bit 0xD2), 0x60 STV6120 (0xC0, through the repeater), 0x67 / 0x64 LNAs (0xCE / 0xC8).
"""
import argparse
import collections
import csv
import os
import re

GAP = 0.00025        # gap between two transactions (s); a byte takes about 0.13 ms at the observed clock
READ_GAP = 0.0006    # gap between the address bytes and the first read byte (repeated start + address)
REGS = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "MediaSources", "Minitiouner", "stv0910_regs.cs")


def load_names():
    names = {}
    if os.path.exists(REGS):
        for line in open(REGS, encoding="utf-8", errors="ignore"):
            m = re.search(r"public const ushort R(STV0910_\w+) = 0x([0-9a-fA-F]+);", line)
            if m:
                names[int(m.group(2), 16)] = m.group(1).replace("STV0910_", "")
    return names


def load_transactions(path):
    rows = []
    probes = collections.Counter()
    with open(path, newline="") as f:
        reader = csv.reader(f)
        next(reader)
        for t, pid, addr, data, rw, ack in reader:
            if data == "":                      # address byte without data (a probe, usually answered with NAK)
                probes[(int(addr, 16), ack)] += 1
                continue
            rows.append((float(t), int(addr, 16), int(data, 16), rw[0]))
    load_transactions.probes = probes
    tx, cur, prev_t, prev_rw = [], None, None, None
    for t, addr, d, rw in rows:
        new = cur is None or addr != cur["addr"]
        if not new and rw == "W" and (prev_rw == "R" or t - prev_t > GAP):
            new = True
        if not new and rw == "R" and prev_rw == "R" and t - prev_t > GAP:
            new = True
        if not new and rw == "R" and prev_rw == "W" and t - prev_t > READ_GAP:
            new = True
        if new:
            cur = {"t": t, "addr": addr, "w": [], "r": []}
            tx.append(cur)
        cur["w" if rw == "W" else "r"].append(d)
        prev_t, prev_rw = t, rw
    return rows, tx


def register_events(tx, i2c_addr=0x69):
    writes, reads = [], collections.defaultdict(list)
    for x in tx:
        if x["addr"] != i2c_addr or len(x["w"]) < 2:
            continue
        base = (x["w"][0] << 8) | x["w"][1]
        if len(x["w"]) >= 3:
            for i, v in enumerate(x["w"][2:]):
                writes.append((x["t"], base + i, v))
        for i, v in enumerate(x["r"]):
            reads[base + i].append((x["t"], v))
    return writes, reads


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("csv")
    ap.add_argument("--writes", nargs=2, type=float, metavar=("FROM", "TO"))
    ap.add_argument("--reads", metavar="REG", help="register address, e.g. 0xF26B")
    ap.add_argument("--from", dest="t0", type=float, default=0.0)
    ap.add_argument("--to", dest="t1", type=float, default=1e9)
    a = ap.parse_args()

    names = load_names()
    name = lambda r: names.get(r, "?")
    rows, tx = load_transactions(a.csv)
    writes, reads = register_events(tx)

    if a.writes:
        for t, r, v in writes:
            if a.writes[0] <= t <= a.writes[1]:
                print("%9.3f s  0x%04X %-24s = 0x%02X" % (t, r, name(r), v))
        return
    if a.reads:
        reg = int(a.reads, 16)
        prev = None
        for t, v in reads.get(reg, []):
            if a.t0 <= t <= a.t1 and v != prev:
                print("%9.3f s  0x%04X %-24s = 0x%02X" % (t, reg, name(reg), v))
                prev = v
        return

    print("bytes: %d, time %.3f .. %.3f s, transactions: %d" % (len(rows), rows[0][0], rows[-1][0], len(tx)))
    print("I2C addresses (7 bit):", dict(collections.Counter(x[1] for x in rows)))
    if load_transactions.probes:
        print("address-only probes (address, ACK/NAK):", dict(load_transactions.probes))
    by = collections.defaultdict(list)
    for t, r, v in writes:
        by[r].append((t, v))
    print("register writes to 0x69: %d events, %d distinct registers" % (len(writes), len(by)))
    for r, l in sorted(by.items(), key=lambda kv: kv[1][0][0]):
        vals = collections.OrderedDict()
        for t, v in l:
            vals.setdefault("%02X" % v, []).append(t)
        desc = ", ".join("%s@%.2fs%s" % (v, ts[0], "(x%d)" % len(ts) if len(ts) > 1 else "") for v, ts in list(vals.items())[:6])
        print("0x%04X %-24s n=%-4d %s" % (r, name(r), len(l), desc))
    print("most read registers:")
    for r, l in sorted(reads.items(), key=lambda kv: -len(kv[1]))[:12]:
        print("  0x%04X %-24s %d" % (r, name(r), len(l)))


if __name__ == "__main__":
    main()
