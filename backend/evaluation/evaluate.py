"""Evaluacija pretrage (3. nedelja, deo 1): bez LLM-a, za par sekundi.

Metrike iz specifikacije:
  - koliko cesto je ocekivani izvor medju top 3 pronadjene sekcije (pitanja sa odgovorom);
  - koliko pitanja bez odgovora je ispravno odbijeno (guardrail 2, prag iz app.main).
Dodatno: tacan izvor na 1. mestu i dobra pitanja koja je prag pogresno odbio.

Pokretanje (iz SE_praksa):  python backend/evaluation/evaluate.py [hybrid|vector]
Rezultat:                   backend/evaluation/results/retrieval.json i .md (hybrid),
                            odnosno retrieval_vector.json i .md (samo vektorska pretraga)
"""
import json
import pathlib
import sys
from datetime import date

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[1]))
from app.main import REFUSAL_THRESHOLD, TOP_K  # noqa: E402
from app.retriever import RETRIEVAL_MODE, Retriever  # noqa: E402

HERE = pathlib.Path(__file__).resolve().parent
OUT_DIR = HERE / "results"


def rank_of_expected(results, expected):
    """1-based mesto prve pronadjene sekcije koja odgovara ocekivanom izvoru, ili None."""
    for i, r in enumerate(results, start=1):
        if any(e in r["heading_path"] for e in expected):
            return i
    return None


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    data = json.loads((HERE / "eval_questions.json").read_text(encoding="utf-8"))
    mode = sys.argv[1] if len(sys.argv) > 1 else RETRIEVAL_MODE
    retriever = Retriever(mode=mode)
    name = "retrieval" if mode == "hybrid" else f"retrieval_{mode}"
    print(f"Nacin pretrage: {mode}")

    answerable = []
    for q in data["answerable"]:
        results = retriever.search(q["question"], k=TOP_K)
        rank = rank_of_expected(results, q["expected_sources"])
        answerable.append({
            **q,
            "rank": rank,
            "top_score": round(results[0]["query_top_similarity"], 4),
            "refused": results[0]["query_top_similarity"] < REFUSAL_THRESHOLD,
            "retrieved": [{"heading_path": r["heading_path"], "score": round(r["score"], 4)} for r in results],
        })

    unanswerable = []
    for q in data["unanswerable"]:
        results = retriever.search(q["question"], k=1)
        unanswerable.append({**q, "top_score": round(results[0]["query_top_similarity"], 4),
                             "top_section": results[0]["heading_path"],
                             "refused": results[0]["query_top_similarity"] < REFUSAL_THRESHOLD})

    n_a, n_u = len(answerable), len(unanswerable)
    hit3 = sum(r["rank"] is not None for r in answerable)
    hit1 = sum(r["rank"] == 1 for r in answerable)
    false_refusals = sum(r["refused"] for r in answerable)
    refused_ok = sum(r["refused"] for r in unanswerable)
    summary = {
        "date": date.today().isoformat(), "mode": mode,
        "threshold": REFUSAL_THRESHOLD, "top_k": TOP_K,
        "answerable": n_a, "unanswerable": n_u,
        "source_in_top3": hit3, "source_in_top3_pct": round(100 * hit3 / n_a, 1),
        "source_at_rank1": hit1, "source_at_rank1_pct": round(100 * hit1 / n_a, 1),
        "false_refusals": false_refusals,
        "correctly_refused": refused_ok, "correctly_refused_pct": round(100 * refused_ok / n_u, 1),
    }

    print(f"Tacan izvor u top 3:        {hit3}/{n_a} ({summary['source_in_top3_pct']} %)")
    print(f"Tacan izvor na 1. mestu:    {hit1}/{n_a} ({summary['source_at_rank1_pct']} %)")
    print(f"Dobra pitanja odbijena:     {false_refusals}/{n_a}")
    print(f"Ispravno odbijena (bez odg): {refused_ok}/{n_u} ({summary['correctly_refused_pct']} %)")
    misses = [r for r in answerable if r["rank"] is None]
    if misses:
        print("\nPromasaji (ocekivani izvor nije u top 3):")
        for r in misses:
            print(f"  {r['id']} {r['question']}\n      ocekivano: {r['expected_sources']}\n      dobijeno:  {[x['heading_path'] for x in r['retrieved']]}")
    leaks = [r for r in unanswerable if not r["refused"]]
    if leaks:
        print("\nPitanja bez odgovora koja su prosla prag (odlucuje LLM):")
        for r in leaks:
            print(f"  {r['id']} {r['top_score']:.3f} {r['question']}  ->  {r['top_section']}")

    OUT_DIR.mkdir(exist_ok=True)
    (OUT_DIR / f"{name}.json").write_text(json.dumps(
        {"summary": summary, "answerable": answerable, "unanswerable": unanswerable},
        ensure_ascii=False, indent=2), encoding="utf-8")
    (OUT_DIR / f"{name}.md").write_text(to_markdown(summary, answerable, unanswerable), encoding="utf-8")


def to_markdown(s, answerable, unanswerable):
    lines = [
        f"# Evaluacija pretrage: {s['mode']} ({s['date']})", "",
        f"Prag odbijanja {s['threshold']}, top {s['top_k']} sekcije.", "",
        "| Metrika | Rezultat |", "| --- | --- |",
        f"| Tačan izvor u top 3 | {s['source_in_top3']}/{s['answerable']} ({s['source_in_top3_pct']} %) |",
        f"| Tačan izvor na 1. mestu | {s['source_at_rank1']}/{s['answerable']} ({s['source_at_rank1_pct']} %) |",
        f"| Pitanja sa odgovorom pogrešno odbijena | {s['false_refusals']}/{s['answerable']} |",
        f"| Pitanja bez odgovora ispravno odbijena | {s['correctly_refused']}/{s['unanswerable']} ({s['correctly_refused_pct']} %) |",
        "", "## Pitanja sa odgovorom", "",
        "| ID | Pitanje | Mesto izvora | Skor |", "| --- | --- | --- | --- |",
    ]
    for r in answerable:
        lines.append(f"| {r['id']} | {r['question']} | {r['rank'] or 'nije u top 3'} | {r['top_score']:.3f} |")
    lines += ["", "## Pitanja bez odgovora", "", "| ID | Pitanje | Skor | Odbijeno |", "| --- | --- | --- | --- |"]
    for r in unanswerable:
        lines.append(f"| {r['id']} | {r['question']} | {r['top_score']:.3f} | {'da' if r['refused'] else 'ne (LLM)'} |")
    return "\n".join(lines) + "\n"


if __name__ == "__main__":
    main()
