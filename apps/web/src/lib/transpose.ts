/**
 * Transpose ChordPro-style chords by semitones. Only the bracketed chords move;
 * lyrics stay. Accidentals follow the target: sharps going up, flats going down,
 * except keys that are conventionally flat (F, Bb, Eb, Ab, Db) keep flats.
 */
const SHARPS = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];
const FLATS = ["C", "Db", "D", "Eb", "E", "F", "Gb", "G", "Ab", "A", "Bb", "B"];
const INDEX: Record<string, number> = {
  C: 0, "B#": 0, "C#": 1, Db: 1, D: 2, "D#": 3, Eb: 3, E: 4, Fb: 4, F: 5, "E#": 5,
  "F#": 6, Gb: 6, G: 7, "G#": 8, Ab: 8, A: 9, "A#": 10, Bb: 10, B: 11, Cb: 11,
};

function moveRoot(root: string, semitones: number, preferFlats: boolean): string {
  const index = INDEX[root];
  if (index === undefined) return root;
  const next = (((index + semitones) % 12) + 12) % 12;
  return (preferFlats ? FLATS : SHARPS)[next];
}

/** "Am7/G" → "Bm7/A" for +2. Anything that isn't a chord is returned untouched. */
export function transposeChord(chord: string, semitones: number, preferFlats: boolean): string {
  if (semitones === 0) return chord;
  const match = /^([A-G][#b]?)([^/]*)(?:\/([A-G][#b]?))?$/.exec(chord.trim());
  if (!match) return chord;
  const [, root, quality, bass] = match;
  const moved = moveRoot(root, semitones, preferFlats) + quality;
  return bass ? `${moved}/${moveRoot(bass, semitones, preferFlats)}` : moved;
}

export function transposeSheet(text: string, semitones: number): string {
  if (semitones === 0) return text;
  // Flats if the sheet already leans flat, else sharps.
  const flats = (text.match(/\[[A-G]b/g) ?? []).length;
  const sharps = (text.match(/\[[A-G]#/g) ?? []).length;
  const preferFlats = flats > sharps;
  return text.replace(/\[([^\]]+)\]/g, (_, chord: string) => `[${transposeChord(chord, semitones, preferFlats)}]`);
}
