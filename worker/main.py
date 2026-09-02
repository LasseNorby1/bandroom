"""Bandroom audio worker — ai demo polish (spec §5.5, "the roadie can run the desk").

Deterministic dsp, no generation: per-stem loudness gain-staging, label-based
panning, bus glue compression, then a -14 LUFS "demo master" with a peak
ceiling. Stems come in and the result goes out via presigned urls — audio never
touches the api. Reference-based mastering (matchering) is the v2 hook.
"""

import asyncio
import io
import os
import subprocess
import tempfile

import httpx
import matchering as mg
import numpy as np
import pyloudnorm as pyln
import soundfile as sf
from fastapi import FastAPI, HTTPException
from pydantic import BaseModel

app = FastAPI()

SR = 44100

# Pre-mix gain staging targets (LUFS): vocals forward, rhythm section behind.
TARGETS = {
    "vocals": -15.0,
    "drums": -16.5,
    "bass": -17.5,
    "guitar": -18.0,
    "keys": -18.0,
    "other": -18.0,
}

# Stereo positions per label; same-label stems alternate through the list.
PAN = {
    "drums": [0.0],
    "bass": [0.0],
    "vocals": [0.0, -0.2, 0.2],
    "guitar": [-0.35, 0.35, -0.6, 0.6],
    "keys": [0.25, -0.25],
    "other": [0.2, -0.2, 0.45, -0.45],
}


class StemIn(BaseModel):
    url: str
    label: str


class PolishRequest(BaseModel):
    stems: list[StemIn]
    output_url: str
    master: bool = True
    reference_url: str | None = None


class PolishResponse(BaseModel):
    duration_seconds: float


def decode_to_stereo(data: bytes) -> np.ndarray:
    """ffmpeg decodes any container/codec to 44.1k stereo float32."""
    proc = subprocess.run(
        [
            "ffmpeg", "-hide_banner", "-loglevel", "error", "-i", "pipe:0",
            "-f", "f32le", "-acodec", "pcm_f32le", "-ac", "2", "-ar", str(SR), "pipe:1",
        ],
        input=data,
        capture_output=True,
    )
    if proc.returncode != 0 or len(proc.stdout) < 8:
        raise HTTPException(422, f"could not decode stem: {proc.stderr.decode()[:200]}")
    audio = np.frombuffer(proc.stdout, dtype=np.float32)
    return audio.reshape(-1, 2).astype(np.float64)


def label_bucket(label: str) -> str:
    lowered = label.lower()
    for key in TARGETS:
        if key in lowered:
            return key
    if any(token in lowered for token in ("vox", "voc", "sang", "sing")):
        return "vocals"
    if any(token in lowered for token in ("git", "gtr")):
        return "guitar"
    if any(token in lowered for token in ("synth", "piano", "pad")):
        return "keys"
    return "other"


def loudness_normalize(audio: np.ndarray, meter: pyln.Meter, target_lufs: float) -> np.ndarray:
    loudness = meter.integrated_loudness(audio)
    if not np.isfinite(loudness):
        return audio
    return audio * (10 ** ((target_lufs - loudness) / 20))


def pan(audio: np.ndarray, position: float) -> np.ndarray:
    """Constant-power pan, position in [-1, 1]."""
    angle = (position + 1) * np.pi / 4
    out = audio.copy()
    out[:, 0] *= np.cos(angle) * np.sqrt(2)
    out[:, 1] *= np.sin(angle) * np.sqrt(2)
    return out


def bus_compress(mix: np.ndarray, threshold_db: float = -18.0, ratio: float = 3.0, block: int = 512) -> np.ndarray:
    """Block-rms glue compression — enough to make stems sit together."""
    total = len(mix)
    blocks = (total + block - 1) // block
    gains = np.ones(blocks)
    envelope = -80.0
    for i in range(blocks):
        segment = mix[i * block : (i + 1) * block]
        rms = float(np.sqrt(np.mean(segment**2))) + 1e-9
        level = 20 * np.log10(rms)
        envelope = level if level > envelope else envelope * 0.9 + level * 0.1
        over = envelope - threshold_db
        gains[i] = 10 ** ((-over * (1 - 1 / ratio)) / 20) if over > 0 else 1.0
    smoothed = np.convolve(gains, np.ones(8) / 8, mode="same")
    per_sample = np.repeat(smoothed, block)[:total]
    return mix * per_sample[:, None]


def match_reference(mix: np.ndarray, reference_bytes: bytes) -> np.ndarray:
    """Matchering: eq/loudness/width matched to the reference, own limiter."""
    reference = decode_to_stereo(reference_bytes)
    with tempfile.TemporaryDirectory() as tmp:
        target_path = os.path.join(tmp, "target.wav")
        reference_path = os.path.join(tmp, "reference.wav")
        output_path = os.path.join(tmp, "out.wav")
        sf.write(target_path, mix.astype(np.float32), SR, subtype="FLOAT")
        sf.write(reference_path, reference.astype(np.float32), SR, subtype="FLOAT")
        mg.process(target=target_path, reference=reference_path, results=[mg.pcm16(output_path)])
        matched, _ = sf.read(output_path, dtype="float64", always_2d=True)
        return matched


@app.get("/")
def health() -> dict[str, bool]:
    return {"ok": True}


@app.post("/polish", response_model=PolishResponse)
async def polish(request: PolishRequest) -> PolishResponse:
    if not request.stems:
        raise HTTPException(422, "no stems")

    async with httpx.AsyncClient(timeout=300) as client:
        stems: list[tuple[str, np.ndarray]] = []
        for stem in request.stems:
            response = await client.get(stem.url)
            response.raise_for_status()
            stems.append((label_bucket(stem.label), decode_to_stereo(response.content)))

    length = max(len(audio) for _, audio in stems)
    meter = pyln.Meter(SR)
    mix = np.zeros((length, 2), dtype=np.float64)
    pan_counts: dict[str, int] = {}
    for bucket, audio in stems:
        padded = np.zeros((length, 2), dtype=np.float64)
        padded[: len(audio)] = audio
        staged = loudness_normalize(padded, meter, TARGETS[bucket])
        index = pan_counts.get(bucket, 0)
        pan_counts[bucket] = index + 1
        positions = PAN[bucket]
        mix += pan(staged, positions[index % len(positions)])

    mix = bus_compress(mix)

    if request.reference_url:
        async with httpx.AsyncClient(timeout=300) as client:
            reference = await client.get(request.reference_url)
            reference.raise_for_status()
        # Matchering is cpu-bound and blocking — keep the event loop free.
        mix = await asyncio.to_thread(match_reference, mix, reference.content)
    else:
        if request.master:
            mix = loudness_normalize(mix, meter, -14.0)
        peak = float(np.max(np.abs(mix))) or 1.0
        if peak > 0.985:
            mix *= 0.985 / peak

    buffer = io.BytesIO()
    sf.write(buffer, mix.astype(np.float32), SR, format="WAV", subtype="PCM_16")

    async with httpx.AsyncClient(timeout=300) as client:
        upload = await client.put(
            request.output_url, content=buffer.getvalue(), headers={"content-type": "audio/wav"})
        upload.raise_for_status()

    return PolishResponse(duration_seconds=len(mix) / SR)
