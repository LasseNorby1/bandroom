FROM python:3.12-slim
RUN apt-get update && \
    apt-get install -y --no-install-recommends ffmpeg libsndfile1 && \
    rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt
# demucs without its optional extras: sphn and diffq need a compiler (none in slim),
# lameenc only writes mp3. htdemucs is a plain checkpoint; wav in, wav out.
RUN pip install --no-cache-dir --no-deps demucs==4.0.1 && \
    pip install --no-cache-dir dora-search einops julius lameenc openunmix pyyaml tqdm
COPY main.py .
EXPOSE 8090
CMD ["uvicorn", "main:app", "--host", "0.0.0.0", "--port", "8090"]
