import { NextResponse } from "next/server";
import { callAuth, setRefreshCookie, type TokenPair } from "@/lib/session-server";

export async function POST(request: Request) {
  const response = await callAuth("/auth/login", await request.json());
  if (!response.ok) {
    return new NextResponse(await response.text(), {
      status: response.status,
      headers: { "content-type": response.headers.get("content-type") ?? "application/json" },
    });
  }

  const tokens = (await response.json()) as TokenPair;
  await setRefreshCookie(tokens.refreshToken);
  return NextResponse.json({ accessToken: tokens.accessToken, expiresInSeconds: tokens.expiresInSeconds });
}
