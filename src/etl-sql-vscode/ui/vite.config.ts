import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import { viteSingleFile } from "vite-plugin-singlefile"
import tailwindcss from '@tailwindcss/vite'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react(), tailwindcss(), viteSingleFile({ useRecommendedBuildConfig: false })],
  build: {
    assetsInlineLimit: 100000000,
    cssCodeSplit: false,
    assetsDir: "",
    rollupOptions: {
      output: {
        // Vite 8+ deprecation fix: 
        // @ts-ignore
        codeSplitting: false
      }
    }
  },
  base: "./",
})
