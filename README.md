# DocPilot — RAG chat sistem

Chat sistem koji odgovara na pitanja o PowSyBl dokumentaciji (CIM/CGMES, elektroenergetski sistemi) koristeći RAG.
Specifikacija: [docs/specification](docs/specification/DocPilot%20-%20Specification%20(October%202026).md)

## Struktura

```
SE_praksa/
├── docs/
│   ├── specification/     # specifikacija zadatka
│   ├── progress/          # izveštaj o napretku za mentora (.md izvor + PDF)
│   └── research/          # istraživanje Markdown biblioteka, izveštaji
├── data/
│   ├── raw/               # originalni PDF (powsybl-core 7.1.1, CC-BY-4.0)
│   ├── processed/         # PDF konvertovan u Markdown + slike, source_urls.json (URL-ovi izvora)
│   └── index/             # FAISS indeks (generiše se, nije u git-u)
├── backend/               # Python server
│   ├── ingestion/         # konverzija, chunkovanje, embeddings, indeksiranje
│   ├── app/               # HTTP API (POST /chat, GET /health)
│   ├── evaluation/        # skup test pitanja i rezultati evaluacije
│   └── requirements.txt
└── frontend/              # WPF klijent (.NET Framework 4.8)
```

## Pokretanje

Potrebno: [Ollama](https://ollama.com) sa modelom `llama3` (`ollama pull llama3`) koji radi u pozadini.

**1. Backend (VS Code):** otvori folder `SE_praksa` u VS Code-u i pritisni **F5** (konfiguracija „DocPilot backend“ u
`.vscode/launch.json`). Server je spreman kada se u terminalu pojavi `Application startup complete`.
Bonus funkcije: streaming odgovora, ocena 👍/👎 (log u `data/feedback/`), svetla / tamna tema (dugme u zaglavlju).
API i interaktivna dokumentacija: `http://127.0.0.1:8000/docs` (opis u [backend/API.md](backend/API.md)).

**2. Klijent (Visual Studio):** otvori `frontend/DocPilot.sln` i pritisni **F5**. Pokreće se projekat `DocPilot.Client`.

### Priprema podataka (samo prvi put)

Iz foldera `SE_praksa`:

```
.venv\Scripts\activate
pip install -r backend\requirements.txt
python backend\ingestion\convert_pdf_to_md.py
python backend\ingestion\chunk.py
python backend\ingestion\build_index.py
```

Backend bez VS Code-a: `python -m uvicorn app.main:app --app-dir backend --port 8000`

## Rezultati evaluacije

48 pitanja (33 sa odgovorom, 15 bez), hibridna pretraga (BM25 + vektori) i llama3 8B:

| Metrika | Rezultat |
| --- | --- |
| Tačan izvor u top 3 | 30/33 (90,9 %) |
| Pitanja bez odgovora ispravno odbijena | 15/15 (100 %) |
| Prosečna ocena odgovora (1–3) | 2,82 |

Poređenje modela (bonus): gemma3 1B ima prosečnu ocenu 2,24 i 14/15 odbijenih, ali je oko 4 puta brža (medijana 15 s naprema 64 s).

Ponavljanje: `python backend\evaluation\evaluate.py` (pretraga, par sekundi) i
`python backend\evaluation\evaluate_answers.py [model]` (odgovori sa LLM-om, oko 45 min za llama3).

## Dokumenti

- Izveštaj o napretku i završni rezultati: [docs/progress/DocPilot_napredak.pdf](docs/progress/DocPilot_napredak.pdf)
- Istraživanje Markdown biblioteka: [docs/research/Markdown_biblioteke.pdf](docs/research/Markdown_biblioteke.pdf)
- API: [backend/API.md](backend/API.md)
- Plan demo-a: [docs/demo/Demo_plan.md](docs/demo/Demo_plan.md)

## Izvor dokumentacije

[PowSyBl](https://github.com/powsybl/powsybl-core) dokumentacija, licencirana pod CC-BY-4.0
(© autori PowSyBl dokumentacije).
