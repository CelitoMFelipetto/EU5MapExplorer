import { effect, inject, Injectable, signal } from '@angular/core';
import * as L from 'leaflet';
import { ProvinceBounds, ProvinceDto } from './models/location.dto';
import { MapService } from './map.service';

@Injectable({ providedIn: 'root' })
export class ViewportCullingService {
  private readonly mapService = inject(MapService);

  readonly PADDING_PX = 0;

  private readonly provinceRegistry = new Map<string, ProvinceBounds>();

  readonly visibleProvinceIds = signal<ReadonlySet<string>>(new Set<string>());

  constructor() {
    effect(() => {
      const bounds = this.mapService.viewBounds();
      if (bounds === null) return;
      this.recomputeVisibility(bounds);
    });
  }

  registerProvinces(provinces: ProvinceDto[]): void {
    for (const p of provinces) {
      this.provinceRegistry.set(p.id, p.bounds);
    }
    const map = this.mapService.map;
    if (map) this.recomputeVisibility(map.getBounds());
  }

  private recomputeVisibility(vb: L.LatLngBounds): void {
    const pad = this.PADDING_PX;
    const mapH = this.mapService.mapHeight;
    const visible = new Set<string>();

    for (const [id, b] of this.provinceRegistry) {
      // Convert pixel bounds to Leaflet lat/lng
      const northLat = mapH - b.maxN;  // maxN = smallest Y = topmost row
      const southLat = mapH - b.maxS;  // maxS = largest Y  = bottommost row
      const westLng = b.maxW;
      const eastLng = b.maxE;

      if (
        northLat >= vb.getSouth() + pad &&
        southLat <= vb.getNorth() - pad &&
        eastLng >= vb.getWest() + pad &&
        westLng <= vb.getEast() - pad
      ) {
        visible.add(id);
      }
    }

    this.visibleProvinceIds.set(visible);
  }
}
