export type PathCoordinates = [number, number];

export type PolygonPath = PathCoordinates[] | PathCoordinates[][] | PathCoordinates[][][];

export interface LocationDto {
  /** Hex colour without '#', used as a unique identifier (e.g. 'dda910'). */
  id: string;
  /** Full hex colour string (e.g. '#dda910'). */
  color: string;
  /** SVG path `d` attribute values — one entry per closed loop in this location. */
  paths: PolygonPath;
}

export interface MapDataDto {
  svgWidth: number;
  svgHeight: number;
  locations: LocationDto[];
}
