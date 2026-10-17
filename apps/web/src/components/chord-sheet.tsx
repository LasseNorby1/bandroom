"use client";

import { Fragment } from "react";

/**
 * Renders ChordPro-ish text: "[Am]Hold the [F]line" shows the chords inline,
 * highlighted, above the syllable they sit on. Lines without brackets are plain
 * lyrics; {directives} are shown dimmed. No transposition yet.
 */
export function ChordSheet({ text }: { text: string }) {
  const lines = text.replace(/\r\n?/g, "\n").split("\n");
  return (
    <pre className="overflow-x-auto whitespace-pre-wrap font-mono text-[13px] leading-6">
      {lines.map((line, i) => (
        <Fragment key={i}>
          {renderLine(line)}
          {"\n"}
        </Fragment>
      ))}
    </pre>
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
