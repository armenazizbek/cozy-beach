import { readFileSync } from 'node:fs';
import { defineConfig } from 'vite';

const pkg = JSON.parse(readFileSync(new URL('./package.json', import.meta.url), 'utf-8')) as { version: string };

// relative asset paths so the built game also works when opened from disk (the desktop app)
export default defineConfig({
  base: './',
  define: { __APP_VERSION__: JSON.stringify(pkg.version), __BUILD_YEAR__: JSON.stringify(String(new Date().getFullYear())) },
  server: {
    watch: {
      ignored: ['**/release/**'],
    },
  },
});
