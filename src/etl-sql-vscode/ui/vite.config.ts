import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import { singleFileWebview } from './singleFileWebview'
import tailwindcss from '@tailwindcss/vite'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react(), tailwindcss(), singleFileWebview()],
  build: {
    assetsInlineLimit: 100000000,
    cssCodeSplit: false,
    assetsDir: "",
    rollupOptions: {
      output: {
        codeSplitting: false
      }
    }
  },
  base: "./",
})
