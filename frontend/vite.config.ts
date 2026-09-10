/// <reference types="vitest/config" />
import { defineConfig, type UserConfig } from 'vite';
import type { InlineConfig } from 'vitest/node';
import react from '@vitejs/plugin-react';

interface VitestConfig extends UserConfig {
  test?: InlineConfig;
}

export default defineConfig({
  plugins: [react()],
  server: {
    host: '0.0.0.0',
    port: 3000,
    allowedHosts: true,
    proxy: {
      '/api': {
        target: process.env.BACKEND_INTERNAL_URL || 'http://backend:5000',
        changeOrigin: true,
      },
      '/hubs': {
        target: process.env.BACKEND_INTERNAL_URL || 'http://backend:5000',
        ws: true,
        changeOrigin: true,
      },
    },
    watch: {
      usePolling: true,
    },
  },
  test: {
    globals: true,
    environment: 'jsdom',
    setupFiles: './src/tests/setup.ts',
  },
} as VitestConfig);
