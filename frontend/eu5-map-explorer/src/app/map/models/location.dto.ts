// ── Types consumed by LocationComponent / MapComponent ───────────────────────

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
}

export interface MapDataDto {
  svgWidth: number;
  svgHeight: number;
  locations: LocationDto[];
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
  locations: ApiLocationDto[];
}

export interface ApiMapResponse {
  area: string;
  provinces: ApiProvinceDto[];
}
