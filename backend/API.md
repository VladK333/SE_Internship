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
| `FEEDBACK_FILE` | `data/feedback/feedback.jsonl` | Gde se čuvaju ocene odgovora iz klijenta |

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
| `400` | Prazno pitanje (ili samo razmaci) | `The question must not be empty.` |
| `400` | Pitanje duže od 1000 karaktera | `The question is too long (1001 characters, at most 1000 allowed).` |
| `503` | Ollama nije pokrenuta | `Ollama is not reachable at http://127.0.0.1:11434 (...)` |
| `503` | Model nije odgovorio na vreme | `The model did not answer within 300 s.` |
| `422` | Telo zahteva nije ispravan JSON ili nema polje `question` | (FastAPI validaciona greška) |

## `POST /chat/stream`

Isto pitanje i iste provere kao `/chat`, ali odgovor stiže deo po deo, kao **Server-Sent Events**
(`text/event-stream`), dok ga model piše (bonus „streaming“). Klijent ga koristi podrazumevano.

| Događaj | Kada | `data` |
| --- | --- | --- |
| `sources` | odmah posle pretrage | `{"sources": [...]}` (isti oblik kao u `/chat`) |
| `token` | za svaki deo teksta | `{"text": "CGMES versions"}` |
| `done` | na kraju | ceo odgovor, isti oblik kao `/chat`: `{"answer", "sources", "refused"}` |
| `error` | Ollama ne radi ili model zastane | `{"detail": "..."}` |

```
event: sources
data: {"sources": [{"title": "Format specification", ...}]}

event: token
data: {"text": "CGMES"}

event: token
data: {"text": " versions 2.4.15 and 3.0 are supported. [1]"}

event: done
data: {"answer": "CGMES versions 2.4.15 and 3.0 are supported. [1]", "sources": [...], "refused": false}
```

- Prazno ili predugo pitanje vraća `400` pre početka strima, kao `/chat`.
- Odbijanje ispod praga odmah šalje samo `done` sa `refused: true`. Ako model napiše „Not covered in the documentation.“,
  delovi tog teksta stignu kao `token`, a `done` ima `refused: true` i prazne izvore.
- `LLM_TIMEOUT` ovde važi za čekanje na **sledeći** deo, ne za ceo odgovor.

## `POST /feedback`

Ocena odgovora palcem gore ili dole (bonus „feedback dugmad“). Klijent šalje pitanje, odgovor i ocenu, a server
dodaje jednu JSON liniju sa vremenom u `FEEDBACK_FILE`. Ako korisnik promeni ocenu, upisuje se nova linija; važi poslednja.

```json
{
  "question": "Which CGMES versions are supported?",
  "answer": "Current supported versions of CGMES are 2.4.15 and 3.0. [1]",
  "rating": "up",
  "refused": false,
  "sources": ["Grid exchange formats > CIM-CGMES > Format specification"]
}
```

| Polje | Tip | Opis |
| --- | --- | --- |
| `question` | string | Pitanje, najviše 1000 karaktera |
| `answer` | string | Ocenjeni odgovor |
| `rating` | `"up"` ili `"down"` | Ocena |
| `refused` | bool | Da li je ocenjeno odbijanje (opciono, podrazumevano `false`) |
| `sources` | niz stringova | Putanje naslova prikazanih izvora (opciono) |

Odgovor je `204 No Content`; neispravno telo (npr. `rating: "meh"`) vraća `422`.

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
