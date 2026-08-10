#!/usr/bin/env node
// Regenerates each app's favicon.ico from its own hand-edited favicon.svg
// (a dedicated favicon source, separate from the in-app gbp-logo.svg) --
// run after editing either SVG instead of re-exporting the .ico by hand.
import { readFile, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import sharp from 'sharp';
import pngToIco from 'png-to-ico';

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const projectsDir = path.resolve(__dirname, '..', 'projects');

const APPS = ['admin-client', 'player-client'];
// A multi-resolution .ico lets the OS/browser pick the sharpest size for
// wherever it's rendering (browser tab, bookmark, taskbar) instead of
// scaling one bitmap up or down.
const SIZES = [16, 32, 48, 64];

async function generateFavicon(app) {
  const svgPath = path.join(projectsDir, app, 'public', 'favicon.svg');
  const icoPath = path.join(projectsDir, app, 'public', 'favicon.ico');

  const svg = await readFile(svgPath);
  const pngBuffers = await Promise.all(
    SIZES.map((size) =>
      sharp(svg, { density: 384 })
        .resize(size, size)
        .png()
        .toBuffer(),
    ),
  );

  const ico = await pngToIco(pngBuffers);
  await writeFile(icoPath, ico);
  console.log(`Generated ${path.relative(projectsDir, icoPath)}`);
}

for (const app of APPS) {
  await generateFavicon(app);
}
