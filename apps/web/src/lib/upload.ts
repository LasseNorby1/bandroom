import { api } from "./api";
import { decodeAudio, type DecodedAudio } from "./peaks";
import type { DemoVersion, StemInfo, StemLabel } from "./types";

const extensionTypes: Record<string, string> = {
  ".mp3": "audio/mpeg",
  ".m4a": "audio/x-m4a",
  ".aac": "audio/aac",
  ".wav": "audio/wav",
  ".flac": "audio/flac",
  ".ogg": "audio/ogg",
  ".webm": "audio/webm",
};

function contentTypeOf(file: File): string {
  if (file.type) return file.type;
  const dot = file.name.lastIndexOf(".");
  return (dot >= 0 && extensionTypes[file.name.slice(dot).toLowerCase()]) || "audio/mpeg";
}

/** XHR because fetch has no upload progress. Content-Type must match the presign. */
function putWithProgress(url: string, file: File, contentType: string, onProgress?: (fraction: number) => void) {
  return new Promise<void>((resolve, reject) => {
    const xhr = new XMLHttpRequest();
    xhr.open("PUT", url);
    xhr.setRequestHeader("Content-Type", contentType);
    xhr.upload.onprogress = (event) => {
      if (event.lengthComputable && onProgress) onProgress(event.loaded / event.total);
    };
    xhr.onload = () =>
      xhr.status >= 200 && xhr.status < 300 ? resolve() : reject(new Error(`upload failed (${xhr.status})`));
    xhr.onerror = () => reject(new Error("upload failed"));
    xhr.send(file);
  });
}

/**
 * Duration + waveform peaks from the file we already have in hand. Decoding
 * happens once, here, on the uploader's machine; every later viewer gets the
 * peaks from the api. Formats the browser can't decode fall back to metadata
 * duration and no peaks (the player then decodes lazily on first play).
 */
async function analyzeAudio(file: File): Promise<{ durationSeconds: number | null; peaks: number[] | null }> {
  try {
    const decoded: DecodedAudio = await decodeAudio(await file.arrayBuffer());
    return decoded;
  } catch {
    return { durationSeconds: await measureDuration(file), peaks: null };
  }
}

function measureDuration(file: File): Promise<number | null> {
  return new Promise((resolve) => {
    const url = URL.createObjectURL(file);
    const audio = new Audio();
    audio.preload = "metadata";
    audio.onloadedmetadata = () => {
      URL.revokeObjectURL(url);
      resolve(Number.isFinite(audio.duration) ? audio.duration : null);
    };
    audio.onerror = () => {
      URL.revokeObjectURL(url);
      resolve(null);
    };
    audio.src = url;
  });
}

export async function uploadDemoVersion(
  bandId: string,
  ideaId: string,
  file: File,
  onProgress?: (fraction: number) => void,
): Promise<DemoVersion> {
  const contentType = contentTypeOf(file);
  const { data: init, error } = await api.POST("/bands/{bandId}/song-ideas/{ideaId}/versions/uploads", {
    params: { path: { bandId, ideaId } },
    body: { fileName: file.name, contentType, sizeBytes: file.size },
  });
  if (error || !init) throw error ?? new Error("init failed");

  // Analyse while the bytes are in flight — both are local work on the same file.
  const [, analysis] = await Promise.all([
    putWithProgress(init.uploadUrl, file, contentType, onProgress),
    analyzeAudio(file),
  ]);

  const { data: version, error: confirmError } = await api.POST("/bands/{bandId}/versions/{versionId}/confirm", {
    params: { path: { bandId, versionId: init.versionId } },
    body: { durationSeconds: analysis.durationSeconds, peaks: analysis.peaks },
  });
  if (confirmError || !version) throw confirmError ?? new Error("confirm failed");
  return version;
}

export async function uploadStem(
  bandId: string,
  ideaId: string,
  file: File,
  label: StemLabel,
  onProgress?: (fraction: number) => void,
): Promise<StemInfo> {
  const contentType = contentTypeOf(file);
  const { data: init, error } = await api.POST("/bands/{bandId}/song-ideas/{ideaId}/stems/uploads", {
    params: { path: { bandId, ideaId } },
    body: { label, name: null, fileName: file.name, contentType, sizeBytes: file.size },
  });
  if (error || !init) throw error ?? new Error("init failed");

  await putWithProgress(init.uploadUrl, file, contentType, onProgress);

  const { data: stem, error: confirmError } = await api.POST("/bands/{bandId}/stems/{stemId}/confirm", {
    params: { path: { bandId, stemId: init.stemId } },
  });
  if (confirmError || !stem) throw confirmError ?? new Error("confirm failed");
  return stem;
}

export async function uploadReference(
  bandId: string,
  file: File,
  onProgress?: (fraction: number) => void,
) {
  const contentType = contentTypeOf(file);
  const title = file.name.replace(/\.[^.]+$/, "");
  const { data: init, error } = await api.POST("/bands/{bandId}/references/uploads", {
    params: { path: { bandId } },
    body: { title, fileName: file.name, contentType, sizeBytes: file.size },
  });
  if (error || !init) throw error ?? new Error("init failed");

  await putWithProgress(init.uploadUrl, file, contentType, onProgress);

  const { data: reference, error: confirmError } = await api.POST(
    "/bands/{bandId}/references/{referenceId}/confirm",
    { params: { path: { bandId, referenceId: init.referenceId } } },
  );
  if (confirmError || !reference) throw confirmError ?? new Error("confirm failed");
  return reference;
}

export function formatBytes(bytes: number): string {
  if (bytes < 1024 * 1024) return `${Math.max(1, Math.round(bytes / 1024))} KB`;
  if (bytes < 1024 * 1024 * 1024) return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  return `${(bytes / (1024 * 1024 * 1024)).toFixed(2)} GB`;
}

export function formatSeconds(total: number): string {
  const minutes = Math.floor(total / 60);
  const seconds = Math.floor(total % 60);
  return `${minutes}:${String(seconds).padStart(2, "0")}`;
}
