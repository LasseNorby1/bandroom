"use client";

import { Fragment, useState } from "react";
import { transposeSheet } from "@/lib/transpose";

/**
 * Renders ChordPro-ish text: "[Am]Hold the [F]line" shows the chords inline,
 * highlighted, above the syllable they sit on. Lines without brackets are plain
 * lyrics; {directives} are shown dimmed. No transposition yet.
 */
export function ChordSheet({ text }: { text: string }) {
  // Transposition is a view, never saved: the sheet stays in the key it was written in.
  const [semitones, setSemitones] = useState(0);
  const shown = transposeSheet(text, semitones);
  const lines = shown.replace(/\r\n?/g, "\n").split("\n");
  const hasChords = /\[[A-G]/.test(text);
  return (
    <div className="space-y-2">
      {hasChords && (
        <div className="flex items-center gap-2 font-mono text-[11px] text-muted">
          <span>transpose</span>
          <button type="button" onClick={() => setSemitones((s) => Math.max(-11, s - 1))} aria-label="Down a semitone" className="rounded-full border border-line px-2 py-0.5 hover:border-ink hover:text-ink">−</button>
          <span className="w-8 text-center text-ink">{semitones > 0 ? `+${semitones}` : semitones}</span>
          <button type="button" onClick={() => setSemitones((s) => Math.min(11, s + 1))} aria-label="Up a semitone" className="rounded-full border border-line px-2 py-0.5 hover:border-ink hover:text-ink">+</button>
          {semitones !== 0 && (
            <button type="button" onClick={() => setSemitones(0)} className="hover:text-ink">original</button>
          )}
        </div>
      )}
      <pre className="overflow-x-auto whitespace-pre-wrap font-mono text-[13px] leading-6">
        {lines.map((line, i) => (
          <Fragment key={i}>
            {renderLine(line)}
            {"\n"}
          </Fragment>
        ))}
      </pre>
    </div>
  );
}

function renderLine(line: string) {
  if (/^\s*\{.*\}\s*$/.test(line)) {
    return <span className="text-faint">{line}</span>;
  }
  const parts = line.split(/(\[[^\]]+\])/);
  return parts.map((part, i) =>
    part.startsWith("[") && part.endsWith("]") ? (
      <span key={i} className="relative mx-0.5 rounded bg-accent-soft px-1 font-semibold text-accent">
        {part.slice(1, -1)}
      </span>
    ) : (
      <span key={i}>{part}</span>
    ),
  );
}
