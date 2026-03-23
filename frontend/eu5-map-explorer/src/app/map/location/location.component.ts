import { Component, Input, OnChanges } from '@angular/core';
import { LocationDto } from '../models/location.dto';
import * as L from 'leaflet';

export interface LocationHoverEvent {
  location: LocationDto;
  x: number;
  y: number;
}

/**
 * Headless component — no template. Each instance manages a single L.Polygon 
 * element appended to the Leaflet map, containing all closed-loop paths for
 * one location. Inputs are set dynamically by MapComponent via
 * ViewContainerRef, so
 * rendering is deferred to ngOnChanges once both required inputs are present.
 */
@Component({
  selector: 'app-location',
  standalone: true,
  template: '',
})
export class LocationComponent implements OnChanges {
  @Input() location!: LocationDto;
  @Input() map!: L.Map;

  private polygon?: L.Polygon = undefined;

  ngOnChanges(): void {
    // Both inputs must be present, and we only render once.
    if (!this.location || !this.map ) return;
    this.render();
  }

  // ── Rendering ──────────────────────────────────────────────────────────────

  private render(): void {
    // creates the location
    this.polygon = L.polygon(this.location.paths, {color: 'gray', fillColor: this.location.color});
    // adds interactivity
    this.polygon.on({
      mouseover: () => this.highlight(),
      mouseout: () => this.resetHighlight(),
    });
    // adds location to the map
    this.polygon.addTo(this.map);
  }

  private highlight(): void {
    this.polygon?.setStyle({
      fillOpacity: 0.7,
      color: this.location.color,
      weight: 5,
    });
    this.polygon?.bringToFront();
  }

  private resetHighlight() {
    this.polygon?.setStyle({
      fillOpacity: 0.5,
      color: 'gray',
      weight: 3,
    });
  }
}
