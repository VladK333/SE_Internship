# DocPilot — RAG chat sistem

Chat sistem koji odgovara na pitanja o PowSyBl dokumentaciji (CIM/CGMES, elektroenergetski sistemi) koristeći RAG.
Specifikacija: [docs/specification](docs/specification/DocPilot%20-%20Specification%20(October%202026).md)

## Struktura

```
SE_praksa/
├── docs/
│   ├── specification/     # specifikacija zadatka
│   └── research/          # istraživanje Markdown biblioteka, izveštaji
├── data/
│   ├── raw/               # originalni PDF (powsybl-core 7.1.1, CC-BY-4.0)
│   ├── processed/         # PDF konvertovan u Markdown + slike
│   ├── reference_md/      # originalni .md izvori sa GitHub-a (za poređenje)
│   └── index/             # FAISS indeks (generiše se, nije u git-u)
├── backend/               # Python server
│   ├── ingestion/         # konverzija, chunkovanje, embeddings, indeksiranje
│   ├── app/               # HTTP API (POST /chat, GET /health)
│   ├── evaluation/        # skup test pitanja i rezultati evaluacije
│   └── requirements.txt
└── frontend/              # WPF klijent (.NET Framework 4.8)
```

## Pokretanje

Iz foldera `SE_praksa`:

```
.venv\Scripts\activate
pip install -r backend\requirements.txt
python backend\ingestion\convert_pdf_to_md.py
```

## Izvor dokumentacije

[PowSyBl](https://github.com/powsybl/powsybl-core) dokumentacija, licencirana pod CC-BY-4.0
(© autori PowSyBl dokumentacije).
