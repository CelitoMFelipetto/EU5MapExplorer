// ── Types consumed by LocationComponent / ProvinceComponent / MapComponent ────

export type PathCoordinates = [number, number];

export type PolygonPath = PathCoordinates[] | PathCoordinates[][] | PathCoordinates[][][];

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
