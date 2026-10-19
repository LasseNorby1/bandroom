"""Bandroom audio worker — ai demo polish (spec §5.5, "the roadie can run the desk").

Deterministic dsp, no generation: per-stem loudness gain-staging, label-based
panning, bus glue compression, then a -14 LUFS "demo master" with a peak
ceiling. Either stage can chase a reference instead: the mix stage matches the
reference's band balance / stereo width / density (within ±6 dB of the by-ear
staging), the master stage hands the summed mix to matchering. Stems come in
and the result goes out via presigned urls — audio never touches the api.
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

# Bands the mix-reference balance matcher works in (Hz).
BANDS = [(20, 120), (120, 500), (500, 4000), (4000, 16000)]


class StemIn(BaseModel):
    url: str
    label: str


class PolishRequest(BaseModel):
    stems: list[StemIn]
    output_url: str
    master: bool = True
    reference_url: str | None = None
    mix_reference_url: str | None = None


class PolishResponse(BaseModel):
    duration_seconds: float
    peaks: list[float]


PEAK_BARS = 96


def waveform_peaks(audio: np.ndarray, bars: int = PEAK_BARS) -> list[float]:
    """Per-bar absolute peak of the mono sum, normalised to the loudest bar —
    the same shape the web player computes for uploads, so the api can store
    it and nobody has to decode the file just to draw it."""
    mono = np.abs(audio).max(axis=1)
    if len(mono) == 0:
        return [0.0] * bars
    edges = np.linspace(0, len(mono), bars + 1, dtype=int)
    peaks = np.array([mono[a:b].max() if b > a else 0.0 for a, b in zip(edges[:-1], edges[1:])])
    peaks = peaks / max(float(peaks.max()), 1e-3)
    return [round(float(p), 4) for p in peaks]


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


def band_energies(audio: np.ndarray, block: int = 8192) -> np.ndarray:
    """Mean spectral power of the mono sum in each of BANDS, via block ffts."""
    mono = audio.mean(axis=1)
    if len(mono) < block:
        mono = np.pad(mono, (0, block - len(mono)))
    usable = len(mono) - len(mono) % block
    frames = mono[:usable].reshape(-1, block) * np.hanning(block)
    power = (np.abs(np.fft.rfft(frames, axis=1)) ** 2).mean(axis=0)
    freqs = np.fft.rfftfreq(block, 1 / SR)
    return np.array([power[(freqs >= lo) & (freqs < hi)].sum() for lo, hi in BANDS]) + 1e-12


def stereo_width(audio: np.ndarray) -> float:
    """Side/mid rms ratio — 0 is mono, ~1 is very wide."""
    mid = (audio[:, 0] + audio[:, 1]) / 2
    side = (audio[:, 0] - audio[:, 1]) / 2
    return float(np.sqrt(np.mean(side**2)) / (np.sqrt(np.mean(mid**2)) + 1e-9))


def crest_db(audio: np.ndarray) -> float:
    """Peak over rms — high means dynamic, low means dense."""
    rms = float(np.sqrt(np.mean(audio**2))) + 1e-9
    return 20 * float(np.log10((float(np.max(np.abs(audio))) + 1e-9) / rms))


class MixProfile:
    """What the mix stage chases in a reference: band balance, width, density."""

    def __init__(self, reference: np.ndarray):
        energies = band_energies(reference)
        self.band_proportions = energies / energies.sum()
        self.width = stereo_width(reference)
        self.crest_db = crest_db(reference)


def match_balance(stem_energies: np.ndarray, target_proportions: np.ndarray) -> np.ndarray:
    """Per-stem gains (within ±6 dB of the by-ear staging) that pull the summed
    mix's band balance toward the reference's. Each iteration nudges every stem
    by the ratios of the bands its own energy lives in, so a bass-light
    reference pulls the bass stem down instead of tilting the whole mix.
    """
    gains = np.ones(len(stem_energies))
    lo, hi = 10 ** (-6 / 20), 10 ** (6 / 20)
    for _ in range(12):
        mix_bands = (gains[:, None] ** 2 * stem_energies).sum(axis=0)
        ratios = target_proportions / (mix_bands / mix_bands.sum())
        occupancy = stem_energies * gains[:, None] ** 2
        occupancy = occupancy / occupancy.sum(axis=1, keepdims=True)
        step = np.exp((occupancy * 0.5 * np.log(ratios)).sum(axis=1))
        gains = np.clip(gains * np.clip(step, 0.7, 1.4), lo, hi)
    return gains


def assign_positions(buckets: list[str]) -> list[float]:
    counts: dict[str, int] = {}
    positions = []
    for bucket in buckets:
        index = counts.get(bucket, 0)
        counts[bucket] = index + 1
        options = PAN[bucket]
        positions.append(options[index % len(options)])
    return positions


def sum_mix(
    staged: list[tuple[str, np.ndarray]],
    gains: np.ndarray,
    positions: list[float],
    pan_scale: float = 1.0,
) -> np.ndarray:
    mix = np.zeros_like(staged[0][1])
    for (_, audio), gain, position in zip(staged, gains, positions):
        mix += pan(audio * gain, float(np.clip(position * pan_scale, -1.0, 1.0)))
    return mix


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


# ---------------------------------------------------------------------------
# /analyze — bpm + key for one take (spec §5.5 "demo auto-metadata": mir, not
# an llm). Tempo from librosa's beat tracker; key by Krumhansl-Schmuckler
# template correlation on a mean chroma. Both are estimates — the api stores
# them in a derived table and the ui shows them as chips, never as truth.
# ---------------------------------------------------------------------------

KEY_NAMES = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"]
MAJOR_PROFILE = np.array([6.35, 2.23, 3.48, 2.33, 4.38, 4.09, 2.52, 5.19, 2.39, 3.66, 2.29, 2.88])
MINOR_PROFILE = np.array([6.33, 2.68, 3.52, 5.38, 2.60, 3.53, 2.54, 4.75, 3.98, 2.69, 3.34, 3.17])


class AnalyzeRequest(BaseModel):
    url: str


class AnalyzeResponse(BaseModel):
    bpm: float | None
    key: str | None
    key_confidence: float | None


def estimate_key(chroma_mean: np.ndarray) -> tuple[str | None, float | None]:
    """Best-correlating of 24 rotated major/minor profiles, with the margin to
    the runner-up as a 0..1 confidence."""
    scores: list[tuple[float, str]] = []
    for shift in range(12):
        rolled = np.roll(chroma_mean, -shift)
        scores.append((float(np.corrcoef(rolled, MAJOR_PROFILE)[0, 1]), KEY_NAMES[shift]))
        scores.append((float(np.corrcoef(rolled, MINOR_PROFILE)[0, 1]), KEY_NAMES[shift] + "m"))
    scores = [(0.0 if np.isnan(s) else s, k) for s, k in scores]
    scores.sort(reverse=True)
    best, second = scores[0], scores[1]
    if best[0] <= 0:
        return None, None
    confidence = float(np.clip((best[0] - second[0]) / max(best[0], 1e-6) * 4, 0.0, 1.0))
    return best[1], round(confidence, 2)


def analyze_audio(audio: np.ndarray) -> AnalyzeResponse:
    import librosa  # heavy import, only when the endpoint is used

    mono = audio.mean(axis=1).astype(np.float32)
    # Up to 90 seconds is plenty for tempo and key, and keeps this cheap.
    mono = mono[: SR * 90]
    bpm: float | None = None
    try:
        tempo, _ = librosa.beat.beat_track(y=mono, sr=SR)
        tempo = float(np.atleast_1d(tempo)[0])
        if 30 < tempo < 300:
            bpm = round(tempo, 1)
    except Exception:  # noqa: BLE001 — bpm is optional
        bpm = None
    key: str | None = None
    confidence: float | None = None
    try:
        chroma = librosa.feature.chroma_cqt(y=mono, sr=SR)
        key, confidence = estimate_key(chroma.mean(axis=1))
    except Exception:  # noqa: BLE001 — key is optional
        key, confidence = None, None
    return AnalyzeResponse(bpm=bpm, key=key, key_confidence=confidence)


@app.post("/analyze", response_model=AnalyzeResponse)
async def analyze(request: AnalyzeRequest) -> AnalyzeResponse:
    async with httpx.AsyncClient(timeout=300) as client:
        response = await client.get(request.url)
        response.raise_for_status()
        audio = decode_to_stereo(response.content)
    return await asyncio.to_thread(analyze_audio, audio)


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
        references: dict[str, bytes] = {}
        for url in {u for u in (request.mix_reference_url, request.reference_url) if u}:
            response = await client.get(url)
            response.raise_for_status()
            references[url] = response.content

    length = max(len(audio) for _, audio in stems)
    meter = pyln.Meter(SR)
    staged: list[tuple[str, np.ndarray]] = []
    for bucket, audio in stems:
        padded = np.zeros((length, 2), dtype=np.float64)
        padded[: len(audio)] = audio
        staged.append((bucket, loudness_normalize(padded, meter, TARGETS[bucket])))

    positions = assign_positions([bucket for bucket, _ in staged])
    gains = np.ones(len(staged))
    pan_scale, glue_ratio = 1.0, 3.0

    if request.mix_reference_url:
        profile = MixProfile(decode_to_stereo(references[request.mix_reference_url]))
        stem_energies = np.stack([band_energies(audio) for _, audio in staged])
        gains = match_balance(stem_energies, profile.band_proportions)
        flat_width = stereo_width(sum_mix(staged, gains, positions))
        pan_scale = float(np.clip(profile.width / max(flat_width, 1e-3), 0.4, 1.75))
        glue_ratio = float(np.interp(profile.crest_db, [8.0, 16.0], [4.0, 2.0]))
        print(
            f"mix reference: bands={np.round(profile.band_proportions, 3).tolist()}"
            f" gains_db={[round(20 * float(np.log10(g)), 1) for g in gains]}"
            f" pan_scale={round(pan_scale, 2)} glue_ratio={round(glue_ratio, 1)}",
            flush=True,
        )

    mix = bus_compress(sum_mix(staged, gains, positions, pan_scale), ratio=glue_ratio)

    if request.reference_url:
        # Matchering is cpu-bound and blocking — keep the event loop free.
        mix = await asyncio.to_thread(match_reference, mix, references[request.reference_url])
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

    return PolishResponse(duration_seconds=len(mix) / SR, peaks=waveform_peaks(mix))


# ---------------------------------------------------------------------------
# /sheet — the roadie's draft of lyrics and chords from one take (spec §5.5).
# Lyrics: faster-whisper on cpu, word timestamps, language auto-detected.
# Chords: chroma on the harmonic part, synced to beats, matched against the 24
# major/minor triad templates, smoothed. A sketch, never a finished sheet; the
# api turns the two timelines into a ChordPro draft the band accepts or not.
# ---------------------------------------------------------------------------

WHISPER_MODEL = os.environ.get("WHISPER_MODEL", "small")
_whisper = None


def whisper_model():
    global _whisper
    if _whisper is None:
        from faster_whisper import WhisperModel  # heavy import, only when used

        _whisper = WhisperModel(WHISPER_MODEL, device="cpu", compute_type="int8")
    return _whisper


class SheetRequest(BaseModel):
    url: str
    language: str | None = None


class SheetWord(BaseModel):
    start: float
    end: float
    text: str


class SheetSegment(BaseModel):
    start: float
    end: float
    words: list[SheetWord]


class SheetChord(BaseModel):
    start: float
    end: float
    chord: str


class SheetResponse(BaseModel):
    language: str | None
    language_probability: float | None
    segments: list[SheetSegment]
    chords: list[SheetChord]
    model: str


_NOTE_NAMES = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"]


def _chord_templates() -> tuple[np.ndarray, list[str]]:
    templates, names = [], []
    for root in range(12):
        major = np.zeros(12)
        major[[root, (root + 4) % 12, (root + 7) % 12]] = 1.0
        minor = np.zeros(12)
        minor[[root, (root + 3) % 12, (root + 7) % 12]] = 1.0
        templates += [major, minor]
        names += [_NOTE_NAMES[root], _NOTE_NAMES[root] + "m"]
    return np.array(templates), names


def estimate_chords(mono: np.ndarray) -> list[SheetChord]:
    import librosa

    harmonic = librosa.effects.harmonic(mono)
    tempo, beats = librosa.beat.beat_track(y=mono, sr=SR)
    chroma = librosa.feature.chroma_cqt(y=harmonic, sr=SR)
    if len(beats) < 4:
        return []
    synced = librosa.util.sync(chroma, beats, aggregate=np.median)
    times = librosa.frames_to_time(beats, sr=SR)
    templates, names = _chord_templates()
    norm = synced / (np.linalg.norm(synced, axis=0, keepdims=True) + 1e-9)
    scores = templates @ norm  # (24, beats)
    picks = scores.argmax(axis=0)
    confidence = scores.max(axis=0)
    # Smooth: a chord has to hold for two beats to count, else it inherits its neighbour.
    for i in range(1, len(picks) - 1):
        if picks[i] != picks[i - 1] and picks[i] != picks[i + 1]:
            picks[i] = picks[i - 1]
    chords: list[SheetChord] = []
    for i, pick in enumerate(picks):
        if confidence[i] < 0.5:
            continue
        start = float(times[i])
        end = float(times[i + 1]) if i + 1 < len(times) else start + 60.0 / max(float(np.atleast_1d(tempo)[0]), 1.0)
        name = names[int(pick)]
        if chords and chords[-1].chord == name:
            chords[-1].end = end
        else:
            chords.append(SheetChord(start=start, end=end, chord=name))
    return chords


def draft_sheet(audio: np.ndarray, language: str | None) -> SheetResponse:
    import librosa

    mono = audio.mean(axis=1).astype(np.float32)
    mono = mono[: SR * 60 * 12]  # twelve minutes is a song, not a rehearsal recording
    speech = librosa.resample(mono, orig_sr=SR, target_sr=16000)
    model = whisper_model()
    segments_iter, info = model.transcribe(
        speech, language=language, word_timestamps=True, vad_filter=True, beam_size=1, condition_on_previous_text=False,
    )
    segments: list[SheetSegment] = []
    for seg in segments_iter:
        words = [SheetWord(start=float(w.start), end=float(w.end), text=w.word.strip()) for w in (seg.words or []) if w.word.strip()]
        if not words:
            text = seg.text.strip()
            if not text:
                continue
            words = [SheetWord(start=float(seg.start), end=float(seg.end), text=text)]
        segments.append(SheetSegment(start=float(seg.start), end=float(seg.end), words=words))
    try:
        chords = estimate_chords(mono)
    except Exception:  # noqa: BLE001 — chords are optional
        chords = []
    return SheetResponse(
        language=info.language, language_probability=float(info.language_probability or 0),
        segments=segments, chords=chords, model=f"faster-whisper-{WHISPER_MODEL}+chroma-triads-1",
    )


@app.post("/sheet", response_model=SheetResponse)
async def sheet(request: SheetRequest) -> SheetResponse:
    async with httpx.AsyncClient(timeout=300) as client:
        response = await client.get(request.url)
        response.raise_for_status()
        audio = decode_to_stereo(response.content)
    return await asyncio.to_thread(draft_sheet, audio, request.language)
