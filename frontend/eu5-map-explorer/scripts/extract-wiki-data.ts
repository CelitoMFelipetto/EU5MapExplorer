/**
 * extract-wiki-data.ts
 *
 * 1. Reads authoritative good colours from the EU5 game file
 *    (named_colors/02_map.txt) using jomini — the canonical PDXScript parser
 * 2. Scrapes the EU5 wiki Goods page for good names and icon URLs,
 *    then downloads each icon into public/goods/<snake_name>.png
 * 3. Rewrites the raw_material block in src/app/map/map-mode.ts with
 *    the game colours
 *
 * Run from the frontend/eu5-map-explorer directory:
 *   npm run extract-wiki-data
 */

import { Jomini } from 'jomini';
import { JSDOM } from 'jsdom';
import { promises as fs } from 'fs';
import path from 'path';
import { fileURLToPath } from 'url';

// ── Configuration ─────────────────────────────────────────────────────────────

const WIKI_GOODS_URL  = 'https://eu5.paradoxwikis.com/Goods';
const GAME_COLORS_FILE =
  'C:/Program Files (x86)/Steam/steamapps/common/Europa Universalis V' +
  '/game/main_menu/common/named_colors/02_map.txt';

// Page table indices on the wiki: 0=info banner, 1=Raw goods, 2=Food, 3=Produced goods
const GOODS_TABLE_INDICES = new Set([1, 2, 3]);

// Wiki snake_name → game file key suffix (after stripping "goods_")
// Only needed where the two names differ.
const GAME_KEY_ALIASES: Record<string, string> = {
  cannon:       'cannons',
  potatoes:     'potato',
};

// ── Types ─────────────────────────────────────────────────────────────────────

interface Good {
  name:      string;
  snakeName: string;
  color:     string;   // hex — from game file, falls back to #cccccc
  iconUrl:   string;   // full-size PNG on wiki CDN
}

// ── Color conversion ──────────────────────────────────────────────────────────

/**
 * rgb values can be either 0-255 integers or 0.0-1.0 floats.
 * If every value is ≤ 1 we treat them as normalised floats.
 */
function rgbToHex(values: number[]): string {
  const isNorm = values.every(v => v <= 1.0);
  const [r, g, b] = values.map(v => {
    const byte = isNorm ? Math.round(v * 255) : Math.round(v);
    return Math.max(0, Math.min(255, byte));
  });
  return '#' + [r, g, b].map(n => n.toString(16).padStart(2, '0')).join('');
}

/** Standard HSV → RGB; h/s/v all in the 0–1 range. */
function hsvToRgbBytes(h: number, s: number, v: number): [number, number, number] {
  const i = Math.floor(h * 6);
  const f = h * 6 - i;
  const p = v * (1 - s);
  const q = v * (1 - f * s);
  const t = v * (1 - (1 - f) * s);
  const cases: Array<[number, number, number]> = [
    [v, t, p], [q, v, p], [p, v, t],
    [p, q, v], [t, p, v], [v, p, q],
  ];
  return cases[i % 6].map(c => Math.round(c * 255)) as [number, number, number];
}

/** hsv { h s v } — all values are 0-1 floats. */
function hsvNormToHex(values: number[]): string {
  const [r, g, b] = hsvToRgbBytes(values[0], values[1], values[2]);
  return '#' + [r, g, b].map(n => n.toString(16).padStart(2, '0')).join('');
}

/** hsv360 { h s v } — h is 0-360, s is 0-100, v is 0-100. */
function hsv360ToHex(values: number[]): string {
  return hsvNormToHex([values[0] / 360, values[1] / 100, values[2] / 100]);
}

function colorEntryToHex(entry: unknown): string | null {
  if (!entry || typeof entry !== 'object') return null;
  const e = entry as Record<string, unknown>;
  if (Array.isArray(e['rgb']))    return rgbToHex(e['rgb']     as number[]);
  if (Array.isArray(e['hsv']))    return hsvNormToHex(e['hsv']  as number[]);
  if (Array.isArray(e['hsv360'])) return hsv360ToHex(e['hsv360'] as number[]);
  return null;
}

// ── Game colors ───────────────────────────────────────────────────────────────

async function loadGameColors(parser: Jomini): Promise<Map<string, string>> {
  const buf  = await fs.readFile(GAME_COLORS_FILE);
  const data = parser.parseText(buf) as { colors?: Record<string, unknown> };

  const map = new Map<string, string>();
  for (const [key, value] of Object.entries(data.colors ?? {})) {
    if (!key.startsWith('goods_')) continue;
    const snakeName = key.slice('goods_'.length); // strip the goods_ prefix
    const hex = colorEntryToHex(value);
    if (hex) map.set(snakeName, hex);
  }
  return map;
}

// ── Wiki extraction ───────────────────────────────────────────────────────────

function toSnakeCase(name: string): string {
  return name.trim().toLowerCase().replace(/[\s-]+/g, '_').replace(/[^a-z0-9_]/g, '');
}

function extractGoodsFromWiki(
  html: string,
): Array<{ name: string; snakeName: string; iconUrl: string }> {
  const { document } = new JSDOM(html).window;
  const goods: Array<{ name: string; snakeName: string; iconUrl: string }> = [];

  document.querySelectorAll('table').forEach((table, ti) => {
    if (!GOODS_TABLE_INDICES.has(ti)) return;

    table.querySelectorAll('tr').forEach(row => {
      const img       = row.querySelector('td img') as HTMLImageElement | null;
      const firstCell = row.querySelector('td');
      if (!img || !firstCell) return;

      // Each good has a <div style="font-weight:bold; ... color:rgb(...);">Name</div>
      const nameDiv = Array.from(firstCell.querySelectorAll('div')).find(
        d => d.getAttribute('style')?.includes('font-weight'),
      );
      if (!nameDiv) return;

      const name = nameDiv.textContent?.trim() ?? '';
      if (!name) return;

      // Thumb URL: /images/thumb/<hash>/<file>/64px-<file>
      // Full-size: /images/<hash>/<file>
      const thumbSrc    = img.getAttribute('src') ?? '';
      const absoluteSrc = thumbSrc.startsWith('http')
        ? thumbSrc
        : `https://eu5.paradoxwikis.com${thumbSrc}`;
      const iconUrl = absoluteSrc
        .replace('/thumb/', '/')
        .replace(/\/\d+px-[^/]+$/, '');

      goods.push({ name, snakeName: toSnakeCase(name), iconUrl });
    });
  });

  return goods;
}

// ── Icon download ─────────────────────────────────────────────────────────────

async function downloadIcon(url: string, dest: string): Promise<void> {
  const res = await fetch(url);
  if (!res.ok) throw new Error(`HTTP ${res.status}`);
  await fs.writeFile(dest, Buffer.from(await res.arrayBuffer()));
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
  const feRoot     = path.resolve(path.dirname(__filename), '..');

  const publicGoodsDir = path.join(feRoot, 'public', 'goods');
  const mapModePath    = path.join(feRoot, 'src', 'app', 'map', 'map-mode.ts');

  await fs.mkdir(publicGoodsDir, { recursive: true });

  // ── Step 1: load game colors via jomini ───────────────────────────────────
  console.log('Initialising jomini parser...');
  const parser = await Jomini.initialize();

  console.log(`Reading game colors:\n  ${GAME_COLORS_FILE}`);
  const gameColors = await loadGameColors(parser);
  console.log(`  → ${gameColors.size} goods_* entries loaded.\n`);

  // ── Step 2: fetch + parse wiki ────────────────────────────────────────────
  console.log(`Fetching ${WIKI_GOODS_URL}...`);
  const html      = await fetch(WIKI_GOODS_URL).then(r => r.text());
  const wikiGoods = extractGoodsFromWiki(html);
  console.log(`  → ${wikiGoods.length} goods found on wiki.\n`);

  // ── Step 3: merge ─────────────────────────────────────────────────────────
  const goods: Good[] = wikiGoods.map(g => {
    // Try direct match first, then any known alias.
    const gameKey = GAME_KEY_ALIASES[g.snakeName] ?? g.snakeName;
    const color   = gameColors.get(gameKey);
    if (!color) {
      console.warn(`  [WARN] No game color for '${g.name}' (tried: goods_${gameKey})`);
    }
    return { ...g, color: color ?? '#cccccc' };
  });

  // ── Step 4: download icons in parallel ────────────────────────────────────
  console.log('Downloading icons...');
  const results = await Promise.allSettled(
    goods.map(g => downloadIcon(g.iconUrl, path.join(publicGoodsDir, `${g.snakeName}.png`))),
  );
  results.forEach((r, i) => {
    if (r.status === 'fulfilled') {
      console.log(`  ✓ ${goods[i].name.padEnd(22)} → public/goods/${goods[i].snakeName}.png`);
    } else {
      console.warn(`  ✗ ${goods[i].name.padEnd(22)}: ${(r.reason as Error).message}`);
    }
  });

  // ── Step 5: patch map-mode.ts ─────────────────────────────────────────────
  console.log('\nPatching src/app/map/map-mode.ts...');
  await patchMapMode(goods, mapModePath);
  console.log('  ✓ raw_material legend updated.');

  console.log('\nDone.');
}

main().catch(err => {
  console.error(err);
  process.exit(1);
});
