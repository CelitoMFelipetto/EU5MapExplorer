/**
 * Leaflet custom pane names and their z-indices.
 *
 * Leaflet's built-in overlayPane sits at z-index 400.
 * All custom panes live just above it so the SVG tile layer stays below,
 * and each layer is drawn on top of the previous one.
 *
 *   400 — overlayPane   (built-in, SVG tile layer)
 *   401 — locationsPane (filled location polygons)
 *   402 — provincesPane (province boundary outlines)
 *   403 — cityIconsPane (city rank icons at city positions)
 */
export const MAP_PANES = {
  locations:        { name: 'locationsPane',  zIndex: 401 },
  provinceOutlines: { name: 'provincesPane',  zIndex: 402 },
  cityIcons:        { name: 'cityIconsPane',  zIndex: 403 },
} as const;
