import { Component, effect, inject, OnDestroy } from '@angular/core';
import * as L from 'leaflet';
import { LocationDto } from '../models/location.dto';
import { MAP_PANES } from '../map-panes';
import { MapMode } from '../map-mode';
import { LOCATION_DTO } from '../map-tokens';
import { MapHighlightService } from '../map-highlight.service';
import { MapService } from '../map.service';
import { ViewportCullingService } from '../viewport-culling.service';

const regularStyle: L.PolylineOptions = {
  weight: 0,
  fillOpacity: 0.5,
};

const highlightStyle: L.PolylineOptions = {
  weight: 0,
  fillOpacity: 0.7,
};

/** Returns the tooltip string, or null if there is nothing worth showing. */
function tooltipContent(location: LocationDto, mode: MapMode): string | null {
  if (mode === 'locations') return location.id;
  const value = location[mode];
  return value != null ? String(value) : null;
}

/**
 * Headless component — no template. Receives its LocationDto via DI at
 * construction time (provided by ProvinceComponent via a custom Injector) and
 * renders the Leaflet polygon immediately in the constructor.
 *
 * Three independent effects keep the polygon in sync:
 *  - color effect   → updates fillColor when the map mode changes (all locations)
 *  - tooltip effect → updates tooltip content when the map mode changes (non-lakes)
 *  - highlight effect → updates stroke style and shows/hides tooltip on hover (non-lakes)
 */
@Component({
  selector: 'app-location',
  standalone: true,
  template: '',
})
export class LocationComponent implements OnDestroy {
  private readonly polygon: L.Polygon;
  private readonly cityMarker: L.Marker | null = null;
  private readonly goodsMarker: L.Marker | null = null;

  constructor() {
    const location = inject(LOCATION_DTO);
    const mapService = inject(MapService);
    const mapHighlight = inject(MapHighlightService);
    const viewportCulling = inject(ViewportCullingService);

    this.polygon = L.polygon(location.paths, {
      ...regularStyle,
      fillColor: mapService.getLocationColor(location),
      pane: MAP_PANES.locations.name,
    });

    // ── Color mode effect (all locations, including lakes) ────────────────────
    // Only touches fillColor — never conflicts with the highlight effect.
    effect(() => {
      this.polygon.setStyle({ fillColor: mapService.getLocationColor(location) });
    });

    // ── Viewport visibility effect ────────────────────────────────────────────
    const provinceId = location.province.id;
    const map = mapService.map!;
    effect(() => {
      if (viewportCulling.visibleProvinceIds().has(provinceId)) {
        this.polygon.addTo(map);
      } else {
        this.polygon.remove();
      }
    });

    // ── Highlight + tooltip effects (non-lakes only) ──────────────────────────
    if (location.topography !== 'lakes') {
      const tooltip = L.tooltip({ sticky: false });
      const center = this.polygon.getBounds().getCenter();

      if (location.city_position != null) {
        // Full highlight behaviour: fires global signal → borders turn gold, fill brightens.
        const locationId = location.id;

        this.polygon.on({
          mouseover: () => mapHighlight.highlight(locationId),
          mouseout:  () => mapHighlight.clear(),
        });

        effect(() => {
          const isHighlighted = mapHighlight.highlightedLocationId() === locationId;
          if (isHighlighted) {
            this.polygon.setStyle({ ...highlightStyle });
            this.polygon.bringToFront();
            const content = tooltipContent(location, mapService.mapMode());
            if (content != null) {
              tooltip.setContent(content).setLatLng(center).addTo(map);
            }
          } else {
            this.polygon.setStyle({ ...regularStyle });
            tooltip.remove();
          }
        });
      } else {
        // No city position (impassable, wasteland, etc.) — tooltip only, no highlight.
        // Still clears any lingering highlight from a previously hovered location.
        this.polygon.on({
          mouseover: () => {
            mapHighlight.clear();
            const content = tooltipContent(location, mapService.mapMode());
            if (content != null) {
              tooltip.setContent(content).setLatLng(center).addTo(map);
            }
          },
          mouseout: () => tooltip.remove(),
        });
      }
    }

    // ── City rank icon (non-lakes with a city_position) ───────────────────────
    // The icon is a fixed-pixel-size image that never scales with zoom.
    // It is shown only when the zoom level exceeds 0.5.
    if (location.city_position && location.topography !== 'lakes') {
      const cityPos = location.city_position;
      // Apply the same CRS.Simple flip used for polygon paths:
      //   Leaflet lat = svgHeight − y,  lng = x
      const lat = mapService.mapHeight - cityPos.y;
      const lng = cityPos.x;

      const iconSize = location.rank === 'city' ? 14 : 12;

      const icon = L.divIcon({
        html: `<img src="city_ranks/${location.rank}.png" style="width:${iconSize}px;height:${iconSize}px;display:block;" />`,
        iconSize: [iconSize, iconSize],
        iconAnchor: [iconSize / 2, iconSize / 2],
        className: '',   // suppress Leaflet's default white-box wrapper style
      });

      this.cityMarker = L.marker([lat, lng], {
        icon,
        pane: MAP_PANES.cityIcons.name,
        interactive: false,
      });

      const marker = this.cityMarker;

      // Show / hide based on zoom AND viewport visibility.
      // CRS.Simple zoom levels are negative at full-map view (typically -2…0),
      // so the threshold is set to .5: icons appear at the default view and
      // above, and disappear only when zoomed very far out.
      effect(() => {
        if (mapService.zoom() > .5 && viewportCulling.visibleProvinceIds().has(provinceId)) {
          marker.addTo(map);
        } else {
          marker.remove();
        }
      });
    }

    // ── Goods / raw material icon (locations with a unit_position and raw_material) ──
    /** Some raw materials have no icon — fall back to the closest equivalent. */
    const GOODS_ICON_ALIASES: Record<string, string> = { millet: 'sturdy_grains' };

    if (location.unit_position && location.raw_material) {
      const unitPos = location.unit_position;
      const lat = mapService.mapHeight - unitPos.y;
      const lng = unitPos.x;
      const rawMaterial = GOODS_ICON_ALIASES[location.raw_material] ?? location.raw_material;

      const iconSize = 24;
      const icon = L.divIcon({
        html: `<img src="goods/${rawMaterial}.png" style="width:${iconSize}px;height:${iconSize}px;display:block;" />`,
        iconSize: [iconSize, iconSize],
        iconAnchor: [iconSize / 2, iconSize / 2],
        className: '',
      });

      this.goodsMarker = L.marker([lat, lng], {
        icon,
        pane: MAP_PANES.goodsIcons.name,
        interactive: false,
      });

      const goodsMarker = this.goodsMarker;

      // Show only when zoom > 0.5, viewport visible, AND raw_material mode is active.
      effect(() => {
        if (
          mapService.zoom() > .5 &&
          mapService.mapMode() === 'raw_material' &&
          viewportCulling.visibleProvinceIds().has(provinceId)
        ) {
          goodsMarker.addTo(map);
        } else {
          goodsMarker.remove();
        }
      });
    }
  }

  ngOnDestroy(): void {
    this.polygon.remove();
    this.cityMarker?.remove();
    this.goodsMarker?.remove();
  }
}
