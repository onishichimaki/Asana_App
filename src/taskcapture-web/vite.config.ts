import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  build: {
    outDir: '../TaskCapture.Api/wwwroot',
    emptyOutDir: true,
  },
  server: {
    host: true,
    port: 5173,
    proxy: {
      '/api': 'http://localhost:5080',
    },
  },
})
