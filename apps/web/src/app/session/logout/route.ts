import { NextResponse } from "next/server";
import { callAuth, clearRefreshCookie, readRefreshCookie } from "@/lib/session-server";

export async function POST() {
  const refreshToken = await readRefreshCookie();
  if (refreshToken) {
    // Revokes the whole family server-side; the cookie goes regardless.
    await callAuth("/auth/logout", { refreshToken }).catch(() => undefined);
  }

  await clearRefreshCookie();
  return new NextResponse(null, { status: 204 });
}
