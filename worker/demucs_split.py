"""Split one wav into vocals / the rest with htdemucs, without torchaudio i/o
(torchaudio 2.9 moved save/load behind torchcodec) and without demucs.api
(demucs 4.0.1 predates it). Run as a subprocess so the model's memory is
released before whisper loads. Usage: demucs_split.py in.wav out_dir"""

import sys

import librosa
import numpy as np
import soundfile as sf
import torch
from demucs.apply import apply_model
from demucs.pretrained import get_model

src, out = sys.argv[1], sys.argv[2]
audio, sr = sf.read(src, dtype="float32", always_2d=True)  # (frames, channels)
if audio.shape[1] == 1:
    audio = np.repeat(audio, 2, axis=1)
model = get_model("htdemucs")
model.eval()
if sr != model.samplerate:
    audio = librosa.resample(audio.T, orig_sr=sr, target_sr=model.samplerate).T
wav = torch.from_numpy(np.ascontiguousarray(audio.T))  # (channels, frames)
ref = wav.mean(0)
wav = (wav - ref.mean()) / (ref.std() + 1e-8)
with torch.no_grad():
    sources = apply_model(model, wav[None], device="cpu", segment=7, shifts=0, overlap=0.2, progress=False, num_workers=0)[0]
sources = sources * (ref.std() + 1e-8) + ref.mean()
names = list(model.sources)
vocals = sources[names.index("vocals")].numpy().T
rest = sum(sources[i] for i, n in enumerate(names) if n != "vocals").numpy().T
sf.write(f"{out}/vocals.wav", vocals, model.samplerate)
sf.write(f"{out}/no_vocals.wav", rest, model.samplerate)
print("ok", vocals.shape[0], model.samplerate)
