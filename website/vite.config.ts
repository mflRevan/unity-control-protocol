import path from 'path';
import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import tailwindcss from '@tailwindcss/vite';

export default defineConfig({
  plugins: [react(), tailwindcss()],
  resolve: {
    alias: {
      '@': path.resolve(__dirname, './src'),
      '@docs': path.resolve(__dirname, '../docs'),
      '@skills': path.resolve(__dirname, '../skills'),
    },
  },
  server: {
    fs: {
      allow: ['..'],
    },
  },
  base: '/',
});
