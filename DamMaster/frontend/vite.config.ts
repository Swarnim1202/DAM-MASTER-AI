import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// Dev: Vite on :5173 proxies /api to the ASP.NET Core API on :5080 (same-origin cookies, no CORS needed).
// Build: output goes to the API's wwwroot so one IIS / Kestrel site serves both.
export default defineConfig({
  plugins: [react()],
  server: { port: 5173, proxy: { '/api': { target: 'http://localhost:5080', changeOrigin: false } } },
  build: { outDir: '../backend/DamMaster.Api/wwwroot', emptyOutDir: true, chunkSizeWarningLimit: 1500 },
});
