"""Pretraga dokumentacije: pitanje -> top k chunkova.

Hibridna pretraga (podrazumevano): vektorska pretraga (FAISS, bge-small) i BM25 (tacne reci)
se spajaju metodom Reciprocal Rank Fusion. BM25 pomaze kod tacnih naziva (npr. voltageInitMode)
i reci koje razlikuju slicne sekcije (import / export).

Svaki rezultat ima:
  score                 kosinusna slicnost pitanja i chunka (za prikaz izvora)
  query_top_similarity  najveca kosinusna slicnost za pitanje u celom indeksu; na njoj je
                        kalibrisan prag odbijanja (guardrail 2), pa ne zavisi od nacina spajanja

Podesavanje: RETRIEVAL_MODE = hybrid (podrazumevano) ili vector.
Test iz komandne linije:
  python backend/app/retriever.py "What voltage initialization modes does the load flow support?"
"""
import json
import os
import pathlib
import re
import sys

import faiss
import numpy as np
from rank_bm25 import BM25Okapi
from sentence_transformers import SentenceTransformer

ROOT = pathlib.Path(__file__).resolve().parents[2]
INDEX_DIR = ROOT / "data" / "index"
# bge modeli preporucuju ovaj prefiks za pitanja (ne i za pasuse)
QUERY_PREFIX = "Represent this sentence for searching relevant passages: "
RETRIEVAL_MODE = os.getenv("RETRIEVAL_MODE", "hybrid")

CANDIDATES = 30   # koliko rezultata svake pretrage ulazi u spajanje
RRF_K = 60        # standardna konstanta za Reciprocal Rank Fusion

STOPWORDS = set("""a an and are as at be by can do does for from how i in is it its of on or that the
this to what when which why with you your my me we will would should could there their into than then
use used using""".split())
WORD = re.compile(r"[A-Za-z][A-Za-z0-9]*(?:[._-][A-Za-z0-9]+)*|\d+")
CAMEL = re.compile(r"[A-Z]?[a-z]+|[A-Z]+(?![a-z])|\d+")


def tokenize(text):
    """Reci za BM25: mala slova, bez cestih reci; nazivi se cuvaju celi i deljeni na delove
    (voltageInitMode -> voltageinitmode, voltage, init, mode; iidm.import.cgmes -> + iidm, import, cgmes)."""
    tokens = []
    for word in WORD.findall(text):
        parts = [p.lower() for piece in re.split(r"[._-]", word) for p in CAMEL.findall(piece)]
        for t in {word.lower(), *parts}:
            if len(t) > 3 and t.endswith("s") and not t.endswith("ss"):
                t = t[:-1]   # jednostavno svodjenje mnozine: modes -> mode
            if t not in STOPWORDS and len(t) > 1:
                tokens.append(t)
    return tokens


class Retriever:
    def __init__(self, index_dir=INDEX_DIR, mode=RETRIEVAL_MODE):
        meta = json.loads((index_dir / "meta.json").read_text(encoding="utf-8"))
        self.model_name = meta["model"]
        self.mode = mode
        self.model = SentenceTransformer(self.model_name)
        self.index = faiss.read_index(str(index_dir / "index.faiss"))
        self.chunks = [json.loads(line) for line in (index_dir / "chunks.jsonl").open(encoding="utf-8")]
        # isti tekst kao za embedding: putanja naslova + tekst chunka
        self.bm25 = BM25Okapi([tokenize(f"{c['heading_path']}\n{c['text']}") for c in self.chunks])

    def search(self, question, k=3):
        qvec = self.model.encode([QUERY_PREFIX + question], normalize_embeddings=True,
                                 convert_to_numpy=True).astype("float32")
        n = max(k, CANDIDATES) if self.mode == "hybrid" else k
        scores, ids = self.index.search(qvec, n)
        vector_ranked = [int(i) for i in ids[0] if i != -1]
        cosine = {i: float(s) for s, i in zip(scores[0], ids[0]) if i != -1}
        top_similarity = float(scores[0][0])

        if self.mode == "hybrid":
            bm25_scores = self.bm25.get_scores(tokenize(question))
            bm25_ranked = [int(i) for i in np.argsort(-bm25_scores)[:CANDIDATES] if bm25_scores[i] > 0]
            fused = {}
            for ranked in (vector_ranked, bm25_ranked):
                for rank, i in enumerate(ranked, start=1):
                    fused[i] = fused.get(i, 0.0) + 1.0 / (RRF_K + rank)
            top = sorted(fused, key=fused.get, reverse=True)[:k]
        else:
            top = vector_ranked[:k]

        results = []
        for i in top:
            if i not in cosine:   # pronasao samo BM25: kosinus se racuna iz sacuvanog vektora
                cosine[i] = float(np.dot(self.index.reconstruct(i), qvec[0]))
            results.append({**self.chunks[i], "score": cosine[i], "query_top_similarity": top_similarity})
        return results


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    question = " ".join(sys.argv[1:]) or "What voltage initialization modes does the load flow support?"
    for r in Retriever().search(question):
        print(f"{r['score']:.3f}  {r['heading_path']}  ({r['part'] or '1/1'})\n       {r['url']}")
