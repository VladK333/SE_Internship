"""Evaluacija odgovora: svako pitanje prolazi kroz ceo /chat (pretraga, prag, LLM).

Odgovori se cuvaju posle svakog pitanja u results/answers.jsonl, pa se prekinuto pokretanje
nastavlja tamo gde je stalo. Ocene 1-3 se upisuju posebno, u results/answer_scores.json.

Pokretanje (Ollama mora da radi):  python backend/evaluation/evaluate_answers.py [model]
Kiristi podrazumevani model (llama3) i pise results/answers.jsonl; sa drugim modelom
(gemma3:1b) pise results/answers_<model>.jsonl
"""
import json
import os
import pathlib
import sys
import time

MODEL = sys.argv[1] if len(sys.argv) > 1 else None
if MODEL:
    os.environ["OLLAMA_MODEL"] = MODEL   # mora pre importa app, koji cita promenljivu pri ucitavanju

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[1]))
from fastapi.testclient import TestClient  # noqa: E402

from app.main import app  # noqa: E402

HERE = pathlib.Path(__file__).resolve().parent
OUT = HERE / "results" / (f"answers_{MODEL.replace(':', '-')}.jsonl" if MODEL else "answers.jsonl")


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    data = json.loads((HERE / "eval_questions.json").read_text(encoding="utf-8"))
    questions = [{**q, "kind": "answerable"} for q in data["answerable"]] + \
                [{**q, "kind": "unanswerable"} for q in data["unanswerable"]]

    OUT.parent.mkdir(exist_ok=True)
    done = set()
    if OUT.exists():
        done = {json.loads(line)["id"] for line in OUT.read_text(encoding="utf-8").splitlines() if line.strip()}

    with TestClient(app) as client, OUT.open("a", encoding="utf-8") as out:
        for i, q in enumerate(questions, start=1):
            if q["id"] in done:
                continue
            start = time.time()
            r = client.post("/chat", json={"question": q["question"]})
            seconds = round(time.time() - start, 1)
            body = r.json()
            row = {
                "id": q["id"], "kind": q["kind"], "question": q["question"],
                "expected_sources": q.get("expected_sources", []),
                "status": r.status_code, "seconds": seconds,
                "refused": body.get("refused"), "answer": body.get("answer") or body.get("detail"),
                "sources": [s["heading_path"] for s in body.get("sources", [])],
            }
            out.write(json.dumps(row, ensure_ascii=False) + "\n")
            out.flush()
            print(f"[{i}/{len(questions)}] {q['id']} {seconds:6.1f} s  refused={row['refused']}  {q['question'][:70]}", flush=True)


if __name__ == "__main__":
    main()
