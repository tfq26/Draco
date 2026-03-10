import { defineConfig } from 'astro/config';
import node from '@astrojs/node';
import vercel from '@astrojs/vercel';
import vue from '@astrojs/vue';
import tailwindcss from '@tailwindcss/vite';

// Robust environment detection for Vercel vs. other targets (Railway/Local)
const isVercel = !!(process.env.VERCEL || process.env.VERCEL_URL || process.env.VITE_VERCEL);

// https://astro.build/config
export default defineConfig({
  output: 'server',
  adapter: isVercel ? vercel() : node({ mode: 'standalone' }),
  integrations: [vue()],

  vite: {
    plugins: [tailwindcss()],
  },
});
