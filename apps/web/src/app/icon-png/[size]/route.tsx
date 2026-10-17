import { brandIcon } from "@/lib/brand-icon";

// /icon-png/192, /icon-png/512?maskable=1 — the manifest's Android icons.
export async function GET(request: Request, { params }: { params: Promise<{ size: string }> }) {
  const { size } = await params;
  const px = Number(size);
  if (![192, 512].includes(px)) {
    return new Response("Not found", { status: 404 });
  }
  const maskable = new URL(request.url).searchParams.get("maskable") === "1";
  const image = brandIcon(px, maskable);
  image.headers.set("Cache-Control", "public, max-age=604800, immutable");
  return image;
}
