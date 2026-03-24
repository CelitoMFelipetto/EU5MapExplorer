import { Component, effect, inject, OnDestroy } from '@angular/core';
import * as L from 'leaflet';
import { LocationDto } from '../models/location.dto';
import { MAP_PANES } from '../map-panes';
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

const lakeColor = '#11a9ec';

/**
 * Headless component — no template. Receives its LocationDto via DI at
 * construction time (provided by ProvinceComponent via a custom Injector) and
 * renders the Leaflet polygon immediately in the constructor.
 */
@Component({
  selector: 'app-location',
  standalone: true,
  template: '',
})
export class LocationComponent implements OnDestroy {
  private readonly polygon: L.Polygon;
  private readonly location: LocationDto;

  constructor() {
    this.location      = inject(LOCATION_DTO);
    const mapService   = inject(MapService);
    const mapHighlight = inject(MapHighlightService);

    this.polygon = L.polygon(this.location.paths, {
      ...regularStyle,
      fillColor: this.fillColor(),
      pane: MAP_PANES.locations.name,
    });

    if (this.location.topography !== 'lakes') {
      const locationId = this.location.id;
      const map        = mapService.map!;
      const tooltip    = L.tooltip({ sticky: false }).setContent(locationId);

      this.polygon.on({
        mouseover: () => mapHighlight.highlight(locationId),
        mouseout:  () => mapHighlight.clear(),
      });

      // Both highlight style and tooltip are driven solely by the signal.
      // If mouseout is ever missed, the next mouseover on any location changes
      // the signal, this effect re-runs, and the stale tooltip is removed.
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

  private fillColor(): string {
    return this.location.topography === 'lakes' ? lakeColor : this.location.color;
  }
}
