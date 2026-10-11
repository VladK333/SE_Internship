"""Pravi PDF izvestaja o napretku iz DocPilot_napredak.md.

Markdown -> HTML (markdown-it-py) -> PDF (Chrome ili Edge u headless rezimu).
Pokretanje (iz SE_praksa):  python docs/progress/build_pdf.py
"""
import pathlib
import shutil
import subprocess
import tempfile

from markdown_it import MarkdownIt

HERE = pathlib.Path(__file__).resolve().parent
SRC = HERE / "DocPilot_napredak.md"
OUT = HERE / "DocPilot_napredak.pdf"

BROWSERS = [
    r"C:\Program Files\Google\Chrome\Application\chrome.exe",
    r"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
    r"C:\Program Files\Microsoft\Edge\Application\msedge.exe",
]

CSS = """
@page {
  size: A4; margin: 18mm 18mm 20mm 18mm;
  @bottom-left { content: "DocPilot — izveštaj o napretku"; font: 8pt Arial; color: #777; }
  @bottom-right { content: "strana " counter(page) " / " counter(pages); font: 8pt Arial; color: #777; }
}
body { font-family: Arial, sans-serif; font-size: 10pt; line-height: 1.45; color: #222; }
h1 { font-size: 20pt; color: #1F3A5F; margin: 0 0 4pt; }
h2 { font-size: 14pt; color: #1F3A5F; margin: 18pt 0 6pt; padding-bottom: 2pt;
     border-bottom: 1px solid #C9CED6; break-after: avoid; }
h3 { font-size: 11.5pt; color: #1F3A5F; margin: 10pt 0 4pt; break-after: avoid; }
p { margin: 0 0 6pt; }
ul, ol { margin: 0 0 6pt; padding-left: 18pt; }
li { margin: 0 0 2pt; }
code { font-family: Consolas, monospace; font-size: 9pt; color: #8B2252; }
pre { background: #F3F4F6; padding: 6pt 8pt; border-radius: 3pt; break-inside: avoid; }
pre code { color: #222; font-size: 8.5pt; }
table { border-collapse: collapse; width: 100%; margin: 0 0 8pt; font-size: 9.5pt; }
tr { break-inside: avoid; }
th { background: #1F3A5F; color: white; text-align: left; padding: 3pt 6pt; }
td { padding: 3pt 6pt; border: 1px solid #C9CED6; vertical-align: top; }
tr:nth-child(even) td { background: #F7F8FA; }
"""


def find_browser():
    for b in BROWSERS:
        if pathlib.Path(b).exists():
            return b
    return shutil.which("chrome") or shutil.which("msedge")


def main():
    md = MarkdownIt("commonmark").enable("table")
    body = md.render(SRC.read_text(encoding="utf-8"))
    html = f'<!doctype html><html lang="sr"><head><meta charset="utf-8"><style>{CSS}</style></head><body>{body}</body></html>'

    browser = find_browser()
    if not browser:
        raise SystemExit("Nije pronadjen Edge ni Chrome.")
    with tempfile.TemporaryDirectory() as tmp:
        page = pathlib.Path(tmp) / "report.html"
        page.write_text(html, encoding="utf-8")
        subprocess.run([browser, "--headless=new", "--disable-gpu", "--no-pdf-header-footer",
                        f"--user-data-dir={tmp}", f"--print-to-pdf={OUT}", page.as_uri()],
                       check=True, capture_output=True, timeout=120)
    if not OUT.exists():
        raise SystemExit(f"PDF nije napravljen ({browser}).")
    print(f"Gotovo: {OUT}")


if __name__ == "__main__":
    main()
