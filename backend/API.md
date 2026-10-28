# DocPilot API

HTTP + JSON API servera. Interaktivna dokumentacija (Swagger UI) je na `http://localhost:8000/docs`,
a OpenAPI specifikacija na `http://localhost:8000/openapi.json`.

## Pokretanje

Iz foldera `SE_praksa`, sa aktivnim `.venv`:

```
python -m uvicorn app.main:app --app-dir backend --port 8000
```

Pre prvog pokretanja mora postojati indeks (`python backend/ingestion/build_index.py`) i mora raditi
[Ollama](https://ollama.com) sa preuzetim modelom (`ollama pull llama3`).
Embedding model i indeks se učitavaju jednom, pri startu (oko 10 s).

| Promenljiva okruženja | Podrazumevano | Opis |
| --- | --- | --- |
| `OLLAMA_URL` | `http://127.0.0.1:11434` | Adresa Ollama servera |
| `OLLAMA_MODEL` | `llama3` | Model koji piše odgovor |
| `LLM_TIMEOUT` | `300` | Najduže čekanje na model, u sekundama |
| `REFUSAL_THRESHOLD` | `0.70` | Najmanji skor sličnosti da bi pitanje stiglo do modela |
| `RETRIEVAL_MODE` | `hybrid` | `hybrid` (BM25 + vektori, Reciprocal Rank Fusion) ili `vector` (samo vektori) |

## `POST /chat`

Postavlja pitanje. Server pronađe 3 najrelevantnije sekcije dokumentacije (hibridna pretraga: BM25 + vektori), a lokalni LLM (Ollama, llama3 8B)
iz njih napiše odgovor u Markdown-u, sa citatima u obliku `[1]`, `[2]` koji odgovaraju redosledu pronađenih sekcija.
Na računaru bez grafičke kartice odgovor stiže za 30 s do 4 min.

**Zahtev**

```json
{ "question": "Which CGMES versions are supported?" }
```

**Odgovor `200`**

```json
{
  "answer": "Current supported versions of CGMES are 2.4.15 and 3.0. [1]",
  "sources": [
    {
      "title": "Format specification",
      "heading_path": "Grid exchange formats > CIM-CGMES > Format specification",
      "url": "https://powsybl.readthedocs.io/projects/powsybl-core/en/v7.1.1/grid_exchange_formats/cgmes/format_specification.html",
      "score": 0.8393
    }
  ],
  "refused": false
}
```

| Polje | Tip | Opis |
| --- | --- | --- |
| `answer` | string | Odgovor u Markdown-u (naslovi, tekst, tabele, kod, slike sa HTTPS adresa, linkovi) |
| `sources` | niz | Korišćene sekcije: naslov, putanja naslova, URL, skor sličnosti (0–1) |
| `refused` | bool | `true` kada sistem odbije da odgovori; tada je `answer` = `Not covered in the documentation.`, a `sources` prazan |

**Odbijanje (guardrail 2).** Ako je najbolji skor pretrage manji od `REFUSAL_THRESHOLD`, server odmah vraća
odbijanje, **bez poziva modelu** (odgovor za ~15 ms). Ako pretraga prođe prag, ali model u sekcijama ne nađe
odgovor i napiše „Not covered in the documentation.“, i to se vraća kao `refused: true`.

```json
{ "answer": "Not covered in the documentation.", "sources": [], "refused": true }
```

**Greške**

| Status | Kada | Primer `detail` |
| --- | --- | --- |
| `400` | Prazno pitanje (ili samo razmaci) | `Pitanje ne sme biti prazno.` |
| `400` | Pitanje duže od 1000 karaktera | `Pitanje je predugo (1001 karaktera, dozvoljeno 1000).` |
| `503` | Ollama nije pokrenuta | `Ollama nije dostupna na http://127.0.0.1:11434 (...)` |
| `503` | Model nije odgovorio na vreme | `Model nije odgovorio za 300 s.` |
| `422` | Telo zahteva nije ispravan JSON ili nema polje `question` | (FastAPI validaciona greška) |

## `GET /health`

Status servera, indeksa i modela.

```json
{
  "status": "ok",
  "index": { "chunks": 804, "embedding_model": "BAAI/bge-small-en-v1.5" },
  "llm": { "status": "ok", "model": "llama3", "url": "http://127.0.0.1:11434" },
  "uptime_seconds": 42
}
```

`status` je `degraded` kada Ollama ne radi (`llm.status`: `unavailable`) ili model nije preuzet
(`model not pulled`). Vraća `503` ako indeks još nije učitan.
