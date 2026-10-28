import os
import time
from contextlib import asynccontextmanager

from fastapi import FastAPI, HTTPException
from pydantic import BaseModel, Field

from app import llm
from app.retriever import Retriever

TOP_K = 3
MAX_QUESTION_LENGTH = 1000
# guardrail 2: ispod ovog skora pitanje se odbija bez poziva LLM-u
# (odredjeno u backend/evaluation/calibrate_threshold.py)
REFUSAL_THRESHOLD = float(os.getenv("REFUSAL_THRESHOLD", "0.70"))
REFUSAL_MESSAGE = "Not covered in the documentation."

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
    version="0.2.0",
    description="RAG chat nad PowSyBl dokumentacijom (powsybl-core 7.1.1, CC-BY-4.0). "
                "/chat pronalazi top 3 sekcije i iz njih lokalni LLM (Ollama) pise odgovor.",
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
    llm: dict
    uptime_seconds: int


@app.post("/chat", response_model=ChatResponse, summary="Postavi pitanje",
          responses={400: {"description": "Prazno ili predugo pitanje"},
                     503: {"description": "LLM nije dostupan ili nije odgovorio na vreme"}})
def chat(request: ChatRequest):
    question = request.question.strip()
    if not question:
        raise HTTPException(status_code=400, detail="Pitanje ne sme biti prazno.")
    if len(question) > MAX_QUESTION_LENGTH:
        raise HTTPException(status_code=400,
                            detail=f"Pitanje je predugo ({len(question)} karaktera, dozvoljeno {MAX_QUESTION_LENGTH}).")

    results = state["retriever"].search(question, k=TOP_K)
    if not results or results[0]["query_top_similarity"] < REFUSAL_THRESHOLD:
        return ChatResponse(answer=REFUSAL_MESSAGE, sources=[], refused=True)

    sources, seen = [], set()
    for r in results:
        if r["heading_path"] not in seen:     # vise delova iste sekcije = jedan izvor
            seen.add(r["heading_path"])
            sources.append(Source(title=r["title"], heading_path=r["heading_path"],
                                  url=r["url"], score=round(r["score"], 4)))
    try:
        answer = llm.generate_answer(question, results)
    except llm.LLMError as e:
        raise HTTPException(status_code=503, detail=str(e))
    if answer.startswith(REFUSAL_MESSAGE.rstrip(".")):
        # pretraga je prosla prag, ali model u sekcijama nije nasao odgovor
        return ChatResponse(answer=REFUSAL_MESSAGE, sources=[], refused=True)
    return ChatResponse(answer=answer, sources=sources, refused=False)


@app.get("/health", response_model=HealthResponse, summary="Status servera i modela")
def health():
    retriever = state.get("retriever")
    if retriever is None:
        raise HTTPException(status_code=503, detail="Indeks jos nije ucitan.")
    llm_status = llm.status()
    return HealthResponse(
        status="ok" if llm_status["status"] == "ok" else "degraded",
        index={"chunks": retriever.index.ntotal, "embedding_model": retriever.model_name},
        llm=llm_status,
        uptime_seconds=int(time.time() - state["started"]),
    )
