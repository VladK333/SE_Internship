"""Pretraga FAISS indeksa: pitanje -> top k najslicnijih chunkova sa skorom.

Test iz komandne linije (iz SE_praksa):
  python backend/app/retriever.py "How are ACLineSegments imported from CGMES?"
"""
import json
import pathlib
import sys

import faiss
from sentence_transformers import SentenceTransformer

ROOT = pathlib.Path(__file__).resolve().parents[2]
INDEX_DIR = ROOT / "data" / "index"
# bge modeli preporucuju ovaj prefiks za pitanja (ne i za pasuse)
QUERY_PREFIX = "Represent this sentence for searching relevant passages: "


class Retriever:
    def __init__(self, index_dir=INDEX_DIR):
        meta = json.loads((index_dir / "meta.json").read_text(encoding="utf-8"))
        self.model_name = meta["model"]
        self.model = SentenceTransformer(self.model_name)
        self.index = faiss.read_index(str(index_dir / "index.faiss"))
        self.chunks = [json.loads(line) for line in (index_dir / "chunks.jsonl").open(encoding="utf-8")]

    def search(self, question, k=3):
        vec = self.model.encode([QUERY_PREFIX + question], normalize_embeddings=True,
                                convert_to_numpy=True).astype("float32")
        scores, ids = self.index.search(vec, k)
        return [{**self.chunks[i], "score": float(s)} for s, i in zip(scores[0], ids[0]) if i != -1]


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    question = " ".join(sys.argv[1:]) or "How are ACLineSegments imported from CGMES?"
    for r in Retriever().search(question):
        print(f"{r['score']:.3f}  {r['heading_path']}  ({r['part'] or '1/1'})\n       {r['url']}")
