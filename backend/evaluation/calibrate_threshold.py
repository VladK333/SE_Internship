"""Odredjuje prag slicnosti za odbijanje pitanja (guardrail 2).

Za svako pitanje iz calibration_questions.json racuna najbolji skor pretrage (bez LLM-a)
i trazi prag koji najbolje razdvaja pitanja sa odgovorom od pitanja bez odgovora.
Pokretanje (iz SE_praksa):  python backend/evaluation/calibrate_threshold.py
Rezultat:                   backend/evaluation/calibration_results.json
"""
import json
import pathlib
import sys

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[1]))
from app.retriever import Retriever  # noqa: E402

HERE = pathlib.Path(__file__).resolve().parent


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    data = json.loads((HERE / "calibration_questions.json").read_text(encoding="utf-8"))
    retriever = Retriever()
    rows = []
    for label in ("answerable", "unanswerable"):
        for q in data[label]:
            best = retriever.search(q, k=1)[0]
            rows.append({"question": q, "label": label, "score": round(best["score"], 4),
                         "top_section": best["heading_path"]})

    ans = sorted(r["score"] for r in rows if r["label"] == "answerable")
    una = sorted(r["score"] for r in rows if r["label"] == "unanswerable")
    print(f"Sa odgovorom:  min {ans[0]:.3f}  max {ans[-1]:.3f}  ({len(ans)} pitanja)")
    print(f"Bez odgovora:  min {una[0]:.3f}  max {una[-1]:.3f}  ({len(una)} pitanja)\n")

    # prag t: odbija se ako je skor < t
    candidates = []
    for i in range(50, 86):
        t = i / 100
        false_refusals = sum(s < t for s in ans)      # dobro pitanje odbijeno
        false_accepts = sum(s >= t for s in una)      # pitanje van dokumentacije pusteno do LLM-a
        balanced = ((len(ans) - false_refusals) / len(ans) + (len(una) - false_accepts) / len(una)) / 2
        candidates.append({"threshold": t, "false_refusals": false_refusals,
                           "false_accepts": false_accepts, "balanced_accuracy": round(balanced, 4)})
    print("prag  pogresno_odbijeno  pogresno_pusteno  balansirana_tacnost")
    for c in candidates:
        if 0.60 <= c["threshold"] <= 0.75:
            print(f"{c['threshold']:.2f}  {c['false_refusals']:>17}  {c['false_accepts']:>16}  {c['balanced_accuracy']:.3f}")

    # od svih pragova sa najboljom balansiranom tacnoscu bira se srednji, najdalji od obe grupe
    top = max(c["balanced_accuracy"] for c in candidates)
    tied = [c for c in candidates if c["balanced_accuracy"] == top]
    best = tied[(len(tied) - 1) // 2]
    print(f"\nPreporuceni prag: {best['threshold']:.2f}")

    print("\nNajblize granici:")
    for r in sorted(rows, key=lambda r: abs(r["score"] - best["threshold"]))[:8]:
        print(f"  {r['score']:.3f}  {r['label']:<12}  {r['question']}")

    (HERE / "calibration_results.json").write_text(json.dumps({
        "recommended_threshold": best["threshold"], "best": best,
        "candidates": candidates, "questions": rows}, ensure_ascii=False, indent=2), encoding="utf-8")


if __name__ == "__main__":
    main()
