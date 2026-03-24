import { inject, Injectable, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { map, Observable } from 'rxjs';
import * as L from 'leaflet';
import {
  ApiMapResponse,
  LocationDto,
  MapDataDto,
  PathCoordinates,
  ProvinceDto,
} from './models/location.dto';
import { COLOR_LEGENDS, LEGEND_DEFAULT_COLOR, MAP_MODES, MapMode } from './map-mode';

const LAKE_COLOR = '#11a9ec';

const SESSION_KEYS = {
  zoom: 'eu5map_zoom',
  pan:  'eu5map_pan',   // JSON { x: number; y: number }
  mode: 'eu5map_mode',
} as const;

const VALID_MODES = new Set<string>(MAP_MODES.map(m => m.id));

function readSessionMode(): MapMode {
  const stored = sessionStorage.getItem(SESSION_KEYS.mode);
  return (stored && VALID_MODES.has(stored)) ? stored as MapMode : 'locations';
}

@Injectable({ providedIn: 'root' })
export class MapService {
  private readonly http = inject(HttpClient);
  public mapHeight = 0;

  /** The active Leaflet map instance. Set by MapComponent, cleared on destroy. */
  private _map: L.Map | null = null;

  get map(): L.Map | null { return this._map; }

  set map(value: L.Map | null) {
    this._map = value;

    if (value) {
      // Persist zoom whenever the user finishes a zoom gesture.
      value.on('zoomend', () => {
        sessionStorage.setItem(SESSION_KEYS.zoom, value.getZoom().toString());
      });

      // Persist pan center whenever the user finishes a pan gesture.
      // moveend fires on both drag-end and programmatic setView calls,
      // so one listener covers every case.
      value.on('moveend', () => {
        const c = value.getCenter();
        sessionStorage.setItem(SESSION_KEYS.pan, JSON.stringify({ x: c.lng, y: c.lat }));
      });
    }
  }

  /**
   * Returns a saved { center, zoom } if sessionStorage has a complete entry,
   * or null on first visit (so MapComponent falls back to fitBounds).
   */
  getSavedView(): { center: L.LatLngExpression; zoom: number } | null {
    const zoomStr = sessionStorage.getItem(SESSION_KEYS.zoom);
    const panStr  = sessionStorage.getItem(SESSION_KEYS.pan);
    if (!zoomStr || !panStr) return null;
    try {
      const { x, y } = JSON.parse(panStr) as { x: number; y: number };
      return { center: [y, x], zoom: parseFloat(zoomStr) };
    } catch {
      return null;
    }
  }

  /** Currently selected map display mode — initialised from sessionStorage. */
  readonly mapMode = signal<MapMode>(readSessionMode());

  setMapMode(mode: MapMode): void {
    this.mapMode.set(mode);
    sessionStorage.setItem(SESSION_KEYS.mode, mode);
  }

  /**
   * Returns the fill colour for a location given the current map mode.
   * Reading mapMode() here makes any effect() that calls this method
   * automatically reactive to mode changes.
   */
  getLocationColor(location: LocationDto): string {
    // Lakes always use the fixed water colour regardless of the selected mode.
    if (location.topography === 'lakes') return LAKE_COLOR;

    const mode = this.mapMode();
    if (mode === 'locations') return location.color;

    const legend = COLOR_LEGENDS[mode];
    const value  = location[mode]; // topography | climate | vegetation | raw_material
    return value != null ? (legend[value] ?? LEGEND_DEFAULT_COLOR) : LEGEND_DEFAULT_COLOR;
  }

  /**
   * Fetches map data from GET /api/map and converts it into the MapDataDto
   * shape expected by MapComponent / ProvinceComponent / LocationComponent.
   *
   * The API returns pixel-space coordinates [x, y] from the source PNG.
   * Leaflet's CRS.Simple expects [lat, lng] where lat increases upward, so
   * each point is remapped to [imageHeight - y, x].
   * The image dimensions are derived from the max coordinates in the data.
   */
  getMapData(): Observable<MapDataDto> {
    return this.http.get<ApiMapResponse>('/api/map').pipe(
      map(response => this.mapApiResponse(response)),
    );
  }

  private mapApiResponse(response: ApiMapResponse): MapDataDto {
    // ── Pass 1: derive image bounds from max coordinates ─────────────────────
    let maxX = 0;
    let maxY = 0;

    for (const province of response.provinces) {
      for (const path of province.paths) {
        for (const [x, y] of path) {
          if (x > maxX) maxX = x;
          if (y > maxY) maxY = y;
        }
      }
      for (const location of province.locations) {
        for (const path of location.paths) {
          for (const [x, y] of path) {
            if (x > maxX) maxX = x;
            if (y > maxY) maxY = y;
          }
        }
      }
    }

    const svgWidth  = maxX;
    const svgHeight = maxY;
    this.mapHeight  = svgHeight;

    const flip = ([x, y]: number[]): PathCoordinates => [svgHeight - y, x];

    // ── Pass 2: build ProvinceDtos and LocationDtos ───────────────────────────
    const provinces: ProvinceDto[] = [];

    for (const apiProvince of response.provinces) {
      // Build the ProvinceDto shell first so LocationDtos can reference it.
      const provinceDto: ProvinceDto = {
        id:        apiProvince.name,
        paths:     apiProvince.paths.map(path => path.map(flip)),
        locations: [],
      };

      provinceDto.locations = apiProvince.locations.map(loc => {
        const locationDto: LocationDto = {
          id:           loc.name,
          color:        `#${loc.color}`,
          topography:   loc.topography,
          climate:      loc.climate,
          vegetation:   loc.vegetation,
          raw_material: loc.raw_material,
          paths:        loc.paths.map(path => path.map(flip)),
          province:     provinceDto,
        };
        return locationDto;
      });

      provinces.push(provinceDto);
    }

    return { svgWidth, svgHeight, provinces };
  }
}
