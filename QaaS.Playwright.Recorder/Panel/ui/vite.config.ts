import { copyFileSync } from 'node:fs';
import tailwindcss from '@tailwindcss/vite';
import { defineConfig, type Plugin } from 'vite';
import { viteSingleFile } from 'vite-plugin-singlefile';

// The built page goes beside the recorder's code, as Panel/panel.html, which the recorder embeds.
const embed = (): Plugin => ({
  name: 'qaas-embed',
  apply: 'build',
  closeBundle: () => copyFileSync('dist/panel.html', '../panel.html'),
});

// One self-contained HTML file, every script and style inline: the recorder shows it in a browser that may have no
// network.
export default defineConfig({
  plugins: [tailwindcss(), viteSingleFile({ removeViteModuleLoader: true }), embed()],
  server: { open: '/panel.html' },
  build: {
    target: 'chrome120',
    rollupOptions: { input: 'panel.html' },
  },
});
