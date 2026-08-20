import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

export default defineConfig({
  plugins: [react()],
  base: './', // Ensures relative paths for Electron production builds
  server: {
    port: 5173,
    strictPort: true, // Fail if port 5173 is already in use
  },
});