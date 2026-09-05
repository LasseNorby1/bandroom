/// Waveform peaks: one number per bar, 0..1, normalised to the loudest bar.
/// Computed exactly once per take — by the uploader's browser here, or by the
/// worker for ai mixes — and stored on the version, so viewers never download
/// and decode a whole file just to draw it.

/** Mirrors the api's WaveformPeaks.BarCount and the worker's PEAK_BARS. */
export const PEAK_BARS = 96;

export function peaksFromChannel(channel: Float32Array, bars = PEAK_BARS): number[] {
  const blockSize = Math.max(1, Math.floor(channel.length / bars));
  const peaks = Array.from({ length: bars }, (_, i) => {
    let peak = 0;
    const start = i * blockSize;
    const end = Math.min(channel.length, start + blockSize);
    // Every 32nd sample is plenty for a 96-bar picture.
    for (let j = start; j < end; j += 32) {
      peak = Math.max(peak, Math.abs(channel[j]));
    }
    return peak;
  });
  const max = Math.max(...peaks, 0.01);
  return peaks.map((p) => Math.round((p / max) * 10_000) / 10_000);
}

export type DecodedAudio = { durationSeconds: number; peaks: number[] };

/** Decode with the Web Audio api; throws when the browser can't decode the format. */
export async function decodeAudio(buffer: ArrayBuffer): Promise<DecodedAudio> {
  const audioContext = new AudioContext();
  try {
    const decoded = await audioContext.decodeAudioData(buffer);
    return { durationSeconds: decoded.duration, peaks: peaksFromChannel(decoded.getChannelData(0)) };
  } finally {
    void audioContext.close();
  }
}
