import {
  Component,
  ComponentRef,
  inject,
  Input,
  OnChanges,
  OnDestroy,
  ViewContainerRef,
} from '@angular/core';
import * as L from 'leaflet';
import { ProvinceDto } from '../models/location.dto';
import { LocationComponent } from '../location/location.component';
import { MAP_PANES } from '../map-panes';

const provinceStyle: L.PolylineOptions = {
  color: '#666',
  weight: 2,
  fill: false,
  interactive: false,
  pane: MAP_PANES.provinceOutlines.name,
};

/**
 * Headless component — no template. Each instance manages the province
 * boundary outline on the Leaflet map and owns the lifecycle of all child
 * LocationComponent instances belonging to this province.
 */
@Component({
  selector: 'app-province',
  standalone: true,
  template: '',
})
export class ProvinceComponent implements OnChanges, OnDestroy {
  @Input() province!: ProvinceDto;
  @Input() map!: L.Map;

  private readonly vcr = inject(ViewContainerRef);

  private outline?: L.Polygon;
  private locationRefs: ComponentRef<LocationComponent>[] = [];

  ngOnChanges(): void {
    if (!this.province || !this.map) return;
    this.render();
  }

  // ── Rendering ──────────────────────────────────────────────────────────────

  private render(): void {
    this.outline = L.polygon(this.province.paths, provinceStyle).addTo(this.map);
    this.outline.bindTooltip(this.province.id, { sticky: false });

    for (const location of this.province.locations) {
      const ref = this.vcr.createComponent(LocationComponent);
      ref.setInput('map', this.map);
      ref.setInput('location', location);
      this.locationRefs.push(ref);
    }
  }

  // ── Lifecycle ──────────────────────────────────────────────────────────────

  ngOnDestroy(): void {
    this.locationRefs.forEach(ref => ref.destroy());
    this.outline?.remove();
  }
}
