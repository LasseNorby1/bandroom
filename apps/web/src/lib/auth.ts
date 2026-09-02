/// Access-token handling (spec §6.2): memory only, never storage. A silent
/// refresh bootstraps it; refreshes are single-flight because two concurrent
/// refreshes would present the same rotating token twice — which the api treats
/// as theft and answers by revoking the whole family.
let accessToken: string | null = null;
let refreshInFlight: Promise<string | null> | null = null;

export function getAccessToken(): string | null {
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
      accessToken = tokens.accessToken;
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
  accessToken = tokens.accessToken;
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
