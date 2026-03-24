/**
 * Leaflet custom pane names and their z-indices.
 *
 * Leaflet's built-in overlayPane sits at z-index 400.
 * Both custom panes live just above it so the SVG tile layer stays below,
 * and the province outline layer is always drawn on top of the locations layer.
 *
 *   400 — overlayPane (built-in, SVG tile layer)
 *   401 — locationsPane   (filled location polygons)
 *   402 — provincesPane   (province boundary outlines)
 */
export const MAP_PANES = {
  locations:        { name: 'locationsPane',  zIndex: 401 },
  provinceOutlines: { name: 'provincesPane',  zIndex: 402 },
} as const;
