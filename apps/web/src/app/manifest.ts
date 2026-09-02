import type { MetadataRoute } from "next";

// Installable-to-homescreen is the pre-Expo stopgap (spec §6.2).
export default function manifest(): MetadataRoute.Manifest {
  return {
    name: "Bandroom",
    short_name: "Bandroom",
    description: "One room for the band — practice days, demos, and the talk around them.",
    start_url: "/home",
    display: "standalone",
    background_color: "#faf9f8",
    theme_color: "#1c1917",
    icons: [
      {
        src: "/icon.svg",
        sizes: "any",
        type: "image/svg+xml",
        purpose: "any",
      },
    ],
  };
}
