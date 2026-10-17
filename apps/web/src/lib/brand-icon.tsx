import { ImageResponse } from "next/og";

/**
 * The one mark, rasterised on demand: a bold B with the accent dot on the
 * ink-dark tile. `maskable` fills the whole canvas (Android safe zone), `any`
 * keeps the rounded tile.
 */
export function brandIcon(size: number, maskable = false): ImageResponse {
  const radius = maskable ? 0 : Math.round(size * 0.22);
  return new ImageResponse(
    (
      <div
        style={{
          width: size,
          height: size,
          display: "flex",
          alignItems: "center",
          justifyContent: "center",
          background: "#1c1917",
          borderRadius: radius,
        }}
      >
        <div
          style={{
            display: "flex",
            alignItems: "flex-end",
            marginLeft: -size * 0.06,
            fontFamily: "sans-serif",
            fontWeight: 700,
            fontSize: size * 0.58,
            lineHeight: 1,
            color: "#faf9f8",
          }}
        >
          B
          <div
            style={{
              width: size * 0.15,
              height: size * 0.15,
              borderRadius: 999,
              background: "#c13b2a",
              marginLeft: size * 0.03,
              marginBottom: size * 0.06,
            }}
          />
        </div>
      </div>
    ),
    { width: size, height: size },
  );
}
