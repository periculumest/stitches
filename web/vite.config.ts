import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
export default defineConfig({ plugins: [react()], server: { proxy: { '/api': 'http://127.0.0.1:5057' } }, build: { outDir: '../server/wwwroot', emptyOutDir: true } });
