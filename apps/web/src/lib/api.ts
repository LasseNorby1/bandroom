import createClient, { type Middleware } from "openapi-fetch";
import type { paths } from "@bandroom/client";
import { getAccessToken, refreshSession } from "./auth";

const baseUrl = process.env.NEXT_PUBLIC_API_URL ?? "/api";

export const api = createClient<paths>({ baseUrl });

const auth: Middleware = {
  async onRequest({ request }) {
    const token = getAccessToken() ?? (await refreshSession());
    if (token) {
      request.headers.set("authorization", `Bearer ${token}`);
    }
    return request;
  },
  async onResponse({ request, response }) {
    if (response.status !== 401) {
      return response;
    }

    const token = await refreshSession();
    if (!token) {
      window.location.assign("/login");
      return response;
    }

    // Bodyless methods retry safely; a mid-flight expiry on a mutation is rare
    // enough to surface as an error instead of replaying a consumed body.
    if (request.method === "GET" || request.method === "DELETE") {
      const retry = new Request(request.url, { method: request.method, headers: request.headers });
      retry.headers.set("authorization", `Bearer ${token}`);
      return fetch(retry);
    }

    return response;
  },
};

api.use(auth);
