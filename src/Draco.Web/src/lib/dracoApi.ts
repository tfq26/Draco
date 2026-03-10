import { authClient } from "./auth";
const apiBaseUrl = import.meta.env.PUBLIC_DRACO_API_URL || "http://localhost:5020";

let cachedApiToken: string | null = null;

export const getDracoApiBaseUrl = () => apiBaseUrl;

export async function getDracoApiToken(forceRefresh = false): Promise<string> {
  if (!forceRefresh && cachedApiToken) {
    return cachedApiToken;
  }

  const sessionResult = await authClient.getSession();
  const sessionId = sessionResult.data?.session?.id;
  const sessionToken = sessionResult.data?.session?.token;
  const user = sessionResult.data?.user;
  console.log("[DEBUG] dracoApi session data found:", {
    hasId: !!sessionId,
    hasToken: !!sessionToken,
    hasUser: !!user,
    imageUrl: (user as any)?.image || (user as any)?.picture
  });

  const response = await fetch("/api/draco/token", {
    method: "POST",
    headers: {
      "Content-Type": "application/json"
    },
    body: JSON.stringify({ sessionId, sessionToken, user }),
    credentials: "include",
  });

  if (!response.ok) {
    throw new Error("Failed to exchange Neon session for API token.");
  }

  const data = await response.json();
  const token = data?.token;
  if (!token || typeof token !== "string") {
    throw new Error("API token missing in exchange response.");
  }

  cachedApiToken = token;
  return token;
}

export async function dracoApiFetch(path: string, init: RequestInit = {}): Promise<Response> {
  const token = await getDracoApiToken();
  const headers = new Headers(init.headers || {});
  headers.set("Authorization", `Bearer ${token}`);

  return fetch(`${apiBaseUrl}${path}`, {
    ...init,
    headers,
  });
}
