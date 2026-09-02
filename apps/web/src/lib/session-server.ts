import { cookies } from "next/headers";

/// The ~80-line auth BFF (spec §6.2): the refresh token lives in an httponly
/// cookie only this server reads; the browser never holds a long-lived
/// credential. The api itself stays token-in-body for the mobile app.
const API = process.env.API_URL ?? "http://localhost:5180";

export const REFRESH_COOKIE = "bandroom_refresh";

export type TokenPair = { accessToken: string; expiresInSeconds: number; refreshToken: string };

export function callAuth(path: string, body: unknown): Promise<Response> {
  return fetch(`${API}${path}`, {
    method: "POST",
    headers: { "content-type": "application/json" },
    body: JSON.stringify(body),
    cache: "no-store",
  });
}

export async function setRefreshCookie(token: string): Promise<void> {
  const jar = await cookies();
  jar.set(REFRESH_COOKIE, token, {
    httpOnly: true,
    sameSite: "lax",
    secure: process.env.NODE_ENV === "production",
    path: "/",
    maxAge: 60 * 60 * 24 * 30,
  });
}

export async function clearRefreshCookie(): Promise<void> {
  const jar = await cookies();
  jar.delete(REFRESH_COOKIE);
}

export async function readRefreshCookie(): Promise<string | null> {
  const jar = await cookies();
  return jar.get(REFRESH_COOKIE)?.value ?? null;
}
