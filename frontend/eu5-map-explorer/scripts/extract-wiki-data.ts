/**
 * extract-wiki-data.ts
 *
 * Scrapes the EU5 wiki Goods page and:
 *   1. Downloads each good's icon into public/goods/<snake_name>.png
 *   2. Rewrites the raw_material block in src/app/map/map-mode.ts with
 *      the colours extracted from the wiki (text colour of each goods name element).
 *
 * Run from the frontend/eu5-map-explorer directory:
 *   npm run extract-wiki-data
 */

import { JSDOM } from 'jsdom';
import { promises as fs } from 'fs';
import path from 'path';
import { fileURLToPath } from 'url';

const WIKI_GOODS_URL = 'https://eu5.paradoxwikis.com/Goods';

// Page table indices: 0=info banner, 1=Raw goods, 2=Food, 3=Produced goods
const GOODS_TABLE_INDICES = new Set([1, 2, 3]);

// ── Helpers ───────────────────────────────────────────────────────────────────

function toSnakeCase(name: string): string {
  return name.trim().toLowerCase().replace(/[\s-]+/g, '_').replace(/[^a-z0-9_]/g, '');
}

function rgbToHex(rgb: string): string {
  const m = rgb.match(/rgb\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*\)/);
  if (!m) return '#cccccc';
  return '#' + [m[1], m[2], m[3]].map(n => parseInt(n).toString(16).padStart(2, '0')).join('');
}

function toHex(color: string): string {
  const c = color.replace(/\s/g, '');
  if (c.startsWith('rgb')) return rgbToHex(c);
  return c.startsWith('#') ? c : `#${c}`;
}

// ── Types ─────────────────────────────────────────────────────────────────────

interface Good {
  name: string;
  snakeName: string;
  color: string;   // hex, taken from the wiki name element's CSS color property
  iconUrl: string; // full-size PNG on the wiki CDN
}

// ── Extraction ────────────────────────────────────────────────────────────────

function extractGoods(html: string): Good[] {
  const { document } = new JSDOM(html).window;
  const goods: Good[] = [];

  document.querySelectorAll('table').forEach((table, ti) => {
    if (!GOODS_TABLE_INDICES.has(ti)) return;

    table.querySelectorAll('tr').forEach(row => {
      const img = row.querySelector('td img') as HTMLImageElement | null;
      if (!img) return;

      const firstCell = row.querySelector('td');
      if (!firstCell) return;

      // Each good has a <div style="font-weight:bold; ... color:rgb(...);">Name</div>
      const nameDiv = Array.from(firstCell.querySelectorAll('div')).find(
        d => d.getAttribute('style')?.includes('font-weight'),
      );
      if (!nameDiv) return;

      const name = nameDiv.textContent?.trim() ?? '';
      if (!name) return;

      const styleAttr = nameDiv.getAttribute('style') ?? '';
      const colorMatch = styleAttr.match(/color:\s*(rgb\([^)]+\)|#[0-9a-fA-F]+)/);
      const color = colorMatch ? toHex(colorMatch[1]) : '#cccccc';

      // Thumb URL format: /images/thumb/<hash>/<file>/64px-<file>
      // Full-size format: /images/<hash>/<file>
      const thumbSrc = img.getAttribute('src') ?? '';
      const absoluteSrc = thumbSrc.startsWith('http')
        ? thumbSrc
        : `https://eu5.paradoxwikis.com${thumbSrc}`;
      const iconUrl = absoluteSrc
        .replace('/thumb/', '/')
        .replace(/\/\d+px-[^/]+$/, '');

      goods.push({ name, snakeName: toSnakeCase(name), color, iconUrl });
    });
  });

  return goods;
}

// ── Icon download ─────────────────────────────────────────────────────────────

async function downloadIcon(url: string, dest: string): Promise<void> {
  const res = await fetch(url);
  if (!res.ok) throw new Error(`HTTP ${res.status} fetching ${url}`);
  const buf = await res.arrayBuffer();
  await fs.writeFile(dest, Buffer.from(buf));
}

// ── map-mode.ts patch ─────────────────────────────────────────────────────────

async function patchMapMode(goods: Good[], mapModePath: string): Promise<void> {
  const src = await fs.readFile(mapModePath, 'utf-8');

  const entries = goods
    .map(g => `    ${g.snakeName}: '${g.color}',  // ${g.name}`)
    .join('\n');

  const patched = src.replace(
    /raw_material:\s*\{[^}]*\}/s,
    `raw_material: {\n${entries}\n  }`,
  );

  if (patched === src) {
    console.warn('  ⚠ raw_material block not found in map-mode.ts — nothing patched.');
    return;
  }

  await fs.writeFile(mapModePath, patched, 'utf-8');
}

// ── Main ──────────────────────────────────────────────────────────────────────

async function main(): Promise<void> {
  const __filename = fileURLToPath(import.meta.url);
  const feRoot = path.resolve(path.dirname(__filename), '..');

  const publicGoodsDir = path.join(feRoot, 'public', 'goods');
  const mapModePath    = path.join(feRoot, 'src', 'app', 'map', 'map-mode.ts');

  await fs.mkdir(publicGoodsDir, { recursive: true });

  // ── Step 1: fetch + parse ─────────────────────────────────────────────────
  console.log(`Fetching ${WIKI_GOODS_URL}...`);
  const html = await fetch(WIKI_GOODS_URL).then(r => r.text());

  console.log('Extracting goods data...');
  const goods = extractGoods(html);
  console.log(`  Found ${goods.length} goods.\n`);

  // ── Step 2: download icons (all in parallel) ──────────────────────────────
  console.log('Downloading icons...');
  const iconResults = await Promise.allSettled(
    goods.map(g => downloadIcon(g.iconUrl, path.join(publicGoodsDir, `${g.snakeName}.png`))),
  );

  iconResults.forEach((r, i) => {
    if (r.status === 'fulfilled') {
      console.log(`  ✓ ${goods[i].name.padEnd(20)} → public/goods/${goods[i].snakeName}.png`);
    } else {
      console.warn(`  ✗ ${goods[i].name.padEnd(20)}: ${(r.reason as Error).message}`);
    }
  });

  // ── Step 3: patch map-mode.ts ─────────────────────────────────────────────
  console.log('\nPatching src/app/map/map-mode.ts...');
  await patchMapMode(goods, mapModePath);
  console.log('  ✓ raw_material legend updated.');

  console.log('\nDone.');
}

main().catch(err => {
  console.error(err);
  process.exit(1);
});
