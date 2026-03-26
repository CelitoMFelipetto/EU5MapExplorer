import {
  AfterViewInit,
  Component,
  ComponentRef,
  ElementRef,
  inject,
  Injector,
  OnDestroy,
  signal,
  ViewChild,
  ViewContainerRef,
} from '@angular/core';
import * as L from 'leaflet';
import { MapService } from './map.service';
import { ProvinceComponent } from './province/province.component';
import { MAP_PANES } from './map-panes';
import { PROVINCE_DTO } from './map-tokens';
import { MAP_MODES, MapMode } from './map-mode';

@Component({
  selector: 'app-map',
  standalone: true,
  template: `
    <div #mapEl class="map-container"></div>
    <div #tooltipEl class="map-tooltip"></div>
    <div class="zoom-badge">{{ zoomLevel() }}</div>
    <div class="pan-badge">{{ panPosition() }}</div>
    <ng-container #locationHost></ng-container>

    <div class="mode-bar">
      @for (mode of modes; track mode.id) {
        <button
          class="mode-btn"
          [class.active]="mapService.mapMode() === mode.id"
          (click)="setMode(mode.id)">
          {{ mode.label }}
        </button>
      }
    </div>
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

    .pan-badge {
      position: absolute;
      top: 108px; /* zoom-badge top (74) + height (26) + 8px gap */
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
      white-space: nowrap;
      pointer-events: none;
      box-shadow: 0 1px 5px rgba(0, 0, 0, 0.2);
    }

    .mode-bar {
      position: absolute;
      bottom: 20px;
      left: 50%;
      transform: translateX(-50%);
      z-index: 1000;
      display: flex;
      gap: 2px;
      background: rgba(255, 255, 255, 0.92);
      border: 2px solid rgba(0, 0, 0, 0.2);
      border-radius: 6px;
      padding: 4px;
      box-shadow: 0 2px 8px rgba(0, 0, 0, 0.2);
    }

    .mode-btn {
      padding: 5px 14px;
      border: none;
      border-radius: 4px;
      background: transparent;
      font-family: monospace;
      font-size: 12px;
      font-weight: 600;
      color: #555;
      cursor: pointer;
      transition: background 0.15s, color 0.15s;
    }

    .mode-btn:hover {
      background: rgba(0, 0, 0, 0.07);
      color: #222;
    }

    .mode-btn.active {
      background: #3a6ea8;
      color: #fff;
    }
  `],
})
export class MapComponent implements AfterViewInit, OnDestroy {
  @ViewChild('mapEl') mapEl!: ElementRef<HTMLDivElement>;
  @ViewChild('tooltipEl') tooltipEl!: ElementRef<HTMLDivElement>;
  @ViewChild('locationHost', { read: ViewContainerRef })
  locationHost!: ViewContainerRef;

  protected readonly mapService = inject(MapService);
  private map?: L.Map;
  private provinceRefs: ComponentRef<ProvinceComponent>[] = [];

  readonly zoomLevel   = signal('—');
  readonly panPosition = signal('—, —');
  readonly modes = MAP_MODES;

  setMode(mode: MapMode): void {
    this.mapService.setMapMode(mode);
  }

  ngAfterViewInit(): void {
    this.mapService.getMapData().subscribe(({ svgWidth, svgHeight, provinces }) => {
      // Build a minimal SVG shell — LocationComponents fill it with <path> elements.
      const ns = 'http://www.w3.org/2000/svg';
      const svgEl = document.createElementNS(ns, 'svg') as SVGSVGElement;
      svgEl.setAttribute('width', String(svgWidth));
      svgEl.setAttribute('height', String(svgHeight));
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

      // Read the saved view BEFORE assigning the map to the service.
      // Once the map is assigned, the service attaches moveend/zoomend listeners
      // that write to sessionStorage — fitBounds would overwrite the saved values
      // if we read them any later.
      const savedView = this.mapService.getSavedView();

      // Publish the map instance so child components can inject it via MapService.
      // This also attaches the sessionStorage persistence listeners.
      this.mapService.map = this.map;

      const updatePan = () => {
        const c = this.map!.getCenter();
        // CRS.Simple: lng = x, lat = y
        this.panPosition.set(`${Math.round(c.lng)}, ${Math.round(c.lat)}`);
      };

      this.map.on('zoomend', () => {
        this.zoomLevel.set(this.map!.getZoom().toFixed(2));
      });

      this.map.on('move', updatePan);

      // Register custom panes — must be done before any layer uses them.
      for (const { name, zIndex } of Object.values(MAP_PANES)) {
        const pane = this.map.createPane(name);
        pane.style.zIndex = String(zIndex);
      }

      // CRS.Simple: lat increases upward → SW=[0,0], NE=[h,w].
      const bounds: L.LatLngBoundsExpression = [[0, 0], [svgHeight, svgWidth]];
      L.svgOverlay(svgEl, bounds).addTo(this.map);

      // Restore the saved view if one exists; otherwise fit the whole map.
      if (savedView) {
        this.map.setView(savedView.center, savedView.zoom, { animate: false });
      } else {
        this.map.fitBounds(bounds);
      }

      // Seed the UI badges and the reactive zoom signal with whatever view is
      // now active. The zoomend listener covers subsequent zoom gestures; this
      // call ensures the signal is correct from the very first render.
      const initialZoom = this.map.getZoom();
      this.zoomLevel.set(initialZoom.toFixed(2));
      this.mapService.zoom.set(initialZoom);
      const c = this.map.getCenter();
      this.panPosition.set(`${Math.round(c.lng)}, ${Math.round(c.lat)}`);

      // Spawn one ProvinceComponent per province, providing the DTO directly
      // into the component's injector so it can be resolved in the constructor.
      for (const province of provinces) {
        const injector = Injector.create({
          providers: [{ provide: PROVINCE_DTO, useValue: province }],
          parent: this.locationHost.injector,
        });
        const ref = this.locationHost.createComponent(ProvinceComponent, { injector });
        this.provinceRefs.push(ref);
      }
    });
  }

  // ── Lifecycle ──────────────────────────────────────────────────────────────

  ngOnDestroy(): void {
    this.provinceRefs.forEach(ref => ref.destroy());
    this.map?.remove();
    this.mapService.map = null;
  }
}
