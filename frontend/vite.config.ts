import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import { resolve } from 'path';

export default defineConfig({
  plugins: [react()],
  base: './',
  define: {
    'process.env.NODE_ENV': JSON.stringify(process.env.NODE_ENV || 'production')
  },
  build: {
    outDir: resolve(__dirname, '../Web/dist'),
    emptyOutDir: true,
    sourcemap: false,
    cssCodeSplit: false,
    modulePreload: false,
    rollupOptions: {
      input: {
        config: resolve(__dirname, 'src/config/main.tsx'),
        injection: resolve(__dirname, 'src/injection/main.tsx'),
        player: resolve(__dirname, 'src/player/main.tsx')
      },
      output: {
        entryFileNames: 'jellyemu.[name].bundle.js',
        chunkFileNames: 'chunks/jellyemu.[name].js',
        assetFileNames: (assetInfo) => {
          const name = assetInfo.name || (assetInfo.names && assetInfo.names[0]) || '';
          if (name.endsWith('.css')) {
            return 'jellyemu.index.bundle.css';
          }
          return 'assets/[name].[ext]';
        }
      }
    }
  }
});
