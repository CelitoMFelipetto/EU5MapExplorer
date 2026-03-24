export type MapMode = 'locations' | 'topography' | 'climate' | 'vegetation' | 'raw_material';

export const MAP_MODES: { id: MapMode; label: string }[] = [
  { id: 'locations', label: 'Locations' },
  { id: 'topography', label: 'Topography' },
  { id: 'climate', label: 'Climate' },
  { id: 'vegetation', label: 'Vegetation' },
  { id: 'raw_material', label: 'Raw Material' },
];

/** Fallback colour used when a metadata value has no entry in a legend. */
export const LEGEND_DEFAULT_COLOR = '#cccccc';

/**
 * Colour legends for each metadata mode.
 * Values are placeholders — override them once the canonical game values are known.
 */
export const COLOR_LEGENDS: Record<Exclude<MapMode, 'locations'>, Record<string, string>> = {
  topography: {
    flatland: 'rgb(90 235 27)',
    plains: '#c8e6a0',
    plateau: '#691804',
    hills: '#b08415',
    mountains: '#691804',
    lakes: '#33ccff',
    wetlands: '#49a679',
  },
  climate: {
    arctic: '#ddeeff',
    subarctic: '#a8c8e8',
    continental: '#c8e0a0',
    oceanic: '#b8d8c0',
    humid_continental: '#90c878',
    mediterranean: '#f0d878',
    semi_arid: '#e8c890',
  },
  vegetation: {
    desert: 'rgb( 242 242 111 )',
    sparse: 'rgb( 147 200 83 )',
    grasslands: 'rgb( 90 235 27 )',
    farmland: 'rgb( 0 255 0 )',
    woods: 'rgb( 41 155 22 )',
    forest: 'rgb( 18 74 9 )',
    jungle: 'rgb( 8 41 3 )',
  },
  raw_material: {
    // TODO: fill this up
    iron: '#888898',
    copper: '#c87830',
    fish: '#4090c8',
    grain: '#e8c840',
    timber: '#8a5a2a',
    fur: '#a87840',
    salt: '#e8e4c0',
    gold: '#ffd700',
    silver: '#c0c0c0',
    none: '#e0dcd0',
  },
};
