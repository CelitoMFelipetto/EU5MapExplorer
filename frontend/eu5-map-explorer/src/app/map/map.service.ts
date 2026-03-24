import { inject, Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { map, Observable } from 'rxjs';
import * as L from 'leaflet';
import {
  ApiMapResponse,
  LocationDto,
  MapDataDto,
  PathCoordinates,
  ProvinceDto,
} from './models/location.dto';

@Injectable({ providedIn: 'root' })
export class MapService {
  private readonly http = inject(HttpClient);
  public mapHeight = 0;

  /** The active Leaflet map instance. Set by MapComponent, cleared on destroy. */
  map: L.Map | null = null;

  /**
   * Fetches map data from GET /api/map and converts it into the MapDataDto
   * shape expected by MapComponent / ProvinceComponent / LocationComponent.
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
      for (const path of province.paths) {
        for (const [x, y] of path) {
          if (x > maxX) maxX = x;
          if (y > maxY) maxY = y;
        }
      }
      for (const location of province.locations) {
        for (const path of location.paths) {
          for (const [x, y] of path) {
            if (x > maxX) maxX = x;
            if (y > maxY) maxY = y;
          }
        }
      }
    }

    const svgWidth  = maxX;
    const svgHeight = maxY;
    this.mapHeight  = svgHeight;

    const flip = ([x, y]: number[]): PathCoordinates => [svgHeight - y, x];

    // ── Pass 2: build ProvinceDtos and LocationDtos ───────────────────────────
    const provinces: ProvinceDto[] = [];

    for (const apiProvince of response.provinces) {
      // Build the ProvinceDto shell first so LocationDtos can reference it.
      const provinceDto: ProvinceDto = {
        id:        apiProvince.name,
        paths:     apiProvince.paths.map(path => path.map(flip)),
        locations: [],
      };

      provinceDto.locations = apiProvince.locations.map(loc => {
        const locationDto: LocationDto = {
          id:           loc.name,
          color:        `#${loc.color}`,
          topography:   loc.topography,
          climate:      loc.climate,
          vegetation:   loc.vegetation,
          raw_material: loc.raw_material,
          paths:        loc.paths.map(path => path.map(flip)),
          province:     provinceDto,
        };
        return locationDto;
      });

      provinces.push(provinceDto);
    }

    return { svgWidth, svgHeight, provinces };
  }
}
