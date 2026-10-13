"""Run a list of ucp command lines, capture stdout/stderr, and report estimated token cost.

usage: python measure.py <ucp.exe> <commands.txt> <outdir> [--label L]
commands.txt: one argument string per line (shell-split); blank lines and # comments skipped.
A line starting with '@json ' is run with --json prepended as well (two runs: plain + json).
"""
import json, os, shlex, subprocess, sys, time
import tiktoken

enc = tiktoken.get_encoding("cl100k_base")
ucp, cmds_file, outdir = sys.argv[1:4]
os.makedirs(outdir, exist_ok=True)
rows = []
def run(args, tag):
    t0 = time.time()
    try:
        p = subprocess.run([ucp] + args, capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=400)
        out, err, code = p.stdout, p.stderr, p.returncode
    except subprocess.TimeoutExpired as e:
        out, err, code = (e.stdout or b"").decode("utf-8", "replace") if isinstance(e.stdout, bytes) else (e.stdout or ""), "TIMEOUT", -1
    dt = time.time() - t0
    slug = "_".join(a.strip("-").replace("/", "_") for a in args)[:60]
    base = os.path.join(outdir, f"{len(rows):03d}_{slug}")
    open(base + ".out", "w", encoding="utf-8").write(out)
    open(base + ".err", "w", encoding="utf-8").write(err)
    to, te = len(enc.encode(out)), len(enc.encode(err))
    rows.append({"n": len(rows), "cmd": " ".join(args), "exit": code, "secs": round(dt, 1), "out_tok": to, "err_tok": te, "tok": to + te, "lines": out.count("\n") + err.count("\n")})
    print(f"{len(rows)-1:3d} {to+te:6d} tok ({to} out/{te} err) {dt:5.1f}s exit {code:3d}  ucp {' '.join(args)}", flush=True)

for line in open(cmds_file, encoding="utf-8"):
    line = line.strip()
    if not line or line.startswith("#"):
        continue
    both = line.startswith("@json ")
    if both:
        line = line[6:]
    args = shlex.split(line)
    run(args, "plain")
    if both:
        run(["--json"] + args, "json")

json.dump(rows, open(os.path.join(outdir, "summary.json"), "w"), indent=1)
total = sum(r["tok"] for r in rows)
print(f"\nTOTAL {total} tokens over {len(rows)} invocations; top offenders:")
for r in sorted(rows, key=lambda r: -r["tok"])[:15]:
    print(f"  {r['tok']:6d}  ucp {r['cmd']}")
