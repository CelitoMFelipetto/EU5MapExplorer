import {
  Component,
  ComponentRef,
  inject,
  Injector,
  OnDestroy,
  ViewContainerRef,
} from '@angular/core';
import { LocationComponent } from '../location/location.component';
import { LOCATION_DTO, PROVINCE_DTO } from '../map-tokens';

/**
 * Headless component — no template. Receives its ProvinceDto via DI at
 * construction time (provided by MapComponent via a custom Injector), then
 * spawns one LocationComponent per location using the same pattern.
 *
 * Province outline rendering and highlight behaviour are handled by
 * BorderLayerService, which draws dedicated L.Polyline objects per shared
 * border segment rather than a single province-outline polygon.
 */
@Component({
  selector: 'app-province',
  standalone: true,
  template: '',
})
export class ProvinceComponent implements OnDestroy {
  private readonly locationRefs: ComponentRef<LocationComponent>[] = [];

  constructor() {
    const province = inject(PROVINCE_DTO);
    const vcr      = inject(ViewContainerRef);

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
  }
}
