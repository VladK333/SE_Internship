import time
from contextlib import asynccontextmanager

from fastapi import FastAPI, HTTPException
from pydantic import BaseModel, Field

from app.retriever import Retriever

TOP_K = 3
MAX_QUESTION_LENGTH = 1000

state = {}


@asynccontextmanager
async def lifespan(app):
    # model i indeks se ucitavaju jednom, pri startu servera
    state["retriever"] = Retriever()
    state["started"] = time.time()
    yield
    state.clear()


app = FastAPI(
    title="DocPilot API",
    version="0.1.0",
    description="RAG chat nad PowSyBl dokumentacijom (powsybl-core 7.1.1, CC-BY-4.0). "
                "U ovoj verziji /chat vraca top 3 najrelevantnije sekcije, bez LLM-a.",
    lifespan=lifespan,
)


class ChatRequest(BaseModel):
    question: str = Field(..., description=f"Pitanje, najvise {MAX_QUESTION_LENGTH} karaktera.",
                          examples=["Which CGMES versions are supported?"])


class Source(BaseModel):
    title: str = Field(..., description="Naslov sekcije")
    heading_path: str = Field(..., description="Putanja naslova, npr. 'Grid exchange formats > CIM-CGMES > Import'")
    url: str = Field(..., description="Link ka sekciji na powsybl.readthedocs.io")
    score: float = Field(..., description="Kosinusna slicnost pitanja i sekcije (0-1)")


class ChatResponse(BaseModel):
    answer: str = Field(..., description="Odgovor u Markdown formatu")
    sources: list[Source]
    refused: bool = Field(..., description="true ako sistem odbije da odgovori")


class HealthResponse(BaseModel):
    status: str
    index: dict
    llm: str
    uptime_seconds: int


def format_answer(results):
    """Pravi Markdown odgovor od pronadjenih sekcija (bez LLM-a)."""
    parts = []
    for n, r in enumerate(results, start=1):
        block = [f"### {n}. {r['title']}", f"*{r['heading_path']}*", "", r["text"]]
        for img in r["images"]:
            block += ["", f"![{img['alt']}]({img['url']})"]
        block += ["", f"[Izvor]({r['url']})"]
        parts.append("\n".join(block))
    return "\n\n---\n\n".join(parts)


@app.post("/chat", response_model=ChatResponse, summary="Postavi pitanje",
          responses={400: {"description": "Prazno ili predugo pitanje"}})
def chat(request: ChatRequest):
    question = request.question.strip()
    if not question:
        raise HTTPException(status_code=400, detail="Pitanje ne sme biti prazno.")
    if len(question) > MAX_QUESTION_LENGTH:
        raise HTTPException(status_code=400,
                            detail=f"Pitanje je predugo ({len(question)} karaktera, dozvoljeno {MAX_QUESTION_LENGTH}).")

    results = state["retriever"].search(question, k=TOP_K)
    sources, seen = [], set()
    for r in results:
        if r["heading_path"] not in seen:     # vise delova iste sekcije = jedan izvor
            seen.add(r["heading_path"])
            sources.append(Source(title=r["title"], heading_path=r["heading_path"],
                                  url=r["url"], score=round(r["score"], 4)))
    return ChatResponse(answer=format_answer(results), sources=sources, refused=False)


@app.get("/health", response_model=HealthResponse, summary="Status servera i modela")
def health():
    retriever = state.get("retriever")
    if retriever is None:
        raise HTTPException(status_code=503, detail="Indeks jos nije ucitan.")
    return HealthResponse(
        status="ok",
        index={"chunks": retriever.index.ntotal, "embedding_model": retriever.model_name},
        llm="not configured (dodaje se u 2. nedelji)",
        uptime_seconds=int(time.time() - state["started"]),
    )
