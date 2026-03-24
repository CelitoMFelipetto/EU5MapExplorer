import { Component, effect, inject, OnDestroy } from '@angular/core';
import * as L from 'leaflet';
import { LocationDto } from '../models/location.dto';
import { MAP_PANES } from '../map-panes';
import { MapMode } from '../map-mode';
import { LOCATION_DTO } from '../map-tokens';
import { MapHighlightService } from '../map-highlight.service';
import { MapService } from '../map.service';

const regularStyle: L.PolylineOptions = {
  color: '#AAA',
  weight: 1,
  fillOpacity: 0.5,
};

const highlightStyle: L.PolylineOptions = {
  color: '#fff43d',
  weight: 3,
  fillOpacity: 0.7,
};

function tooltipContent(location: LocationDto, mode: MapMode): string {
  if (mode === 'locations') return location.id;
  const value = location[mode];
  return value ?? '—';
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

  constructor() {
    const location     = inject(LOCATION_DTO);
    const mapService   = inject(MapService);
    const mapHighlight = inject(MapHighlightService);

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

    // ── Highlight + tooltip effects (non-lakes only) ──────────────────────────
    if (location.topography !== 'lakes') {
      const locationId = location.id;
      const map        = mapService.map!;
      const tooltip    = L.tooltip({ sticky: false });

      // Update tooltip text whenever the mode changes so hovering always
      // shows the value relevant to the currently selected layer.
      effect(() => {
        tooltip.setContent(tooltipContent(location, mapService.mapMode()));
      });

      this.polygon.on({
        mouseover: () => mapHighlight.highlight(locationId),
        mouseout:  () => mapHighlight.clear(),
      });

      // Both highlight style and tooltip visibility are driven solely by the
      // signal. If mouseout is ever missed, the next mouseover on any location
      // changes the signal, this effect re-runs, and the stale tooltip is removed.
      effect(() => {
        if (mapHighlight.highlightedLocationId() === locationId) {
          this.polygon.setStyle({ ...highlightStyle });
          this.polygon.bringToFront();
          tooltip.setLatLng(this.polygon.getBounds().getCenter()).addTo(map);
        } else {
          this.polygon.setStyle({ ...regularStyle });
          tooltip.remove();
        }
      });
    }

    this.polygon.addTo(mapService.map!);
  }

  ngOnDestroy(): void {
    this.polygon.remove();
  }
}
