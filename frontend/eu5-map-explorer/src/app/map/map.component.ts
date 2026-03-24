import {
  AfterViewInit,
  Component,
  ComponentRef,
  ElementRef,
  inject,
  OnDestroy,
  signal,
  ViewChild,
  ViewContainerRef,
} from '@angular/core';
import * as L from 'leaflet';
import { MapService } from './map.service';
import { LocationComponent, LocationHoverEvent } from './location/location.component';

@Component({
  selector: 'app-map',
  standalone: true,
  template: `
    <div #mapEl class="map-container"></div>
    <div #tooltipEl class="map-tooltip"></div>
    <div class="zoom-badge">{{ zoomLevel() }}</div>
    <ng-container #locationHost></ng-container>
  `,
  styles: [`
    :host {
      display: block;
      width: 100%;
      height: 100%;
      position: relative;
    }

    .map-container {
      width: 100%;
      height: 100%;
      background: #f5f0e8;
    }

    .map-tooltip {
      position: fixed;
      pointer-events: none;
      display: none;
      padding: 4px 10px;
      background: rgba(255, 255, 255, 0.92);
      border: 1px solid #ccc;
      border-radius: 4px;
      font-family: monospace;
      font-size: 12px;
      color: #333;
      box-shadow: 0 2px 6px rgba(0, 0, 0, 0.15);
      z-index: 9999;
    }

    .zoom-badge {
      position: absolute;
      /* sits directly below Leaflet's two zoom buttons (~58 px tall) + 10 px offset */
      top: 74px;
      left: 10px;
      z-index: 1000;
      background: rgba(255, 255, 255, 0.9);
      border: 2px solid rgba(0, 0, 0, 0.2);
      border-radius: 4px;
      padding: 0 6px;
      font-family: monospace;
      font-size: 12px;
      font-weight: 600;
      color: #333;
      line-height: 26px;
      min-width: 26px;
      text-align: center;
      pointer-events: none;
      box-shadow: 0 1px 5px rgba(0, 0, 0, 0.2);
    }
  `],
})
export class MapComponent implements AfterViewInit, OnDestroy {
  @ViewChild('mapEl')       mapEl!:       ElementRef<HTMLDivElement>;
  @ViewChild('tooltipEl')   tooltipEl!:   ElementRef<HTMLDivElement>;
  @ViewChild('locationHost', { read: ViewContainerRef })
  locationHost!: ViewContainerRef;

  private readonly mapService = inject(MapService);
  private map?: L.Map;
  private locationRefs: ComponentRef<LocationComponent>[] = [];

  readonly zoomLevel = signal('—');

  ngAfterViewInit(): void {
    this.mapService.getMapData().subscribe(({ svgWidth, svgHeight, locations }) => {
      // Build a minimal SVG shell — LocationComponents fill it with <path> elements.
      const ns = 'http://www.w3.org/2000/svg';
      const svgEl = document.createElementNS(ns, 'svg') as SVGSVGElement;
      svgEl.setAttribute('width',   String(svgWidth));
      svgEl.setAttribute('height',  String(svgHeight));
      svgEl.setAttribute('viewBox', `0 0 ${svgWidth} ${svgHeight}`);

      // The padding translate matches the 2px pad baked into the SVG coordinates.
      const group = document.createElementNS(ns, 'g') as SVGGElement;
      group.setAttribute('transform', 'translate(2,2)');
      svgEl.appendChild(group);

      this.map = L.map(this.mapEl.nativeElement, {
        crs: L.CRS.Simple,
        minZoom: -5,
        maxZoom: 5,
        zoomSnap: 0.25,
        attributionControl: false,
      });

      this.map.on('zoomend', () => {
        this.zoomLevel.set(this.map!.getZoom().toFixed(2));
      });

      // CRS.Simple: lat increases upward → SW=[0,0], NE=[h,w].
      const bounds: L.LatLngBoundsExpression = [[0, 0], [svgHeight, svgWidth]];
      L.svgOverlay(svgEl, bounds).addTo(this.map);
      this.map.fitBounds(bounds);
      this.zoomLevel.set(this.map.getZoom().toFixed(2));

      // Spawn one LocationComponent per location; each appends its own <g> to
      // the SVG group and manages its own hover state.
      for (const location of locations) {
        const ref = this.locationHost.createComponent(LocationComponent);
        ref.setInput('map', this.map);
        ref.setInput('location', location);
        this.locationRefs.push(ref);
      }
    });
  }

  // ── Lifecycle ──────────────────────────────────────────────────────────────

  ngOnDestroy(): void {
    this.locationRefs.forEach(ref => ref.destroy());
    this.map?.remove();
  }
}
