import { createAuthClient } from "better-auth/client";

export const authClient = createAuthClient({
    baseURL: import.meta.env.PUBLIC_NEON_AUTH_URL || "http://localhost:3000", // Will be configured in .env
});

export const { signIn, signUp, signOut, useSession } = authClient;
