import type { APIRoute } from "astro";

const apiBaseUrl = import.meta.env.PUBLIC_DRACO_API_URL || "http://localhost:5020";

export const POST: APIRoute = async ({ request }) => {
  const cookie = request.headers.get("cookie");
  const requestBody = await request.json().catch(() => ({}));

  const response = await fetch(`${apiBaseUrl}/api/auth/neon/exchange`, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
      ...(cookie ? { Cookie: cookie } : {}),
    },
    body: JSON.stringify(requestBody),
  });

  const body = await response.text();
  return new Response(body, {
    status: response.status,
    headers: {
      "content-type": response.headers.get("content-type") || "application/json",
    },
  });
};
