import { createAuthClient } from "@neondatabase/neon-js/auth";

// Official Neon Auth client.
// It uses the hosted Neon Auth service defined by PUBLIC_NEON_AUTH_URL.
export const authClient = createAuthClient(import.meta.env.PUBLIC_NEON_AUTH_URL);
