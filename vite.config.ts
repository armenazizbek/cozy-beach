import { defineConfig } from 'vite';

// relative asset paths so the built game also works when opened from disk (the desktop app)
export default defineConfig({ base: './' });
