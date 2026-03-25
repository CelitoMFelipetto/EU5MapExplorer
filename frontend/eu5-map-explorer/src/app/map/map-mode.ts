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
    alum: '#7b322d',  // Alum
    amber: '#ffbf00',  // Amber
    chili: '#bf3519',  // Chili
    clay: '#be4646',  // Clay
    cloves: '#35bf19',  // Cloves
    coal: '#1f1f1f',  // Coal
    cocoa: '#733617',  // Cocoa
    coffee: '#382617',  // Coffee
    copper: '#d98257',  // Copper
    cotton: '#85ad99',  // Cotton
    dyes: '#a12b80',  // Dyes
    elephants: '#d26a2f',  // Elephants
    fiber_crops: '#0f4d20',  // Fiber Crops
    gems: '#00ba76',  // Gems
    gold: '#ffd630',  // Gold
    horses: '#9f8170',  // Horses
    incense: '#e3c978',  // Incense
    iron: '#333333',  // Iron
    ivory: '#bfb3a1',  // Ivory
    lead: '#322ee6',  // Lead
    lumber: '#737817',  // Lumber
    marble: '#f7e6f7',  // Marble
    medicaments: '#ff7f7f',  // Medicaments
    mercury: '#e03b50',  // Mercury
    pearls: '#eae0c8',  // Pearls
    pepper: '#adc0c0',  // Pepper
    saffron: '#bf571b',  // Saffron
    salt: '#ffffff',  // Salt
    saltpeter: '#964b00',  // Saltpeter
    sand: '#f2f26f',  // Sand
    silk: '#b81a1a',  // Silk
    silver: '#c0c0c0',  // Silver
    stone: '#3d434d',  // Stone
    sugar: '#bdf2ad',  // Sugar
    tea: '#125417',  // Tea
    tin: '#4d4d4d',  // Tin
    tobacco: '#548f61',  // Tobacco
    wine: '#5c2147',  // Wine
    beeswax: '#cc8c25',  // Beeswax
    fish: '#317874',  // Fish
    fruit: '#f23f3f',  // Fruit
    fur: '#8a664f',  // Fur
    legumes: '#2d967d',  // Legumes
    livestock: '#60940a',  // Livestock
    maize: '#cc8f14',  // Maize
    olives: '#486600',  // Olives
    potatoes: '#ffae6a',  // Potatoes
    rice: '#64804e',  // Rice
    sturdy_grains: '#cccccc',  // Sturdy Grains
    wheat: '#d4cc13',  // Wheat
    wild_game: '#ded15d',  // Wild Game
    wool: '#8a9999',  // Wool
    beer: '#160b40',  // Beer
    books: '#a11f65',  // Books
    cannon: '#de99ff',  // Cannon
    cloth: '#db3030',  // Cloth
    fine_cloth: '#b7a99b',  // Fine Cloth
    firearms: '#ffb3ea',  // Firearms
    furniture: '#82c0c0',  // Furniture
    glass: '#009973',  // Glass
    jewelry: '#ff880d',  // Jewelry
    lacquerware: '#980018',  // Lacquerware
    leather: '#261d19',  // Leather
    liquor: '#330a23',  // Liquor
    masonry: '#402613',  // Masonry
    naval_supplies: '#1c2b66',  // Naval Supplies
    paper: '#e6dbb5',  // Paper
    porcelain: '#2e91cc',  // Porcelain
    pottery: '#a0c0c0',  // Pottery
    slaves: '#000000',  // Slaves
    steel: '#242a33',  // Steel
    tar: '#292929',  // Tar
    tools: '#0e1014',  // Tools
    weaponry: '#caccce',  // Weaponry
  },
};
