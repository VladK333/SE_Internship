"""Spaja odgovore (results/answers.jsonl) i ocene (results/answer_scores.json) u zajednickii pregled

Pokretanje:  python backend/evaluation/summarize_answers.py [model]
Rezultat:                   backend/evaluation/results/answers.md
Sa modelom (npr. gemma3:1b) cita answers_<model>.jsonl i answer_scores_<model>.json i pise answers_<model>.md.
"""
import json
import pathlib
import statistics
import sys

HERE = pathlib.Path(__file__).resolve().parent / "results"
SUFFIX = "_" + sys.argv[1].replace(":", "-") if len(sys.argv) > 1 else ""


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    rows = [json.loads(line) for line in (HERE / f"answers{SUFFIX}.jsonl").read_text(encoding="utf-8").splitlines() if line.strip()]
    scores_file = json.loads((HERE / f"answer_scores{SUFFIX}.json").read_text(encoding="utf-8"))
    scores = scores_file["scores"]

    answerable = [r for r in rows if r["kind"] == "answerable"]
    unanswerable = [r for r in rows if r["kind"] == "unanswerable"]
    scored = [scores[r["id"]]["score"] for r in answerable if r["id"] in scores]
    llm_times = [r["seconds"] for r in answerable if not r["refused"]]
    refused_ok = sum(bool(r["refused"]) for r in unanswerable)
    false_refusals = sum(bool(r["refused"]) for r in answerable)

    summary = [
        ("Prosečna ocena odgovora (1–3)", f"{statistics.mean(scored):.2f} ({len(scored)} pitanja)"),
        ("Ocena 3 / 2 / 1", f"{scored.count(3)} / {scored.count(2)} / {scored.count(1)}"),
        ("Pitanja bez odgovora ispravno odbijena (prag + model)", f"{refused_ok}/{len(unanswerable)}"),
        ("Pitanja sa odgovorom koja je model pogrešno odbio", f"{false_refusals}/{len(answerable)}"),
        ("Vreme odgovora sa LLM-om (medijana / najduže)", f"{statistics.median(llm_times):.0f} s / {max(llm_times):.0f} s"),
    ]
    for k, v in summary:
        print(f"{k}: {v}")

    title = f"# Evaluacija odgovora ({sys.argv[1]})" if SUFFIX else "# Evaluacija odgovora"
    lines = [title, "", f"Status ocena: **{scores_file['status']}**", "",
             "| Metrika | Rezultat |", "| --- | --- |"]
    lines += [f"| {k} | {v} |" for k, v in summary]
    lines += ["", "## Pitanja sa odgovorom", "", "| ID | Pitanje | Ocena | Obrazloženje | Vreme |", "| --- | --- | --- | --- | --- |"]
    for r in answerable:
        s = scores.get(r["id"], {})
        lines.append(f"| {r['id']} | {r['question']} | {s.get('score', '–')} | {s.get('note', '')} | {r['seconds']:.0f} s |")
    lines += ["", "## Pitanja bez odgovora", "", "| ID | Pitanje | Odbijeno | Vreme |", "| --- | --- | --- | --- |"]
    for r in unanswerable:
        who = "prag" if r["seconds"] < 5 else "model"
        lines.append(f"| {r['id']} | {r['question']} | {'da (' + who + ')' if r['refused'] else 'NE'} | {r['seconds']:.1f} s |")
    (HERE / f"answers{SUFFIX}.md").write_text("\n".join(lines) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
