import io
import os

from fastapi import FastAPI, File, Form, HTTPException, UploadFile
from faster_whisper import WhisperModel
from faster_whisper.audio import decode_audio

MODEL_SIZE = os.environ.get("STT_MODEL_SIZE", "base")
DEVICE = os.environ.get("STT_DEVICE", "cpu")
COMPUTE_TYPE = os.environ.get("STT_COMPUTE_TYPE", "int8")
SAMPLE_RATE = 16000

app = FastAPI(title="andrey-chat-stt")
model = WhisperModel(MODEL_SIZE, device=DEVICE, compute_type=COMPUTE_TYPE)


@app.get("/health")
def health():
    return {"status": "ok", "model": MODEL_SIZE}


@app.post("/transcribe")
async def transcribe(file: UploadFile = File(...), max_duration_seconds: float | None = Form(default=None)):
    raw = await file.read()
    if not raw:
        raise HTTPException(status_code=400, detail={"code": "empty_audio", "message": "Audio clip is empty."})

    # Decode with PyAV (bundled with faster-whisper) so browser MediaRecorder
    # containers — WebM/Opus on Chrome, MP4/AAC on Safari — are handled, not just WAV.
    try:
        audio = decode_audio(io.BytesIO(raw), sampling_rate=SAMPLE_RATE)
    except Exception:
        raise HTTPException(
            status_code=400,
            detail={"code": "undecodable_audio", "message": "Audio clip could not be decoded."},
        )

    duration = len(audio) / SAMPLE_RATE
    if duration <= 0:
        raise HTTPException(status_code=400, detail={"code": "empty_audio", "message": "Audio clip is empty."})

    if max_duration_seconds is not None and duration > max_duration_seconds:
        raise HTTPException(
            status_code=422,
            detail={
                "code": "audio_too_long",
                "message": f"Audio duration {duration:.1f}s exceeds the {max_duration_seconds:.0f}s limit.",
                "limitSeconds": max_duration_seconds,
            },
        )

    segments, info = model.transcribe(audio)
    text = "".join(segment.text for segment in segments).strip()

    return {"text": text, "language": info.language, "durationSeconds": duration}
