import type { Metadata } from "next";
import { Bricolage_Grotesque, Schibsted_Grotesk, Spline_Sans_Mono } from "next/font/google";
import "./globals.css";

const bricolage = Bricolage_Grotesque({ subsets: ["latin"], variable: "--font-bricolage" });
const schibsted = Schibsted_Grotesk({ subsets: ["latin"], variable: "--font-schibsted" });
const spline = Spline_Sans_Mono({ subsets: ["latin"], weight: ["400", "500"], variable: "--font-spline" });

const siteUrl = process.env.NEXT_PUBLIC_SITE_URL ?? "https://bandroom.io";

export const metadata: Metadata = {
  metadataBase: new URL(siteUrl),
  title: { default: "Bandroom", template: "%s · Bandroom" },
  description: "One room for the band — a calendar that finds practice days by itself, the demos you're working on, and the talk around them.",
  openGraph: {
    type: "website",
    siteName: "Bandroom",
    title: "Bandroom — one room for the band",
    description: "A calendar that finds practice days by itself, the demos you're working on, and the talk around them.",
    url: "/",
  },
  twitter: { card: "summary_large_image" },
};

// Cookie-free visit counts on the landing page, only when a GoatCounter code is
// baked in at build time (NEXT_PUBLIC_GOATCOUNTER=<code>); nothing otherwise.
const goatcounter = process.env.NEXT_PUBLIC_GOATCOUNTER;

export default function RootLayout({ children }: { children: React.ReactNode }) {
  return (
    <html lang="en" className={`${bricolage.variable} ${schibsted.variable} ${spline.variable}`}>
      <body>
        {children}
        {goatcounter && (
          <script
            data-goatcounter={`https://${goatcounter}.goatcounter.com/count`}
            async
            src="https://gc.zgo.at/count.js"
          />
        )}
      </body>
    </html>
  );
}
