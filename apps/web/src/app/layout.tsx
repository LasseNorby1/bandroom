import type { Metadata } from "next";
import { Bricolage_Grotesque, Schibsted_Grotesk, Spline_Sans_Mono } from "next/font/google";
import "./globals.css";

const bricolage = Bricolage_Grotesque({ subsets: ["latin"], variable: "--font-bricolage" });
const schibsted = Schibsted_Grotesk({ subsets: ["latin"], variable: "--font-schibsted" });
const spline = Spline_Sans_Mono({ subsets: ["latin"], weight: ["400", "500"], variable: "--font-spline" });

export const metadata: Metadata = {
  title: "Bandroom",
  description: "One room for the band — practice days, demos, and the talk around them.",
};

export default function RootLayout({ children }: { children: React.ReactNode }) {
  return (
    <html lang="en" className={`${bricolage.variable} ${schibsted.variable} ${spline.variable}`}>
      <body>{children}</body>
    </html>
  );
}
