import {
  AfterViewInit,
  Component,
  ComponentRef,
  DestroyRef,
  ElementRef,
  inject,
  Injector,
  OnDestroy,
  signal,
  ViewChild,
  ViewContainerRef,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import * as L from 'leaflet';
import { MapService } from './map.service';
import { BorderLayerService } from './border-layer.service';
import { ViewportCullingService } from './viewport-culling.service';
import { ProvinceComponent } from './province/province.component';
import { MAP_PANES } from './map-panes';
import { PROVINCE_DTO } from './map-tokens';
import { MAP_MODES, MapMode } from './map-mode';
import { ProvinceDto } from './models/location.dto';

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
          [title]="mode.label"
          (click)="setMode(mode.id)">
          <img class="layer-bg"   src="map_modes/map_mode_bg.png"    alt="">
          <img class="layer-trim" [src]="'map_modes/' + mode.trim"   alt="">
          <img class="layer-icon" [src]="'map_modes/' + mode.icon"   alt="">
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
      gap: 4px;
      background: rgba(20, 15, 10, 0.75);
      border: 1px solid rgba(255, 220, 150, 0.25);
      border-radius: 6px;
      padding: 6px;
      box-shadow: 0 2px 12px rgba(0, 0, 0, 0.5);
    }

    .mode-btn {
      position: relative;
      width: 57px;
      height: 57px;
      padding: 0;
      border: none;
      background: transparent;
      cursor: pointer;
      border-radius: 3px;
      transition: transform 0.1s;
    }

    .mode-btn:hover {
      transform: scale(1.08);
    }

    .mode-btn .layer-bg,
    .mode-btn .layer-trim,
    .mode-btn .layer-icon {
      position: absolute;
      display: block;
      pointer-events: none;
    }

    /* bg and trim fill the whole button */
    .mode-btn .layer-bg,
    .mode-btn .layer-trim {
      top: 0; left: 0;
      width: 100%; height: 100%;
    }

    /* icon is centered at ~52% of button size */
    .mode-btn .layer-icon {
      width: 52%;
      height: 52%;
      top: 24%;
      left: 24%;
    }

    /* selected: brighten bg + trim layers */
    .mode-btn.active .layer-bg,
    .mode-btn.active .layer-trim {
      filter: brightness(1.6) saturate(1.2);
    }

    /* unselected: dim slightly so selected stands out */
    .mode-btn:not(.active) .layer-bg,
    .mode-btn:not(.active) .layer-trim {
      filter: brightness(0.75);
    }
  `],
})
export class MapComponent implements AfterViewInit, OnDestroy {
  @ViewChild('mapEl') mapEl!: ElementRef<HTMLDivElement>;
  @ViewChild('tooltipEl') tooltipEl!: ElementRef<HTMLDivElement>;
  @ViewChild('locationHost', { read: ViewContainerRef })
  locationHost!: ViewContainerRef;

  protected readonly mapService     = inject(MapService);
  private readonly borderLayerService = inject(BorderLayerService);
  private readonly viewportCulling  = inject(ViewportCullingService);
  private readonly destroyRef       = inject(DestroyRef);
  private map?: L.Map;
  private provinceRefs: ComponentRef<ProvinceComponent>[] = [];

  readonly zoomLevel   = signal('—');
  readonly panPosition = signal('—, —');
  readonly modes = MAP_MODES;

  setMode(mode: MapMode): void {
    this.mapService.setMapMode(mode);
  }

  ngAfterViewInit(): void {
    // Subscribe BEFORE startRadialLoad so the first emission is never missed.
    this.mapService.areaLoaded$
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(({ svgWidth, svgHeight, provinces }) => {
        if (!this.map) {
          this.initMap(svgWidth, svgHeight);
        }
        this.renderProvinces(provinces);
      });

    this.mapService.startRadialLoad('atlantic_south_equatorial_area');
  }

  // Full map pixel dimensions (source image: locations.png).
  private static readonly MAP_WIDTH = 16384;
  private static readonly MAP_HEIGHT = 8192;

  private initMap(_svgWidth: number, _svgHeight: number): void {
    const MAP_WIDTH = MapComponent.MAP_WIDTH;
    const MAP_HEIGHT = MapComponent.MAP_HEIGHT;

    // Build a minimal SVG shell — LocationComponents fill it with <path> elements.
    const ns = 'http://www.w3.org/2000/svg';
    const svgEl = document.createElementNS(ns, 'svg') as SVGSVGElement;
    svgEl.setAttribute('width', String(MAP_WIDTH));
    svgEl.setAttribute('height', String(MAP_HEIGHT));
    svgEl.setAttribute('viewBox', `0 0 ${MAP_WIDTH} ${MAP_HEIGHT}`);

    // The padding translate matches the 2px pad baked into the SVG coordinates.
    const group = document.createElementNS(ns, 'g') as SVGGElement;
    group.setAttribute('transform', 'translate(2,2)');
    svgEl.appendChild(group);

    const mapBounds: L.LatLngBoundsExpression = [[0, 0], [MAP_HEIGHT, MAP_WIDTH]];

    this.map = L.map(this.mapEl.nativeElement, {
      crs: L.CRS.Simple,
      minZoom: -1,
      maxZoom: 5,
      zoomSnap: 0.25,
      attributionControl: false,
      maxBounds: mapBounds,
      maxBoundsViscosity: 1.0,
    });

    // Read the saved view BEFORE assigning the map to the service.
    const savedView = this.mapService.getSavedView();

    // Publish the map instance — attaches sessionStorage persistence listeners.
    this.mapService.map = this.map;

    const updatePan = () => {
      const c = this.map!.getCenter();
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
    L.svgOverlay(svgEl, mapBounds).addTo(this.map);

    if (savedView) {
      this.map.setView(savedView.center, savedView.zoom, { animate: false });
    } else {
      this.map.fitBounds(mapBounds);
    }

    const initialZoom = this.map.getZoom();
    this.zoomLevel.set(initialZoom.toFixed(2));
    this.mapService.zoom.set(initialZoom);
    const c = this.map.getCenter();
    this.panPosition.set(`${Math.round(c.lng)}, ${Math.round(c.lat)}`);
  }

  private renderProvinces(provinces: ProvinceDto[]): void {
    // Register border polylines before spawning province components so all
    // polylines exist when location components start reacting to highlights.
    this.borderLayerService.registerArea(provinces);

    // Populate the visibility registry and write the initial visible set to the
    // signal BEFORE any LocationComponent is constructed (effects need the signal).
    this.viewportCulling.registerProvinces(provinces);

    for (const province of provinces) {
      const injector = Injector.create({
        providers: [{ provide: PROVINCE_DTO, useValue: province }],
        parent: this.locationHost.injector,
      });
      const ref = this.locationHost.createComponent(ProvinceComponent, { injector });
      this.provinceRefs.push(ref);
    }
  }

  // ── Lifecycle ──────────────────────────────────────────────────────────────

  ngOnDestroy(): void {
    this.provinceRefs.forEach(ref => ref.destroy());
    this.map?.remove();
    this.mapService.map = null;
  }
}
