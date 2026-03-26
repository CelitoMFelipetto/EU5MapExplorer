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

export interface ProvinceDto {
  /** Province name (e.g. 'uppland_province'). */
  id: string;
  /** Leaflet-ready province boundary paths — coordinates already converted to [lat, lng]. */
  paths: PolygonPath;
  /** All locations that belong to this province. */
  locations: LocationDto[];
}

export interface MapDataDto {
  svgWidth: number;
  svgHeight: number;
  provinces: ProvinceDto[];
}

// ── Raw shapes returned by GET /api/map ───────────────────────────────────────

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
  /**
   * Boundary polygon paths in raw image pixel space.
   * paths[pathIndex][pointIndex] = [x, y]
   */
  paths: number[][][];
}

export interface ApiProvinceDto {
  name: string;
  /** Boundary polygon paths in raw image pixel space. */
  paths: number[][][];
  locations: ApiLocationDto[];
}

export interface ApiMapResponse {
  area: string;
  provinces: ApiProvinceDto[];
}
