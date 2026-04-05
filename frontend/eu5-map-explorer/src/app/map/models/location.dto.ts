// ── Types consumed by LocationComponent / ProvinceComponent / MapComponent ────

export type PathCoordinates = [number, number];

export type PolygonPath = PathCoordinates[] | PathCoordinates[][] | PathCoordinates[][][];

export interface CityPosition {
  /** X coordinate in game world space (east-west). */
  x: number;
  /** Y coordinate in game world space (north-south; corresponds to Z in the 3D game file). */
  y: number;
}

export type LocationRank = 'city' | 'town' | 'rural_settlement';

export interface LocationDto {
  /** Location name (e.g. 'stockholm'). */
  id: string;
  /** Full hex colour string with '#' prefix (e.g. '#dda910'). */
  color: string;
  /** Location meta data */
  topography: string;
  climate: string;
  vegetation: string | null;
  raw_material: string | null;
  /**
   * Settlement rank.
   * 'city' and 'town' are explicitly set in the game files;
   * 'rural_settlement' is the default for anything not listed.
   */
  rank: LocationRank;
  /**
   * City placement position in game world space.
   * Null for locations that have no city object (e.g. lakes, wastelands).
   */
  city_position: CityPosition | null;
  /** Leaflet-ready polygon paths — coordinates already converted to [lat, lng]. */
  paths: PolygonPath;
  /** The province this location belongs to. */
  province: ProvinceDto;
}

export interface ProvinceBounds {
  maxN: number;
  maxS: number;
  maxE: number;
  maxW: number;
}

export interface ProvinceDto {
  /** Province name (e.g. 'uppland_province'). */
  id: string;
  /** Leaflet-ready province boundary paths — coordinates already converted to [lat, lng]. */
  paths: PolygonPath;
  /** Bounding box in pixel coordinates for future viewport culling. */
  bounds: ProvinceBounds;
  /** All locations that belong to this province. */
  locations: LocationDto[];
}

export interface MapDataDto {
  area: string;
  svgWidth: number;
  svgHeight: number;
  provinces: ProvinceDto[];
  neighborAreas: string[];
}

// ── Raw shapes returned by GET /api/map ───────────────────────────────────────

export interface ApiBorderRef {
  key: string;
  reversed: boolean;
  pathIndex: number;
}

export type ApiBorderRing = { borders: ApiBorderRef[] };

export interface ApiLocationDto {
  /** Location name as defined in definitions.txt (e.g. 'stockholm'). */
  name: string;
  /** 6-char hex colour without '#' (e.g. 'dda910'). */
  color: string;
  topography: string;
  climate: string;
  vegetation: string | null;
  raw_material: string | null;
  /** Settlement rank — defaults to 'rural_settlement' when absent from the game file. */
  rank: LocationRank;
  /**
   * City placement position in game world space.
   * Null for locations with no city object (e.g. lakes, wastelands).
   */
  city_position: CityPosition | null;
  /** Border rings referencing shared border segments. */
  borderRings: ApiBorderRing[];
}

export interface ApiProvinceDto {
  name: string;
  /** Border rings referencing shared border segments. */
  borderRings: ApiBorderRing[];
  /** Province bounding box in pixel coordinates. */
  bounds: ProvinceBounds;
  locations: ApiLocationDto[];
}

export interface ApiMapResponse {
  area: string;
  neighbors: string[];
  /** Shared border paths keyed by "locA|locB". */
  borders: Record<string, number[][][]>;
  provinces: ApiProvinceDto[];
}
