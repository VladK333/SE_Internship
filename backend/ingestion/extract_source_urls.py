"""Jednokratno: iz originalnih PowSyBl .md fajlova izvlaci readthedocs URL-ove
stranica, sekcija (sa sidrom) i slika, proverava ih na zivom sajtu i pise JSON.

Rezultat je vec u data/processed/source_urls.json. Za ponovno pokretanje:
  git clone --depth 1 --branch v7.1.1 --filter=blob:none --sparse https://github.com/powsybl/powsybl-core.git core711
  git -C core711 sparse-checkout set docs
  python backend/ingestion/extract_source_urls.py core711/docs data/processed/source_urls.json
"""
import json, re, sys, unicodedata, pathlib, urllib.request, urllib.error
from concurrent.futures import ThreadPoolExecutor

SRC = pathlib.Path(sys.argv[1])
OUT = pathlib.Path(sys.argv[2])
VERSION = "v7.1.1"
BASE = f"https://powsybl.readthedocs.io/projects/powsybl-core/en/{VERSION}/"

def fetch(url):
    try:
        req = urllib.request.Request(url, headers={"User-Agent": "DocPilot-indexer"})
        with urllib.request.urlopen(req, timeout=30) as r:
            return r.status, r.read().decode("utf-8", "replace")
    except urllib.error.HTTPError as e:
        return e.code, ""
    except Exception:
        return 0, ""

def clean_heading(t):
    t = re.sub(r"\[([^\]]*)\]\([^)]*\)", r"\1", t)   # [txt](url) -> txt
    t = re.sub(r"\{[a-z]+\}`([^`<]*)(<[^>]*>)?`", r"\1", t)  # {ref}`txt <x>`
    return t.replace("`", "").replace("*", "").strip()

def slug(t):
    t = unicodedata.normalize("NFKD", t).encode("ascii", "ignore").decode().lower()
    t = re.sub(r"[^a-z0-9]+", "-", t).strip("-")
    return re.sub(r"^[0-9-]+", "", t)

def parse(md_path):
    rel = md_path.relative_to(SRC).as_posix()
    lines = md_path.read_text(encoding="utf-8").splitlines()
    in_code, label, heads, imgs = False, None, [], []
    for line in lines:
        if line.lstrip().startswith(("```", "~~~")):
            in_code = not in_code
            continue
        if in_code:
            continue
        m = re.match(r"^\(([^)]+)\)=\s*$", line)
        if m:
            label = m.group(1); continue
        m = re.match(r"^(#{1,6})\s+(.+?)\s*#*\s*$", line)
        if m:
            heads.append({"level": len(m.group(1)), "heading": clean_heading(m.group(2)), "label": label})
        label = None if not m and line.strip() else label if not m else None
        for alt, src in re.findall(r"!\[([^\]]*)\]\(([^)\s]+)", line):
            if "dark_mode" not in src and not src.startswith("http"):
                imgs.append({"alt": alt, "src": src, "sec": len(heads) - 1})
        for src in re.findall(r"```\{(?:image|figure)\}\s+(\S+)", line):
            if "dark_mode" not in src:
                imgs.append({"alt": "", "src": src, "sec": len(heads) - 1})
    return rel, heads, imgs

files = sorted(p for p in SRC.rglob("*.md") if p.name != "README.md")
parsed = [parse(p) for p in files]
titles = {rel: (h[0]["heading"] if h else rel) for rel, h, _ in parsed}

def page_url(rel):
    return BASE + rel[:-3] + ".html"

with ThreadPoolExecutor(8) as ex:
    pages_html = dict(zip([r for r, _, _ in parsed], ex.map(lambda p: fetch(page_url(p[0])), parsed)))

pages, sections, missing = [], [], []
for rel, heads, imgs in parsed:
    status, html = pages_html[rel]
    if status != 200:
        missing.append(rel); continue
    ids = set(re.findall(r'id="([^"]+)"', html))
    # breadcrumb iz index.md fajlova roditeljskih foldera
    parts = rel.split("/")[:-1]
    crumbs = [titles.get("/".join(parts[:i + 1]) + "/index.md") for i in range(len(parts))]
    crumbs = [c for c in crumbs if c]
    if rel.endswith("index.md") and crumbs:
        crumbs = crumbs[:-1]
    img_urls = []
    for im in imgs:
        name = pathlib.PurePosixPath(im["src"]).name
        img_urls.append({"alt": im["alt"], "url": BASE + "_images/" + name, "sec": im["sec"]})
    stack, page_secs = [], []
    for idx, h in enumerate(heads):
        stack = [s for s in stack if s["level"] < h["level"]] + [h]
        path = " > ".join(crumbs + [s["heading"] for s in stack])
        anchor = next((a for a in (h["label"], slug(h["heading"])) if a and a in ids), None)
        url = page_url(rel) + (f"#{anchor}" if anchor and h is not heads[0] else "")
        sec = {"heading": h["heading"], "level": h["level"], "heading_path": path, "url": url,
               "images": [{"alt": i["alt"], "url": i["url"]} for i in img_urls if i["sec"] == idx]}
        page_secs.append(sec)
        sections.append({"file": rel, **sec})
    img_urls = [{"alt": i["alt"], "url": i["url"]} for i in img_urls]
    pages.append({"file": rel, "title": titles[rel], "url": page_url(rel),
                  "breadcrumb": " > ".join(crumbs), "sections": page_secs, "images": img_urls})

# provera slika
all_imgs = sorted({i["url"] for p in pages for i in p["images"]})
with ThreadPoolExecutor(8) as ex:
    ok = dict(zip(all_imgs, ex.map(lambda u: fetch(u)[0] == 200, all_imgs)))
for p in pages:
    p["images"] = [i for i in p["images"] if ok[i["url"]]]
    for s in p["sections"]:
        s["images"] = [i for i in s["images"] if ok[i["url"]]]

out = {
    "source": "PowSyBl powsybl-core documentation",
    "version": VERSION.lstrip("v"),
    "license": "CC-BY-4.0 (c) authors of PowSyBl documentation",
    "base_url": BASE,
    "pages": pages,
}
OUT.write_text(json.dumps(out, ensure_ascii=False, indent=2), encoding="utf-8")
anch = sum(1 for s in sections if "#" in s["url"])
print(f"pages ok: {len(pages)}, missing in {VERSION}: {len(missing)} {missing}")
print(f"sections: {len(sections)}, with anchor: {anch}")
print(f"images ok: {sum(ok.values())}/{len(ok)}; broken: {[u for u, v in ok.items() if not v]}")
