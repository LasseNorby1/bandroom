import { brandIcon } from "@/lib/brand-icon";

// The favicon. Next links this as <link rel="icon"> next to the svg.
export const size = { width: 32, height: 32 };
export const contentType = "image/png";

export default function Icon() {
  return brandIcon(32);
}
