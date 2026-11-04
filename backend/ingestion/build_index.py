"""Pravi embeddinge za sve chunkove i FAISS indeks.

Ulaz:   data/processed/chunks.jsonl
Izlaz:  data/index/index.faiss   (vektori, IndexFlatIP nad normalizovanim vektorima = kosinusna slicnost)
        data/index/chunks.jsonl  (chunkovi istim redom kao vektori u indeksu)
        data/index/meta.json     (model, dimenzija, broj chunkova)
Pokretanje:  python backend/ingestion/build_index.py
"""
import json
import pathlib
import shutil
import time

import faiss
from sentence_transformers import SentenceTransformer

ROOT = pathlib.Path(__file__).resolve().parents[2]
CHUNKS_PATH = ROOT / "data" / "processed" / "chunks.jsonl"
INDEX_DIR = ROOT / "data" / "index"
MODEL_NAME = "BAAI/bge-small-en-v1.5"


def passage_text(chunk):
    """Tekst koji se embeduje: putanja naslova daje kontekst kratkim sekcijama."""
    return f"{chunk['heading_path']}\n\n{chunk['text']}"


def main():
    chunks = [json.loads(line) for line in CHUNKS_PATH.open(encoding="utf-8")]
    print(f"Ucitano {len(chunks)} chunkova, model {MODEL_NAME}")

    start = time.time()
    model = SentenceTransformer(MODEL_NAME)
    vectors = model.encode([passage_text(c) for c in chunks], batch_size=32,
                           normalize_embeddings=True, show_progress_bar=True,
                           convert_to_numpy=True).astype("float32")

    index = faiss.IndexFlatIP(vectors.shape[1])
    index.add(vectors)

    INDEX_DIR.mkdir(parents=True, exist_ok=True)
    faiss.write_index(index, str(INDEX_DIR / "index.faiss"))
    shutil.copyfile(CHUNKS_PATH, INDEX_DIR / "chunks.jsonl")
    (INDEX_DIR / "meta.json").write_text(json.dumps({
        "model": MODEL_NAME,
        "dimension": int(vectors.shape[1]),
        "chunks": index.ntotal,
        "normalized": True,
        "metric": "inner_product (= cosine)",
    }, indent=2), encoding="utf-8")

    print(f"Indeks: {index.ntotal} vektora, dimenzija {vectors.shape[1]}, "
          f"{time.time() - start:.0f} s  ->  {INDEX_DIR.relative_to(ROOT)}")


if __name__ == "__main__":
    main()
