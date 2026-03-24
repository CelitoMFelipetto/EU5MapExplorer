import { Injectable, signal } from '@angular/core';

/**
 * Tracks which location is currently highlighted.
 * Using a signal guarantees that only one location is highlighted at a time:
 * when a new location is hovered the signal changes, and every LocationComponent
 * reacts via effect() — so stale highlights are always cleared even if the
 * previous mouseout event was never received.
 */
@Injectable({ providedIn: 'root' })
export class MapHighlightService {
  readonly highlightedLocationId = signal<string | null>(null);

  highlight(locationId: string): void {
    this.highlightedLocationId.set(locationId);
  }

  clear(): void {
    this.highlightedLocationId.set(null);
  }
}
