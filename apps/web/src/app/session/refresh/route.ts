import { NextResponse } from "next/server";
import { callAuth, clearRefreshCookie, readRefreshCookie, setRefreshCookie, type TokenPair } from "@/lib/session-server";

export async function POST() {
  const refreshToken = await readRefreshCookie();
  if (!refreshToken) {
    return new NextResponse(null, { status: 401 });
  }

  const response = await callAuth("/auth/refresh", { refreshToken });
  if (!response.ok) {
    await clearRefreshCookie();
    return new NextResponse(null, { status: 401 });
  }

  const tokens = (await response.json()) as TokenPair;
  await setRefreshCookie(tokens.refreshToken);
  return NextResponse.json({ accessToken: tokens.accessToken, expiresInSeconds: tokens.expiresInSeconds });
}
