import { effect, inject, Injectable } from '@angular/core';
import * as L from 'leaflet';
import { MAP_PANES } from './map-panes';
import { MapHighlightService } from './map-highlight.service';
import { MapService } from './map.service';
import { ProvinceDto } from './models/location.dto';

const locationBorderStyle: L.PolylineOptions = { color: '#aaa', weight: 2, opacity: 0.7 };
const provinceBorderStyle: L.PolylineOptions = { color: '#777', weight: 2, opacity: 0.85 };
const locationHighlightStyle: L.PolylineOptions = { color: '#fff43d', weight: 3, opacity: 1 };
const provinceHighlightStyle: L.PolylineOptions = { color: '#333', weight: 3, opacity: 1 };

/**
 * Manages shared border polylines for the entire map.
 *
 * Each unique (borderKey, pathIndex) pair is drawn as exactly one L.Polyline on
 * the 'bordersPane'. Province-level borders are styled thicker/darker than
 * location-only borders. On hover, the hovered location's borders turn gold and
 * its province's borders (that aren't also the location's own) turn dark gray.
 */
@Injectable({ providedIn: 'root' })
export class BorderLayerService {
  private readonly mapService = inject(MapService);
  private readonly mapHighlight = inject(MapHighlightService);

  /** "key/pathIndex" → Leaflet polyline */
  private readonly polylines = new Map<string, L.Polyline>();
  /** locationId → Set of polyline keys */
  private readonly locationBorders = new Map<string, Set<string>>();
  /** provinceId → Set of polyline keys */
  private readonly provinceBorders = new Map<string, Set<string>>();
  /** locationId → provinceId */
  private readonly locationToProvince = new Map<string, string>();
  /** polyline keys that appear in at least one province's border rings */
  private readonly provincePolylineKeys = new Set<string>();
  /** polyline keys currently highlighted (reset on next highlight change) */
  private readonly activeKeys = new Set<string>();

  constructor() {
    effect(() => {
      const locationId = this.mapHighlight.highlightedLocationId();

      // Reset previously highlighted polylines to their default styles
      for (const pk of this.activeKeys) {
        const style = this.provincePolylineKeys.has(pk) ? provinceBorderStyle : locationBorderStyle;
        this.polylines.get(pk)?.setStyle(style);
      }
      this.activeKeys.clear();

      if (!locationId) return;

      // Highlight location's own borders (gold)
      const locKeys = this.locationBorders.get(locationId) ?? new Set<string>();
      for (const pk of locKeys) {
        this.polylines.get(pk)?.setStyle(locationHighlightStyle);
        this.activeKeys.add(pk);
      }

      // Highlight province borders (dark gray) — skip any already highlighted gold
      const provinceId = this.locationToProvince.get(locationId);
      if (provinceId) {
        for (const pk of this.provinceBorders.get(provinceId) ?? []) {
          if (!locKeys.has(pk)) {
            this.polylines.get(pk)?.setStyle(provinceHighlightStyle);
            this.activeKeys.add(pk);
          }
        }
      }
    });
  }

  /**
   * Register all provinces (and their locations) from a freshly loaded area.
   * Creates any missing polylines and records membership for highlight tracking.
   * Safe to call multiple times — polylines are only created once per unique key.
   */
  registerArea(provinces: ProvinceDto[]): void {
    const leafletMap = this.mapService.map;
    if (!leafletMap) return;

    const flip = ([x, y]: number[]): [number, number] =>
      [this.mapService.mapHeight - y, x];

    for (const province of provinces) {
      // Ensure province border set exists
      if (!this.provinceBorders.has(province.id)) {
        this.provinceBorders.set(province.id, new Set());
      }
      const provSet = this.provinceBorders.get(province.id)!;

      // Register province-level border segments
      for (const { key, pathIndex } of province.borderKeys) {
        const pk = `${key}/${pathIndex}`;
        this.provincePolylineKeys.add(pk);
        provSet.add(pk);
        this.ensurePolyline(pk, key, pathIndex, leafletMap, flip, provinceBorderStyle);
      }

      // Register per-location border segments
      for (const location of province.locations) {
        this.locationToProvince.set(location.id, province.id);

        if (!this.locationBorders.has(location.id)) {
          this.locationBorders.set(location.id, new Set());
        }
        const locSet = this.locationBorders.get(location.id)!;

        for (const { key, pathIndex } of location.borderKeys) {
          const pk = `${key}/${pathIndex}`;
          locSet.add(pk);
          // Only create (not upgrade) if not already a province-level polyline
          const style = this.provincePolylineKeys.has(pk) ? provinceBorderStyle : locationBorderStyle;
          this.ensurePolyline(pk, key, pathIndex, leafletMap, flip, style);
        }
      }
    }
  }

  private ensurePolyline(
    pk: string,
    borderKey: string,
    pathIndex: number,
    leafletMap: L.Map,
    flip: (pt: number[]) => [number, number],
    style: L.PolylineOptions,
  ): void {
    if (this.polylines.has(pk)) return;

    const rawPath = this.mapService.borders.get(borderKey)?.[pathIndex];
    if (!rawPath || rawPath.length === 0) return;

    const latlngs = rawPath.map(flip);
    const polyline = L.polyline(latlngs, {
      ...style,
      pane: MAP_PANES.borders.name,
      interactive: false,
    }).addTo(leafletMap);

    this.polylines.set(pk, polyline);
  }
}
