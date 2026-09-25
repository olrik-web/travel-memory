import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import { VitePWA } from 'vite-plugin-pwa';

// https://vite.dev/config/
export default defineConfig({
  plugins: [
    react(),
    VitePWA({
      registerType: 'autoUpdate',
      includeAssets: ['icons/travel-memory-192.png', 'icons/travel-memory-512.png'],
      manifest: {
        name: 'Travel Memory',
        short_name: 'Travel Memory',
        description: 'Your trips kept as personal memories.',
        start_url: '/',
        display: 'standalone',
        background_color: '#f7f2e8',
        theme_color: '#ad4f2c',
        lang: 'en',
        icons: [
          {
            src: '/icons/travel-memory-192.png',
            sizes: '192x192',
            type: 'image/png',
          },
          {
            src: '/icons/travel-memory-512.png',
            sizes: '512x512',
            type: 'image/png',
          },
          {
            src: '/icons/travel-memory-512.png',
            sizes: '512x512',
            type: 'image/png',
            purpose: 'maskable',
          },
        ],
      },
      workbox: {
        navigateFallbackDenylist: [/^\/api\//],
      },
    }),
  ],
  server: {
    proxy: {
      // Proxy API calls to the app service
      '/api': {
        target: process.env.API_HTTPS || process.env.API_HTTP,
        changeOrigin: true,
      },
    },
  },
});
