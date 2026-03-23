import { inject, Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { map, Observable } from 'rxjs';
import { LocationDto, MapDataDto, PathCoordinates, PolygonPath } from './models/location.dto';

@Injectable({ providedIn: 'root' })
export class MapService {
  private readonly http = inject(HttpClient);
  public mapHeight = 0;

  /**
   * Fetches the statically-served SVG and parses it into structured map data.
   * Each unique fill colour in the SVG becomes one LocationDto, collecting all
   * closed path loops that belong to that colour.
   */
  getMapData(): Observable<MapDataDto> {
    return this.http.get('/extract-map.svg', { responseType: 'text' }).pipe(
      map(svgText => this.parseSvg(svgText)),
    );
  }

  private parseSvg(svgText: string): MapDataDto {
    const doc = new DOMParser().parseFromString(svgText, 'image/svg+xml');
    const svgEl = doc.documentElement;

    const svgWidth  = parseFloat(svgEl.getAttribute('width')  ?? '0');
    const svgHeight = parseFloat(svgEl.getAttribute('height') ?? '0');
    this.mapHeight = svgHeight;

    // Group path `d` strings by fill colour.
    const byColor = new Map<string, string[]>();
    doc.querySelectorAll('path').forEach(path => {
      const color = path.getAttribute('fill') ?? '';
      const d     = path.getAttribute('d')    ?? '';
      if (!color || !d) return;
      if (!byColor.has(color)) byColor.set(color, []);
      byColor.get(color)!.push(d);
    });

    const locations: LocationDto[] = Array.from(byColor.entries()).map(([color, paths]) => ({
      id: color.replace('#', ''),
      color,
      paths: this.svgPathToPolygon(paths),
    }));

    console.log("returning locations", locations);

    return { svgWidth, svgHeight, locations };
  }

  private svgPathToPolygon(original: string[]): PolygonPath {
    // For now we are ignoring evenodd property
    const result:PolygonPath = original.reduce((polygons, path) => {
      const subpaths = path.replaceAll(' ', '').split('ZM');
      subpaths.forEach((subpath) => {
        const coordinates = subpath.replaceAll('M', '').replaceAll('Z','').split('L');
        polygons.push(coordinates.map((coordinate) => {
          const [x, y] = coordinate.split(',');
          return [this.mapHeight - Number(y), Number(x)] as PathCoordinates;
        }));
      });
      return polygons;
    }, [] as PathCoordinates[][]);
    return result;
  }
}
