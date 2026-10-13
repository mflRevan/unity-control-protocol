"""Token tally of a shim log dir: per call and total, plus help-probe and error counts."""
import os, sys, tiktoken
e = tiktoken.get_encoding("cl100k_base")
d = sys.argv[1]
rows = []
for line in open(os.path.join(d, "calls.tsv"), encoding="utf-8"):
    n, code, ms, args = line.rstrip("\n").split("\t", 3)
    out = open(os.path.join(d, n + ".out"), encoding="utf-8", errors="replace").read()
    err = open(os.path.join(d, n + ".err"), encoding="utf-8", errors="replace").read()
    # "seen" approximates what a tool harness that truncates output at 30k characters shows the
    # model; the raw count is what the CLI actually produced.
    seen = len(e.encode((out + err)[:30000]))
    rows.append((n, int(code), float(ms)/1000, args, len(e.encode(out)), len(e.encode(err)), seen))
tot = sum(r[4] + r[5] for r in rows)
seen_tot = sum(r[6] for r in rows)
helps = [r for r in rows if "--help" in r[3] or r[3].endswith(" -h") or r[3] == "-h"]
errs = [r for r in rows if r[1] != 0]
print(f"calls={len(rows)} total_tokens={tot} seen_tokens_30k_cap={seen_tot} help_probes={len(helps)} help_tokens={sum(r[4]+r[5] for r in helps)} failed_calls={len(errs)} wall={sum(r[2] for r in rows):.0f}s")
if "-v" in sys.argv:
    for r in rows:
        print(f"{r[0]} {r[4]+r[5]:6d} tok exit {r[1]:3d} {r[2]:5.1f}s  ucp {r[3]}")
