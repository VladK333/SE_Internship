import pathlib
import pymupdf4llm

pdf = "powsybl-readthedocs-io-powsybl-core-en-v7.1.1.pdf"
md = pymupdf4llm.to_markdown(
    pdf,
    write_images=True,          # izvlaci slike u folder
    image_path="out_pymupdf/img",
    page_chunks=False,
)
pathlib.Path("out_pymupdf").mkdir(exist_ok=True)
pathlib.Path("out_pymupdf/powsybl.md").write_text(md, encoding="utf-8")