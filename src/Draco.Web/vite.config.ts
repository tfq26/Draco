import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import path from 'path'

export default defineConfig({
  plugins: [react()],
  resolve: {
    alias: {
      '@': path.resolve(__dirname, './src'),
    },
  },
  build: {
    rollupOptions: {
      output: {
        manualChunks(id) {
          if (!id.includes('node_modules')) {
            return
          }

          if (id.includes('/react/') || id.includes('/react-dom/')) {
            return 'react-vendor'
          }

          if (
            id.includes('/@tanstack/react-router/') ||
            id.includes('/@tanstack/router-core/') ||
            id.includes('/@tanstack/react-query/') ||
            id.includes('/@tanstack/router-devtools/')
          ) {
            return 'tanstack-vendor'
          }

          if (id.includes('/@workos-inc/authkit-react/')) {
            return 'auth-vendor'
          }

          if (
            id.includes('/@radix-ui/') ||
            id.includes('/vaul/') ||
            id.includes('/lucide-react/') ||
            id.includes('/class-variance-authority/') ||
            id.includes('/clsx/') ||
            id.includes('/tailwind-merge/')
          ) {
            return 'ui-vendor'
          }
        },
      },
    },
  },
})
