"""Packs AutoEq's ParametricEQ.txt results into one JSON file for Cypress Sound.

Usage: python build_autoeq.py <AutoEq checkout> <out.json>
AutoEq (https://github.com/jaakkopasanen/AutoEq) is MIT licensed.
"""
import json
import pathlib
import sys

# When the same model was measured by several people, list the best-known measurements first.
PREFERRED = ["oratory1990", "crinacle", "Rtings", "Innerfidelity", "Headphone.com Legacy"]


def main(root: str, out: str) -> None:
    results = pathlib.Path(root) / "results"
    rows = {}
    for f in results.rglob("* ParametricEQ.txt"):
        rel = f.relative_to(results).parts
        name = f.parent.name
        source = rel[0]
        text = f.read_text(encoding="utf-8", errors="replace").strip()
        if "Filter" not in text:
            continue
        rows.setdefault((name, source), text)
    rank = {s.lower(): i for i, s in enumerate(PREFERRED)}
    items = sorted(rows.items(), key=lambda kv: (kv[0][0].lower(), rank.get(kv[0][1].lower(), 99), kv[0][1]))
    data = [{"name": n, "source": s, "eq": t} for (n, s), t in items]
    pathlib.Path(out).parent.mkdir(parents=True, exist_ok=True)
    pathlib.Path(out).write_text(json.dumps(data, ensure_ascii=False, separators=(",", ":")), encoding="utf-8")
    print(f"{len(data)} profiles, {len({n for n, _ in rows})} models -> {out}")
    if len(data) < 1000:
        sys.exit("too few AutoEq profiles found; the AutoEq folder layout may have changed")


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
