import pathlib
import pymupdf4llm

ROOT = pathlib.Path(__file__).resolve().parents[2]   # SE_praksa
pdf = ROOT / "data" / "raw" / "powsybl-core-7.1.1.pdf"
out_dir = ROOT / "data" / "processed"
img_dir = out_dir / "img"
img_dir.mkdir(parents=True, exist_ok=True)

md = pymupdf4llm.to_markdown(
    str(pdf),
    write_images=True,          # izvlaci slike u folder
    image_path=str(img_dir),
    page_chunks=False,
)
# putanje slika relativne u odnosu na .md fajl
md = md.replace(img_dir.as_posix() + "/", "img/").replace(str(img_dir) + "\\", "img/")
(out_dir / "powsybl.md").write_text(md, encoding="utf-8")
print(f"Gotovo: {out_dir / 'powsybl.md'}")
