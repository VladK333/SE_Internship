import json
import os
import pathlib
import threading
import time
from contextlib import asynccontextmanager
from datetime import datetime, timezone
from typing import Literal

from fastapi import FastAPI, HTTPException, Response
from fastapi.responses import StreamingResponse
from pydantic import BaseModel, Field

from app import llm
from app.retriever import Retriever

TOP_K = 3
MAX_QUESTION_LENGTH = 1000
# guardrail 2: ispod ovog skora pitanje se odbija bez poziva LLM-u
# (odredjeno u backend/evaluation/calibrate_threshold.py)
REFUSAL_THRESHOLD = float(os.getenv("REFUSAL_THRESHOLD", "0.70"))
REFUSAL_MESSAGE = "Not covered in the documentation."
# ocene odgovora iz klijenta (bonus: feedback dugmad), jedna JSON linija po oceni
FEEDBACK_FILE = pathlib.Path(os.getenv(
    "FEEDBACK_FILE", pathlib.Path(__file__).resolve().parents[2] / "data" / "feedback" / "feedback.jsonl"))
feedback_lock = threading.Lock()

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


class FeedbackRequest(BaseModel):
    question: str = Field(..., max_length=MAX_QUESTION_LENGTH, description="Pitanje na koje se ocena odnosi")
    answer: str = Field(..., max_length=50_000, description="Ocenjeni odgovor")
    rating: Literal["up", "down"] = Field(..., description="up = dobar odgovor, down = los")
    refused: bool = Field(False, description="Da li je odgovor bio odbijanje")
    sources: list[str] = Field(default_factory=list, description="Putanje naslova prikazanih izvora")


class HealthResponse(BaseModel):
    status: str
    index: dict
    llm: dict
    uptime_seconds: int


@app.post("/chat", response_model=ChatResponse, summary="Postavi pitanje",
          responses={400: {"description": "Prazno ili predugo pitanje"},
                     503: {"description": "LLM nije dostupan ili nije odgovorio na vreme"}})
def chat(request: ChatRequest):
    question, results, sources = retrieve(request)
    if results is None:
        return refusal()
    try:
        answer = llm.generate_answer(question, results)
    except llm.LLMError as e:
        raise HTTPException(status_code=503, detail=str(e))
    return finish(answer, sources)


@app.post("/chat/stream", summary="Postavi pitanje, odgovor stiže deo po deo (Server-Sent Events)",
          response_class=StreamingResponse,
          responses={200: {"content": {"text/event-stream": {}},
                           "description": "Događaji `sources`, `token` (više puta) i na kraju `done` ili `error`"},
                     400: {"description": "Prazno ili predugo pitanje"}})
def chat_stream(request: ChatRequest):
    # provere i pretraga pre početka strima, da bi greške u pitanju i dalje bile običan 400
    question, results, sources = retrieve(request)

    def events():
        if results is None:
            yield sse("done", refusal().model_dump())
            return
        yield sse("sources", {"sources": [s.model_dump() for s in sources]})
        parts = []
        try:
            for text in llm.stream_answer(question, results):
                parts.append(text)
                yield sse("token", {"text": text})
        except llm.LLMError as e:
            yield sse("error", {"detail": str(e)})
            return
        yield sse("done", finish("".join(parts).strip(), sources).model_dump())

    return StreamingResponse(events(), media_type="text/event-stream",
                             headers={"Cache-Control": "no-cache", "X-Accel-Buffering": "no"})


def retrieve(request):
    """Guardrail 4 (provera pitanja) i pretraga; results je None kada pitanje ne prolazi prag (guardrail 2)."""
    question = request.question.strip()
    if not question:
        raise HTTPException(status_code=400, detail="The question must not be empty.")
    if len(question) > MAX_QUESTION_LENGTH:
        raise HTTPException(status_code=400,
                            detail=f"The question is too long ({len(question)} characters, at most {MAX_QUESTION_LENGTH} allowed).")

    results = state["retriever"].search(question, k=TOP_K)
    if not results or results[0]["query_top_similarity"] < REFUSAL_THRESHOLD:
        return question, None, []

    sources, seen = [], set()
    for r in results:
        if r["heading_path"] not in seen:     # vise delova iste sekcije = jedan izvor
            seen.add(r["heading_path"])
            sources.append(Source(title=r["title"], heading_path=r["heading_path"],
                                  url=r["url"], score=round(r["score"], 4)))
    return question, results, sources


def refusal():
    return ChatResponse(answer=REFUSAL_MESSAGE, sources=[], refused=True)


def finish(answer, sources):
    if answer.startswith(REFUSAL_MESSAGE.rstrip(".")):
        # pretraga je prosla prag, ali model u sekcijama nije nasao odgovor
        return refusal()
    return ChatResponse(answer=answer, sources=sources, refused=False)


def sse(event, data):
    """Jedan Server-Sent Events dogadjaj; data je JSON u jednom redu."""
    return f"event: {event}\ndata: {json.dumps(data, ensure_ascii=False)}\n\n"


@app.post("/feedback", status_code=204, response_class=Response, summary="Oceni odgovor (palac gore / dole)")
def feedback(request: FeedbackRequest):
    row = {"time": datetime.now(timezone.utc).isoformat(timespec="seconds"), **request.model_dump()}
    with feedback_lock:     # zahtevi se obradjuju u vise niti; linije ne smeju da se pomesaju
        FEEDBACK_FILE.parent.mkdir(parents=True, exist_ok=True)
        with FEEDBACK_FILE.open("a", encoding="utf-8") as f:
            f.write(json.dumps(row, ensure_ascii=False) + "\n")
    return Response(status_code=204)


@app.get("/health", response_model=HealthResponse, summary="Status servera i modela")
def health():
    retriever = state.get("retriever")
    if retriever is None:
        raise HTTPException(status_code=503, detail="The index is not loaded yet.")
    llm_status = llm.status()
    return HealthResponse(
        status="ok" if llm_status["status"] == "ok" else "degraded",
        index={"chunks": retriever.index.ntotal, "embedding_model": retriever.model_name},
        llm=llm_status,
        uptime_seconds=int(time.time() - state["started"]),
    )
