# DocPilot — izveštaj o napretku

RAG chat sistem nad PowSyBl dokumentacijom · Autor: Vladana · Mentori: Mihajlo Medic, Novo Sandara

Ovaj dokument se dopunjuje posle svake nove funkcionalnosti. Najnovije izmene su u odeljku *Dnevnik izmena* na kraju.

## Status po specifikaciji

| Nedelja | Zadatak | Status |
| --- | --- | --- |
| 1 | Izbor dokumentacije | Završeno (čeka potvrdu mentora) |
| 1 | Konverzija dokumentacije u Markdown | Završeno |
| 1 | URL-ovi izvora za citate | Završeno |
| 1 | Ingestion: chunkovanje i metapodaci | Završeno |
| 1 | Embeddings i FAISS indeks | U toku |
| 1 | `POST /chat` vraća top 3 sekcije bez LLM-a, `GET /health` | Nije počelo |
| 1 | Istraživanje Markdown biblioteka za WPF | Nije počelo |
| 2 | LLM i guardrails | Nije počelo |
| 2 | WPF klijent (.NET Framework 4.8) | Nije počelo |
| 3 | Evaluacija, ispravke, prezentacija | Nije počelo |

## Arhitektura i struktura projekta

Backend je u Python-u, jer se tu nalazi ceo RAG ekosistem (sentence-transformers, FAISS, Ollama klijent). Klijent je, po specifikaciji, WPF na .NET Framework 4.8.

```
SE_praksa/
├── docs/
│   ├── specification/   specifikacija zadatka
│   ├── progress/        ovaj izveštaj (Markdown izvor + PDF)
│   └── research/        istraživanje Markdown biblioteka
├── data/
│   ├── raw/             originalni PDF dokumentacije
│   ├── processed/       powsybl.md, source_urls.json, chunks.jsonl
│   └── index/           FAISS indeks (generiše se)
├── backend/
│   ├── ingestion/       konverzija, URL-ovi, chunkovanje, indeksiranje
│   ├── app/             HTTP API
│   └── evaluation/      test pitanja i rezultati
└── frontend/            WPF klijent
```

## 1. Izbor dokumentacije

**Izabrano:** PowSyBl (Power System Blocks), dokumentacija biblioteke `powsybl-core`, verzija 7.1.1. PowSyBl je open-source projekat LF Energy / Linux Foundation za modelovanje i simulaciju elektroenergetskih mreža, sa posebnim poglavljima o CIM/CGMES formatu.

**Licenca:** dokumentacija je pod CC-BY-4.0 (kod pod MPL 2.0), što dozvoljava upotrebu uz navođenje izvora.

**Provera uslova iz specifikacije:**

| Uslov | Ispunjeno |
| --- | --- |
| Javna, sa licencom koja dozvoljava upotrebu | Da, CC-BY-4.0 |
| Najmanje 50 strana | Da, oko 260 strana PDF-a (oko 77.000 reči) |
| Tabele, slike, liste, kod blokovi | Da, svi elementi postoje |
| Tema koju mogu da procenim | Da, elektroenergetski sistemi i CIM |

**Razmatrane alternative:**

- *EPRI CIM Primer (8. izdanje)* — odbačen: autorska prava EPRI-ja, licenca za ovu upotrebu nije jasna, PDF je šifrovan.
- *MATPOWER User's Manual* — licenca CC BY 4.0, ali ne pokriva CIM.
- *ENTSO-E CGMES Library* — licenca nije jasno navedena za sve dokumente.

## 2. Konverzija PDF-a u Markdown

**Skripta:** `backend/ingestion/convert_pdf_to_md.py` · **Rezultat:** `data/processed/powsybl.md`

Upoređena su dva alata na istom PDF-u:

| Kriterijum | pymupdf4llm | Docling |
| --- | --- | --- |
| Hijerarhija naslova | Očuvana (#, ##, ###) | Svi naslovi na istom nivou |
| Ugnežđene liste | Očuvane | Spljoštene |
| Inline kod | Očuvan | Izgubljen |
| Tabele | Dobre | Dobre |
| Kod blokovi | Spljošteni u jedan red | Spljošteni u jedan red |
| Zaglavlja i brojevi strana | Ostaju (uklanjaju se kasnije) | Uklonjeni |
| Brzina | Nekoliko sekundi | 10+ minuta na CPU |

**Odluka:** pymupdf4llm. Hijerarhija naslova je presudna, jer specifikacija traži chunkovanje po naslovima i čuvanje putanje naslova. Šum sa strana se pouzdano uklanja regex-om u chunkeru, dok se izgubljena hijerarhija ne može vratiti.

**Pokretanje:** `python backend/ingestion/convert_pdf_to_md.py`

## 3. URL-ovi izvora

**Skripta:** `backend/ingestion/extract_source_urls.py` · **Rezultat:** `data/processed/source_urls.json`

PDF ne sadrži web adrese sekcija, a specifikacija traži izvore na koje može da se klikne. Skripta čita originalne Markdown izvore dokumentacije za verziju 7.1.1 (sa GitHub-a) i za svaku stranicu i sekciju računa adresu na powsybl.readthedocs.io, zajedno sa sidrom (`#...`) za direktan skok na sekciju. Svaki URL je proveren na živom sajtu.

| Stavka | Broj |
| --- | --- |
| Stranica | 96 (sve dostupne) |
| Sekcija | 688 (592 sa direktnim sidrom) |
| Slika sa HTTPS adresom | 18 (sve dostupne) |

Slike su vezane za sekciju u kojoj se nalaze, pa odgovor može da prikaže tačno odgovarajući dijagram. Sve slike su sa readthedocs-a, u skladu sa zahtevom da se slike učitavaju samo sa pouzdanih HTTPS izvora.

## 4. Chunkovanje (ingestion)

**Skripta:** `backend/ingestion/chunk.py` · **Rezultat:** `data/processed/chunks.jsonl`

**Šta radi:**

1. Uklanja šum iz PDF konverzije: zaglavlja i podnožja strana, brojeve strana, sadržaj, oznake „continues on next page“ i oznake preloma linija u kodu.
2. Deli dokument po naslovima. Nivo naslova se računa iz numeracije (1.1 → 1.1.3 → podsekcija). Znak `#` unutar kod blokova se ne tretira kao naslov.
3. Duge sekcije deli na delove do 350 reči (model bge-small obrađuje najviše 512 tokena). Kod blokovi i tabele se nikada ne seku.
4. Svakom chunku dodaje metapodatke: naslov, putanju naslova, URL izvora i HTTPS slike. Kada isti naslov postoji na više mesta (npr. „BusbarSection“ u Import-u i u Export-u), bira se sekcija sa najviše zajedničkih nadređenih naslova.

**Primer metapodataka jednog chunka:**

```
title:        ACLineSegment
heading_path: Grid exchange formats > CIM-CGMES > Import > ACLineSegment
url:          .../grid_exchange_formats/cgmes/import.html#cgmes-ac-line-segment-import
part:         1/2
```

**Rezultat:**

| Stavka | Vrednost |
| --- | --- |
| Broj chunkova | 804 |
| Reči po chunku | min 5, prosek 86, max 350 |
| Chunkovi bez URL-a izvora | 0 |
| Chunkovi sa slikama | 18 |
| Preostali šum sa strana | 0 |

**Otvorena pitanja:**

- 87 chunkova ima manje od 15 reči (uglavnom opisi pojedinačnih parametara). Zadržani su jer često sadrže tačan odgovor; ako evaluacija pokaže da smetaju, biće spojeni sa susednim.
- Dugi XML i JSON primeri su posle PDF konverzije u jednom redu. Ne utiče na pretragu, ali utiče na izgled odgovora.

**Pokretanje:** `python backend/ingestion/chunk.py`

## Dnevnik izmena

| Datum | Izmena |
| --- | --- |
| 11.10.2026. | Izbor dokumentacije (PowSyBl 7.1.1), poređenje alata za konverziju, konverzija PDF-a u Markdown |
| 11.10.2026. | Struktura projekta (backend / frontend / data / docs) |
| 11.10.2026. | Izvlačenje i provera URL-ova izvora i slika |
| 11.10.2026. | Chunker sa metapodacima |
| 11.10.2026. | Uveden ovaj izveštaj o napretku |
