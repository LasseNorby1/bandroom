import { ImageResponse } from "next/og";

export const alt = "Bandroom — one room for the band";
export const size = { width: 1200, height: 630 };
export const contentType = "image/png";

export default function OpenGraphImage() {
  return new ImageResponse(
    (
      <div
        style={{
          width: "100%",
          height: "100%",
          display: "flex",
          flexDirection: "column",
          justifyContent: "space-between",
          padding: 72,
          background: "#faf9f8",
          color: "#1c1917",
          fontFamily: "sans-serif",
        }}
      >
        <div style={{ display: "flex", alignItems: "center", gap: 14, fontSize: 34, fontWeight: 700 }}>
          Bandroom
          <div style={{ width: 14, height: 14, borderRadius: 999, background: "#c13b2a" }} />
        </div>
        <div style={{ display: "flex", flexDirection: "column", gap: 24 }}>
          <div style={{ fontSize: 88, fontWeight: 700, letterSpacing: -2, lineHeight: 1.02 }}>One room for the band.</div>
          <div style={{ fontSize: 34, color: "#6f6862", lineHeight: 1.35, maxWidth: 980 }}>
            A calendar that finds practice days by itself, the demos you&apos;re working on, and the talk around them.
          </div>
        </div>
        <div style={{ display: "flex", fontSize: 24, color: "#a8a09a" }}>bandroom.io · free while in early access</div>
      </div>
    ),
    size,
  );
}
