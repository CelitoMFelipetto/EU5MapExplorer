import { inject, Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { map, Observable } from 'rxjs';
import {
  ApiMapResponse,
  LocationDto,
  MapDataDto,
  PathCoordinates,
} from './models/location.dto';

@Injectable({ providedIn: 'root' })
export class MapService {
  private readonly http = inject(HttpClient);
  public mapHeight = 0;

  /**
   * Fetches map data from GET /api/map and converts it into the MapDataDto
   * shape expected by MapComponent / LocationComponent.
   *
   * The API returns pixel-space coordinates [x, y] from the source PNG.
   * Leaflet's CRS.Simple expects [lat, lng] where lat increases upward, so
   * each point is remapped to [imageHeight - y, x].
   * The image dimensions are derived from the max coordinates in the data.
   */
  getMapData(): Observable<MapDataDto> {
    return this.http.get<ApiMapResponse>('/api/map').pipe(
      map(response => this.mapApiResponse(response)),
    );
  }

  private mapApiResponse(response: ApiMapResponse): MapDataDto {
    // ── Pass 1: derive image bounds from max coordinates ─────────────────────
    let maxX = 0;
    let maxY = 0;

    for (const province of response.provinces) {
      for (const location of province.locations) {
        for (const path of location.paths) {
          for (const [x, y] of path) {
            if (x > maxX) maxX = x;
            if (y > maxY) maxY = y;
          }
        }
      }
    }

    const svgWidth = maxX;
    const svgHeight = maxY;
    this.mapHeight = svgHeight;

    // ── Pass 2: build LocationDtos with Leaflet-space coordinates ─────────────
    const locations: LocationDto[] = [];

    for (const province of response.provinces) {
      for (const loc of province.locations) {
        const paths = loc.paths.map(path =>
          path.map(([x, y]) => [svgHeight - y, x] as PathCoordinates),
        );
        const { climate, topography, raw_material, vegetation } = loc;

        locations.push({
          id: loc.name,
          color: `#${loc.color}`,
          climate,
          topography,
          raw_material,
          vegetation,
          paths,
        });
      }
    }

    return { svgWidth, svgHeight, locations };
  }
}
