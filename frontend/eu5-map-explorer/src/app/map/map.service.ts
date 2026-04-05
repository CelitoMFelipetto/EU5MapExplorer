import { inject, Injectable, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { map, Observable, Subject } from 'rxjs';
import * as L from 'leaflet';
import {
  ApiBorderRing,
  ApiMapResponse,
  LocationDto,
  MapDataDto,
  PathCoordinates,
  ProvinceDto,
} from './models/location.dto';
import { COLOR_LEGENDS, LEGEND_DEFAULT_COLOR, MAP_MODES, MapMode } from './map-mode';

const WATER_COLOR = '#11a9ec';

const SESSION_KEYS = {
  zoom: 'eu5map_zoom',
  pan: 'eu5map_pan',   // JSON { x: number; y: number }
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
  /** Full map height in pixels (source image: locations.png). */
  public mapHeight = 8192;

  /** The active Leaflet map instance. Set by MapComponent, cleared on destroy. */
  private _map: L.Map | null = null;

  get map(): L.Map | null { return this._map; }

  set map(value: L.Map | null) {
    this._map = value;

    if (value) {
      value.on('zoomend', () => {
        const z = value.getZoom();
        sessionStorage.setItem(SESSION_KEYS.zoom, z.toString());
        this.zoom.set(z);
      });

      value.on('moveend', () => {
        const c = value.getCenter();
        sessionStorage.setItem(SESSION_KEYS.pan, JSON.stringify({ x: c.lng, y: c.lat }));
      });
    }
  }

  getSavedView(): { center: L.LatLngExpression; zoom: number } | null {
    const zoomStr = sessionStorage.getItem(SESSION_KEYS.zoom);
    const panStr = sessionStorage.getItem(SESSION_KEYS.pan);
    if (!zoomStr || !panStr) return null;
    try {
      const { x, y } = JSON.parse(panStr) as { x: number; y: number };
      return { center: [y, x], zoom: parseFloat(zoomStr) };
    } catch {
      return null;
    }
  }

  /** Current zoom level — updated reactively via the zoomend Leaflet event. */
  readonly zoom = signal<number>(0);

  /** Currently selected map display mode — initialised from sessionStorage. */
  readonly mapMode = signal<MapMode>(readSessionMode());

  setMapMode(mode: MapMode): void {
    this.mapMode.set(mode);
    sessionStorage.setItem(SESSION_KEYS.mode, mode);
  }

  isWaterLocation(location: LocationDto): boolean {
    return location.topography === 'lakes' ||
      location.topography === 'inland_sea' ||
      location.topography === 'ocean' ||
      location.topography === 'coastal_ocean' ||
      location.topography === 'narrows' ||
      location.topography === 'ocean_wasteland' ||
      location.topography === 'deep_ocean';
  }

  getLocationColor(location: LocationDto): string {
    if (this.isWaterLocation(location)) return WATER_COLOR;
    const mode = this.mapMode();
    if (mode === 'locations') return location.color;
    const legend = COLOR_LEGENDS[mode];
    const value = location[mode];
    return value != null ? (legend[value] ?? LEGEND_DEFAULT_COLOR) : LEGEND_DEFAULT_COLOR;
  }

  // ── Radial BFS area loading ─────────────────────────────────────────────────

  /** Cache of border paths keyed by "locA|locB". Persists across area loads. */
  private readonly borderCache = new Map<string, number[][][]>();

  private readonly MAX_LOADED_AREAS = 400;
  private readonly loadedAreas = new Set<string>();
  private readonly queuedAreas = new Set<string>();
  private readonly bfsQueue: string[] = [];

  /** Emits once per area as it is fetched and parsed. */
  readonly areaLoaded$ = new Subject<MapDataDto>();

  /** Start radial BFS loading from the given area name. Called once by MapComponent. */
  startRadialLoad(initialArea = 'svealand_area'): void {
    this.enqueueArea(initialArea);
    this.processQueue();
  }

  private enqueueArea(areaName: string): void {
    if (this.loadedAreas.has(areaName) || this.queuedAreas.has(areaName)) return;
    this.queuedAreas.add(areaName);
    this.bfsQueue.push(areaName);
  }

  private processQueue(): void {
    while (this.bfsQueue.length > 0 && this.loadedAreas.size < this.MAX_LOADED_AREAS) {
      const areaName = this.bfsQueue.shift()!;
      if (this.loadedAreas.has(areaName)) continue;

      this.fetchArea(areaName).subscribe({
        next: data => {
          this.loadedAreas.add(areaName);
          this.areaLoaded$.next(data);
          for (const neighbour of data.neighborAreas) {
            this.enqueueArea(neighbour);
          }
          this.processQueue();
        },
        error: err => {
          console.error(`Failed to load area '${areaName}':`, err);
          // Don't block the queue — skip this area and continue
          this.processQueue();
        },
      });
    }
  }

  private fetchArea(areaName: string): Observable<MapDataDto> {
    return this.http
      .get<ApiMapResponse>(`/api/map?area=${encodeURIComponent(areaName)}`)
      .pipe(map(response => this.mapApiResponse(response)));
  }

  private mapApiResponse(response: ApiMapResponse): MapDataDto {
    // Merge borders into cache
    for (const [key, paths] of Object.entries(response.borders)) {
      if (!this.borderCache.has(key)) this.borderCache.set(key, paths);
    }

    const flip = ([x, y]: number[]): PathCoordinates => [this.mapHeight - y, x];

    // Reconstruct a closed polygon from ordered border rings
    const reconstructPaths = (rings: ApiBorderRing[]): PathCoordinates[][] =>
      rings.map(ring => {
        const points: PathCoordinates[] = [];
        for (const ref of ring.borders) {
          const borderPaths = this.borderCache.get(ref.key);
          if (!borderPaths) continue;
          const seg = borderPaths[ref.pathIndex ?? 0];
          if (!seg) continue;
          const ordered = ref.reversed ? [...seg].reverse() : seg;
          // Skip first point if it duplicates the last point already added (junction)
          const start = points.length > 0 ? 1 : 0;
          for (let i = start; i < ordered.length; i++) {
            points.push(flip(ordered[i]));
          }
        }
        return points;
      });

    const provinces: ProvinceDto[] = [];

    for (const apiProvince of response.provinces) {
      const provinceDto: ProvinceDto = {
        id: apiProvince.name,
        paths: reconstructPaths(apiProvince.borderRings),
        bounds: apiProvince.bounds,
        locations: [],
      };

      provinceDto.locations = apiProvince.locations.map(loc => {
        const locationDto: LocationDto = {
          id: loc.name,
          color: `#${loc.color}`,
          topography: loc.topography,
          climate: loc.climate,
          vegetation: loc.vegetation,
          raw_material: loc.raw_material,
          rank: loc.rank,
          city_position: loc.city_position ?? null,
          paths: reconstructPaths(loc.borderRings),
          province: provinceDto,
        };
        return locationDto;
      });

      provinces.push(provinceDto);
    }

    // Derive svgWidth/svgHeight from bounds
    let svgWidth = 0, svgHeight = 0;
    for (const p of response.provinces) {
      if (p.bounds.maxE > svgWidth) svgWidth = p.bounds.maxE;
      if (p.bounds.maxS > svgHeight) svgHeight = p.bounds.maxS;
    }

    return {
      area: response.area,
      svgWidth,
      svgHeight,
      provinces,
      neighborAreas: response.neighbors ?? [],
    };
  }
}
