import type { APIRoute } from "astro";

const apiBaseUrl = import.meta.env.PUBLIC_DRACO_API_URL || "http://localhost:5020";

export const POST: APIRoute = async ({ request }) => {
  const cookie = request.headers.get("cookie");
  const requestBody = await request.json().catch(() => ({}));

  console.log(`[AUTH PROXY] Exchanging session for Draco API token at: ${apiBaseUrl}`);
  console.log(`[AUTH PROXY] Cookies present: ${!!cookie}`);

  try {
    const response = await fetch(`${apiBaseUrl}/api/auth/neon/exchange`, {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        ...(cookie ? { Cookie: cookie } : {}),
      },
      body: JSON.stringify(requestBody),
    });

    console.log(`[AUTH PROXY] Backend responded with status: ${response.status}`);

    const body = await response.text();
    
    // If it's a 404, it might mean the backend is unreachable or the endpoint moved
    if (response.status === 404) {
      console.error(`[AUTH PROXY] 404 Error: Could not find exchange endpoint at ${apiBaseUrl}. Ensure PUBLIC_DRACO_API_URL is correct.`);
    }

    return new Response(body, {
      status: response.status,
      headers: {
        "content-type": response.headers.get("content-type") || "application/json",
      },
    });
  } catch (error: any) {
    console.error("[AUTH PROXY] Network error during token exchange:", error.message);
    return new Response(JSON.stringify({ 
      error: "Connection to API failed", 
      message: error.message,
      url: apiBaseUrl 
    }), {
      status: 502,
      headers: { "content-type": "application/json" }
    });
  }
};
