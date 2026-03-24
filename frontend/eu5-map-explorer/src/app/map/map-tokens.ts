import { InjectionToken } from '@angular/core';
import { LocationDto, ProvinceDto } from './models/location.dto';

export const PROVINCE_DTO = new InjectionToken<ProvinceDto>('ProvinceDto');
export const LOCATION_DTO = new InjectionToken<LocationDto>('LocationDto');
