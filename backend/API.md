# DocPilot API

HTTP + JSON API servera. Interaktivna dokumentacija (Swagger UI) je na `http://localhost:8000/docs`,
a OpenAPI specifikacija na `http://localhost:8000/openapi.json`.

## Pokretanje

Iz foldera `SE_praksa`, sa aktivnim `.venv`:

```
python -m uvicorn app.main:app --app-dir backend --port 8000
```

Pre prvog pokretanja mora postojati indeks (`python backend/ingestion/build_index.py`).
Model i indeks se učitavaju jednom, pri startu (oko 10 s).

## `POST /chat`

Postavlja pitanje. U ovoj verziji (1. nedelja) vraća 3 najrelevantnije sekcije dokumentacije, bez LLM-a.

**Zahtev**

```json
{ "question": "Which CGMES versions are supported?" }
```

**Odgovor `200`**

```json
{
  "answer": "### 1. Format specification\n*Grid exchange formats > CIM-CGMES > Format specification*\n\nCurrent supported versions of CGMES are 2.4.15 and 3.0. ...\n\n[Izvor](https://powsybl.readthedocs.io/...)\n\n---\n\n### 2. ...",
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
| `refused` | bool | `true` kada sistem odbije da odgovori (guardrail iz 2. nedelje); sada uvek `false` |

**Greške**

| Status | Kada | Primer `detail` |
| --- | --- | --- |
| `400` | Prazno pitanje (ili samo razmaci) | `Pitanje ne sme biti prazno.` |
| `400` | Pitanje duže od 1000 karaktera | `Pitanje je predugo (1001 karaktera, dozvoljeno 1000).` |
| `422` | Telo zahteva nije ispravan JSON ili nema polje `question` | (FastAPI validaciona greška) |

## `GET /health`

Status servera, indeksa i modela.

```json
{
  "status": "ok",
  "index": { "chunks": 804, "embedding_model": "BAAI/bge-small-en-v1.5" },
  "llm": "not configured (dodaje se u 2. nedelji)",
  "uptime_seconds": 42
}
```

Vraća `503` ako indeks još nije učitan.
