/// Access-token handling (spec §6.2): memory only, never storage. A silent
/// refresh bootstraps it; refreshes are single-flight because two concurrent
/// refreshes would present the same rotating token twice — which the api treats
/// as theft and answers by revoking the whole family.
let accessToken: string | null = null;
let accessTokenExpiresAt = Number.MAX_SAFE_INTEGER;
let refreshInFlight: Promise<string | null> | null = null;

function rememberToken(token: string): void {
  accessToken = token;
  accessTokenExpiresAt = decodeExpiryMs(token);
}

function decodeExpiryMs(token: string): number {
  try {
    const segment = token.split(".")[1].replace(/-/g, "+").replace(/_/g, "/");
    const padded = segment + "=".repeat((4 - (segment.length % 4)) % 4);
    const payload = JSON.parse(atob(padded)) as { exp?: number };
    return payload.exp ? payload.exp * 1000 : Number.MAX_SAFE_INTEGER;
  } catch {
    return Number.MAX_SAFE_INTEGER;
  }
}

export function getAccessToken(): string | null {
  // A token at (or within 10s of) expiry is as good as none — after the laptop
  // sleeps, callers must fall through to refresh instead of presenting it.
  if (accessToken && Date.now() >= accessTokenExpiresAt - 10_000) {
    accessToken = null;
  }
  return accessToken;
}

export function refreshSession(): Promise<string | null> {
  refreshInFlight ??= (async () => {
    try {
      const response = await fetch("/session/refresh", { method: "POST" });
      if (!response.ok) {
        accessToken = null;
        return null;
      }
      const tokens = (await response.json()) as { accessToken: string };
      rememberToken(tokens.accessToken);
      return accessToken;
    } catch {
      return null;
    } finally {
      refreshInFlight = null;
    }
  })();
  return refreshInFlight;
}

export type AuthResult = { ok: true } | { ok: false; status: number; detail: string };

async function callSession(path: string, body: unknown): Promise<AuthResult> {
  const response = await fetch(path, {
    method: "POST",
    headers: { "content-type": "application/json" },
    body: JSON.stringify(body),
  });
  if (!response.ok) {
    return { ok: false, status: response.status, detail: await describeProblem(response) };
  }
  const tokens = (await response.json()) as { accessToken: string };
  rememberToken(tokens.accessToken);
  return { ok: true };
}

export function login(email: string, password: string): Promise<AuthResult> {
  return callSession("/session/login", { email, password });
}

export function registerAccount(email: string, password: string, displayName: string): Promise<AuthResult> {
  return callSession("/session/register", { email, password, displayName });
}

export async function logout(): Promise<void> {
  await fetch("/session/logout", { method: "POST" });
  accessToken = null;
}

async function describeProblem(response: Response): Promise<string> {
  try {
    const problem = (await response.json()) as { errors?: Record<string, string[]>; title?: string };
    if (problem.errors) {
      return Object.values(problem.errors).flat().join(" ");
    }
    return problem.title ?? "Something went wrong.";
  } catch {
    return response.status === 401 ? "Wrong email or password." : "Something went wrong.";
  }
}
