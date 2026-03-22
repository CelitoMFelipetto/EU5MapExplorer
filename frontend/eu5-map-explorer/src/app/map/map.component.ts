import { Component, AfterViewInit, ElementRef, ViewChild, inject, OnDestroy } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import * as L from 'leaflet';

@Component({
  selector: 'app-map',
  standalone: true,
  template: `
    <div #mapEl class="map-container"></div>
    <div #tooltipEl class="map-tooltip"></div>
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
  `],
})
export class MapComponent implements AfterViewInit, OnDestroy {
  @ViewChild('mapEl')      mapEl!:      ElementRef<HTMLDivElement>;
  @ViewChild('tooltipEl')  tooltipEl!:  ElementRef<HTMLDivElement>;

  private readonly http = inject(HttpClient);
  private map?: L.Map;
  private cleanupFns: (() => void)[] = [];

  ngAfterViewInit(): void {
    this.http.get('/extract-map.svg', { responseType: 'text' }).subscribe(svgText => {
      const parser = new DOMParser();
      const svgEl = parser.parseFromString(svgText, 'image/svg+xml')
        .documentElement as unknown as SVGSVGElement;

      const w = parseFloat(svgEl.getAttribute('width') ?? '800');
      const h = parseFloat(svgEl.getAttribute('height') ?? '600');

      // The script outputs a background <rect> for standalone viewing — remove it
      // so the Leaflet map background shows through.
      svgEl.querySelector('rect')?.remove();

      // Apply a permanent stroke (slightly darker than each path's fill) to all paths.
      svgEl.querySelectorAll<SVGPathElement>('path').forEach(path => {
        const fill = path.getAttribute('fill') ?? '#000000';
        path.setAttribute('stroke', this.darkenColor(fill, 0.35));
        path.setAttribute('stroke-width', '1');
        path.setAttribute('stroke-linejoin', 'round');
        // Enable pointer events per-path; the SVG container keeps pointer-events:none
        // (set by Leaflet) so empty-area clicks still pan the map normally.
        path.style.pointerEvents = 'all';
      });

      this.map = L.map(this.mapEl.nativeElement, {
        crs: L.CRS.Simple,
        minZoom: -5,
        maxZoom: 5,
        zoomSnap: 0.25,
        attributionControl: false,
      });

      // CRS.Simple: lat increases upward, so image top-left → NW = [h, 0],
      // image bottom-right → SE = [0, w]. Bounds [[SW], [NE]] = [[0,0],[h,w]].
      const bounds: L.LatLngBoundsExpression = [[0, 0], [h, w]];
      L.svgOverlay(svgEl, bounds).addTo(this.map);
      this.map.fitBounds(bounds);

      this.setupInteractivity(svgEl);
    });
  }

  // ── Hover interactivity ────────────────────────────────────────────────────

  private setupInteractivity(svgEl: SVGSVGElement): void {
    const tooltip = this.tooltipEl.nativeElement;
    let active: SVGPathElement | null = null;

    // Per-path enter / leave — efficient because path count is bounded.
    svgEl.querySelectorAll<SVGPathElement>('path').forEach(path => {
      const fill = path.getAttribute('fill') ?? '';

      const onEnter = () => {
        if (active && active !== path) this.resetPath(active);
        active = path;
        path.style.filter = 'brightness(1.35)';
        path.style.strokeWidth = '2';
        tooltip.textContent = fill;
        tooltip.style.display = 'block';
      };

      const onLeave = () => {
        this.resetPath(path);
        active = null;
        tooltip.style.display = 'none';
      };

      path.addEventListener('mouseenter', onEnter);
      path.addEventListener('mouseleave', onLeave);
      this.cleanupFns.push(() => {
        path.removeEventListener('mouseenter', onEnter);
        path.removeEventListener('mouseleave', onLeave);
      });
    });

    // Track cursor on the SVG so the tooltip follows the mouse.
    // mousemove bubbles from paths through the SVG even when the SVG itself
    // has pointer-events:none (set by Leaflet).
    const onMove = (e: MouseEvent) => {
      tooltip.style.left = `${e.clientX + 14}px`;
      tooltip.style.top  = `${e.clientY - 36}px`;
    };
    svgEl.addEventListener('mousemove', onMove);
    this.cleanupFns.push(() => svgEl.removeEventListener('mousemove', onMove));
  }

  private resetPath(path: SVGPathElement): void {
    path.style.filter      = '';
    path.style.strokeWidth = '';
  }

  // ── Colour helpers ─────────────────────────────────────────────────────────

  /** Returns an RGB string that is `amount` (0–1) darker than the given hex colour. */
  private darkenColor(hex: string, amount = 0.35): string {
    const r = Math.round(parseInt(hex.slice(1, 3), 16) * (1 - amount));
    const g = Math.round(parseInt(hex.slice(3, 5), 16) * (1 - amount));
    const b = Math.round(parseInt(hex.slice(5, 7), 16) * (1 - amount));
    return `rgb(${r},${g},${b})`;
  }

  // ── Lifecycle ──────────────────────────────────────────────────────────────

  ngOnDestroy(): void {
    this.cleanupFns.forEach(fn => fn());
    this.map?.remove();
  }
}
