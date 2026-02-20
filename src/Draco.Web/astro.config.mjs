import { defineConfig } from 'astro/config';
import vercel from '@astrojs/vercel';

import vue from '@astrojs/vue';

import tailwindcss from '@tailwindcss/vite';

// https://astro.build/config
export default defineConfig({
  output: 'server',
  adapter: vercel({}),
  integrations: [vue()],

  vite: {
    plugins: [tailwindcss()],
  },
});