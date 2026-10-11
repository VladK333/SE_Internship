"""Deli data/processed/powsybl.md na chunkove po naslovima i svakom dodaje metapodatke
(heading path, URL izvora, slike) iz data/processed/source_urls.json.

Rezultat: data/processed/chunks.jsonl (jedan JSON objekat po liniji).
Pokretanje (iz SE_praksa):  python backend/ingestion/chunk.py
"""
import json
import pathlib
import re

ROOT = pathlib.Path(__file__).resolve().parents[2]
MD_PATH = ROOT / "data" / "processed" / "powsybl.md"
URLS_PATH = ROOT / "data" / "processed" / "source_urls.json"
OUT_PATH = ROOT / "data" / "processed" / "chunks.jsonl"

MAX_WORDS = 350   # duze sekcije se dele (bge-small vidi max 512 tokena)
MIN_WORDS = 5     # sekcije sa manje reci (npr. samo naslov) se preskacu

# Sum koji PDF ponavlja na svakoj strani
NOISE_LINES = [
    re.compile(r"^\*\*<u>powsybl-core, Release [\d.]+</u>\*\*\s*$"),   # zaglavlje strane
    re.compile(r"^\*\*([0-9]+|[ivxlc]+)\*\*\s*$"),                     # broj strane
    re.compile(r"^\*\*Chapter \d+\. .+\*\*\s*$"),                      # podnozje "Chapter 1. ..."
    re.compile(r"^\*\*\d+\.\d+\. .+\*\*\s*$"),                         # podnozje "1.1. CIM-CGMES"
    re.compile(r"^\*\*CONTENTS\*\*\s*$"),
    re.compile(r"^\((continues on next page|continued from previous page)\)\s*$"),
]
HEADING = re.compile(r"^(#{1,6})\s+\*\*(.+?)\*\*\s*$")   # pravi naslovi su uvek bold
CHAPTER_LABEL = re.compile(r"^CHAPTER [A-Z]+$")          # "CHAPTER ONE" - preskace se
NUMBERED = re.compile(r"^(\d+(?:\.\d+)*)\s+(.+)$")         # "1.1.3 Import"
LOCAL_IMAGE = re.compile(r"!\[[^\]]*\]\(img/[^)]+\)")


def norm(text):
    return re.sub(r"[^a-z0-9]+", "", text.lower())


def clean_text(line):
    line = re.sub(r"\s*_?˓→_?\s*", "", line)   # oznaka preloma duge linije koda
    line = re.sub(r"\((continues on next page|continued from previous page)\)", "", line)
    line = re.sub(r"<!--.*?-->", "", line)     # komentari koje ubacuje pymupdf4llm
    return line.replace("␣", " ")


def heading_info(raw_level, text):
    """Vraca (nivo, naslov). Nivo se racuna iz numeracije, ne iz broja '#'."""
    m = NUMBERED.match(text)
    if m:
        return m.group(1).count(".") + 1, m.group(2).strip()   # 1.1 -> 2, 1.1.1 -> 3
    if raw_level == 1:
        return 1, text.strip().capitalize()                    # naslov poglavlja
    return 4, text.strip()                                     # nenumerisana podsekcija


def parse_sections(md):
    """Deli dokument na sekcije: [{path: [...], lines: [...]}]."""
    sections, stack, current = [], [], None
    started, in_code = False, False
    for line in md.splitlines():
        if line.lstrip().startswith("```"):
            in_code = not in_code
        if any(p.match(line) for p in NOISE_LINES):
            continue
        m = None if in_code else HEADING.match(line)
        if m:
            text = m.group(2).replace("**", "").strip()
            if CHAPTER_LABEL.match(text):
                started = True              # sve pre "CHAPTER ONE" je sadrzaj i uvod
                continue
            if not started:
                continue
            level, title = heading_info(len(m.group(1)), text)
            stack = [s for s in stack if s[0] < level] + [(level, title)]
            current = {"path": [t for _, t in stack], "lines": []}
            sections.append(current)
            continue
        if started and current is not None:
            current["lines"].append(clean_text(line))
    return sections


def split_blocks(lines):
    """Deli tekst na blokove (pasus, lista, tabela, kod) ne seckajuci kod i tabele."""
    blocks, buf, in_code = [], [], False
    for line in lines:
        if line.lstrip().startswith("```"):
            in_code = not in_code
            buf.append(line)
            if not in_code:
                blocks.append(buf); buf = []
            continue
        if in_code:
            buf.append(line); continue
        is_table = line.lstrip().startswith("|")
        if not line.strip():
            if buf and not buf[-1].lstrip().startswith("|"):
                blocks.append(buf); buf = []
            continue
        if buf and buf[-1].lstrip().startswith("|") and not is_table:
            blocks.append(buf); buf = []
        buf.append(line)
    if buf:
        blocks.append(buf)
    return ["\n".join(b).strip() for b in blocks if "".join(b).strip()]


def pack(blocks):
    """Spaja blokove u delove do MAX_WORDS reci."""
    parts, cur, words = [], [], 0
    for b in blocks:
        n = len(b.split())
        if cur and words + n > MAX_WORDS:
            parts.append(cur); cur, words = [], 0
        cur.append(b); words += n
    if cur:
        parts.append(cur)
    return ["\n\n".join(p) for p in parts]


class UrlMatcher:
    """Za heading path iz PDF-a nalazi najblizu sekciju u source_urls.json."""

    def __init__(self, data):
        self.by_heading = {}
        for page in data["pages"]:
            for s in page["sections"]:
                entry = {"url": s["url"], "images": s["images"],
                         "path": {norm(p) for p in s["heading_path"].split(" > ")}}
                self.by_heading.setdefault(norm(s["heading"]), []).append(entry)

    def match(self, path):
        """Trazi od najdubljeg naslova ka korenu; bira kandidata sa najvise zajednickih predaka."""
        wanted = {norm(p) for p in path}
        for depth in range(len(path), 0, -1):
            cands = self.by_heading.get(norm(path[depth - 1]), [])
            if cands:
                best = max(cands, key=lambda c: len(c["path"] & wanted))
                # slike samo ako je pogodjen bas taj naslov, ne neki predak
                return best["url"], best["images"] if depth == len(path) else []
        return None, []


def main():
    md = MD_PATH.read_text(encoding="utf-8")
    matcher = UrlMatcher(json.loads(URLS_PATH.read_text(encoding="utf-8")))

    chunks = []
    for sec in parse_sections(md):
        body = "\n".join(sec["lines"])
        body = LOCAL_IMAGE.sub("", body)       # lokalni PNG-ovi; HTTPS slike idu u metapodatke
        body = re.sub(r"\n{3,}", "\n\n", body).strip()
        if len(body.split()) < MIN_WORDS:
            continue
        url, images = matcher.match(sec["path"])
        parts = pack(split_blocks(body.splitlines()))
        for i, text in enumerate(parts):
            chunks.append({
                "id": f"c{len(chunks):04d}",
                "title": sec["path"][-1],
                "heading_path": " > ".join(sec["path"]),
                "part": f"{i + 1}/{len(parts)}" if len(parts) > 1 else None,
                "url": url,
                "images": images,
                "text": text,
                "word_count": len(text.split()),
            })

    with OUT_PATH.open("w", encoding="utf-8") as f:
        for c in chunks:
            f.write(json.dumps(c, ensure_ascii=False) + "\n")

    words = [c["word_count"] for c in chunks]
    print(f"Chunkova: {len(chunks)}  ->  {OUT_PATH.relative_to(ROOT)}")
    print(f"Reci po chunku: min {min(words)}, prosek {sum(words) // len(words)}, max {max(words)}")
    print(f"Bez URL-a: {sum(1 for c in chunks if not c['url'])}")
    print(f"Sa slikama: {sum(1 for c in chunks if c['images'])}")


if __name__ == "__main__":
    main()
