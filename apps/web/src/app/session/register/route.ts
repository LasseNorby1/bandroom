import { NextResponse } from "next/server";
import { callAuth, setRefreshCookie, type TokenPair } from "@/lib/session-server";

export async function POST(request: Request) {
  const body = (await request.json()) as { website?: string } & Record<string, unknown>;
  // Honeypot: the form has a "website" field people never see. A bot fills it;
  // it gets a convincing nothing and no account exists.
  if (typeof body.website === "string" && body.website.trim().length > 0) {
    return NextResponse.json({ accessToken: "", expiresInSeconds: 0 });
  }
  delete body.website;
  const response = await callAuth("/auth/register", body);
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
