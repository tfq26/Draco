import { authClient } from "../../../lib/auth";
import type { APIRoute } from "astro";

export const ALL: APIRoute = async ({ request }) => {
  return authClient.handler(request);
};
