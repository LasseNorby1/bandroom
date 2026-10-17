import { brandIcon } from "@/lib/brand-icon";

// iOS ignores svg for the home-screen icon; it wants a 180px png, full-bleed.
export const size = { width: 180, height: 180 };
export const contentType = "image/png";

export default function AppleIcon() {
  return brandIcon(180, true);
}
