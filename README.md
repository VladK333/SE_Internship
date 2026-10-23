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

## Izvor dokumentacije

[PowSyBl](https://github.com/powsybl/powsybl-core) dokumentacija, licencirana pod CC-BY-4.0
(© autori PowSyBl dokumentacije).
