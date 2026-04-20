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
  server: {
    proxy: {
      '/api': {
        target: 'https://localhost:7071',
        changeOrigin: true,
        secure: false,
      },
      '/hubs': {
        target: 'https://localhost:7071',
        changeOrigin: true,
        secure: false,
        ws: true,
      },
      '/files': {
        target: 'https://localhost:7071',
        changeOrigin: true,
        secure: false,
      },
      '/register': {
        target: 'https://localhost:7071',
        changeOrigin: true,
        secure: false,
      },
      '/login': {
        target: 'https://localhost:7071',
        changeOrigin: true,
        secure: false,
      },
      '/refresh': {
        target: 'https://localhost:7071',
        changeOrigin: true,
        secure: false,
      },
      '/forgotPassword': {
        target: 'https://localhost:7071',
        changeOrigin: true,
        secure: false,
      },
      '/resetPassword': {
        target: 'https://localhost:7071',
        changeOrigin: true,
        secure: false,
      },
    },
  },
})
