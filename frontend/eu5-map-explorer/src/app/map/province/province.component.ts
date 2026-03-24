import {
  Component,
  ComponentRef,
  effect,
  inject,
  Injector,
  OnDestroy,
  ViewContainerRef,
} from '@angular/core';
import * as L from 'leaflet';
import { LocationComponent } from '../location/location.component';
import { MAP_PANES } from '../map-panes';
import { LOCATION_DTO, PROVINCE_DTO } from '../map-tokens';
import { MapHighlightService } from '../map-highlight.service';
import { MapService } from '../map.service';

const provinceStyle: L.PolylineOptions = {
  color: '#888',
  weight: 2,
  fill: false,
  interactive: false,
  pane: MAP_PANES.provinceOutlines.name,
};

const provinceHighlightStyle: L.PolylineOptions = {
  color: '#333',
  weight: 3,
};

/**
 * Headless component — no template. Receives its ProvinceDto via DI at
 * construction time (provided by MapComponent via a custom Injector), renders
 * the province boundary outline immediately, then spawns one LocationComponent
 * per location using the same pattern.
 */
@Component({
  selector: 'app-province',
  standalone: true,
  template: '',
})
export class ProvinceComponent implements OnDestroy {
  private readonly outline: L.Polygon;
  private readonly locationRefs: ComponentRef<LocationComponent>[] = [];

  constructor() {
    const province     = inject(PROVINCE_DTO);
    const mapService   = inject(MapService);
    const mapHighlight = inject(MapHighlightService);
    const vcr          = inject(ViewContainerRef);

    this.outline = L.polygon(province.paths, provinceStyle).addTo(mapService.map!);
    this.outline.bindTooltip(province.id, { sticky: false });

    // Build a set of this province's location IDs for O(1) membership checks.
    const locationIds = new Set(province.locations.map(l => l.id));

    // Darken the outline whenever one of this province's locations is hovered.
    effect(() => {
      if (locationIds.has(mapHighlight.highlightedLocationId()!)) {
        this.outline.setStyle(provinceHighlightStyle);
        this.outline.bringToFront();
      } else {
        this.outline.setStyle(provinceStyle);
      }
    });

    for (const location of province.locations) {
      const injector = Injector.create({
        providers: [{ provide: LOCATION_DTO, useValue: location }],
        parent: vcr.injector,
      });
      const ref = vcr.createComponent(LocationComponent, { injector });
      this.locationRefs.push(ref);
    }
  }

  ngOnDestroy(): void {
    this.locationRefs.forEach(ref => ref.destroy());
    this.outline.remove();
  }
}
