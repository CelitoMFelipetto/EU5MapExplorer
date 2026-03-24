import { Component, inject, OnDestroy } from '@angular/core';
import * as L from 'leaflet';
import { LocationDto } from '../models/location.dto';
import { MAP_PANES } from '../map-panes';
import { LOCATION_DTO } from '../map-tokens';
import { MapService } from '../map.service';

const regularStyle: L.PolylineOptions = {
  color: 'gray',
  weight: 2,
  fillOpacity: 0.5,
};

const highlightStyle: L.PolylineOptions = {
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
    this.location    = inject(LOCATION_DTO);
    const mapService = inject(MapService);

    this.polygon = L.polygon(this.location.paths, {
      ...regularStyle,
      fillColor: this.fillColor(),
      pane: MAP_PANES.locations.name,
    });

    if (this.location.topography !== 'lakes') {
      this.polygon.on({
        mouseover: () => this.highlight(),
        mouseout:  () => this.resetHighlight(),
      });
      this.polygon.bindTooltip(this.location.id);
    }

    this.polygon.addTo(mapService.map!);
  }

  ngOnDestroy(): void {
    this.polygon.remove();
  }

  private fillColor(): string {
    return this.location.topography === 'lakes' ? lakeColor : this.location.color;
  }

  private highlight(): void {
    this.polygon.setStyle({ ...highlightStyle, color: this.fillColor() });
    this.polygon.bringToFront();
  }

  private resetHighlight(): void {
    this.polygon.setStyle({ ...regularStyle });
  }
}
