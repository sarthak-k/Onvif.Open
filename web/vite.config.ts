/// <reference types="vitest/config" />
import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// In development the recorder runs on http://localhost:5080 and the dev server proxies /api to it.
// A deployed app is a static site: public/config.json tells it where the recorder is.
export default defineConfig({
  plugins: [react()],
  build: {
    // hls.js alone is about 590 kB; it is loaded only by the live view.
    chunkSizeWarningLimit: 700,
    rollupOptions: {
      output: {
        // Libraries change less often than the app: their own chunk stays cached across app releases.
        // hls.js (about 600 kB) is only fetched by the live view.
        manualChunks: (id) => (id.includes('node_modules/hls.js') ? 'hls' : id.includes('node_modules') ? 'vendor' : undefined),
      },
    },
  },
  server: {
    port: 5173,
    proxy: { '/api': process.env.RECORDER_URL ?? 'http://localhost:5080' },
  },
  test: {
    environment: 'jsdom',
    globals: true,
    setupFiles: ['./src/test/setup.ts'],
    include: ['src/**/*.test.{ts,tsx}'],
  },
});
