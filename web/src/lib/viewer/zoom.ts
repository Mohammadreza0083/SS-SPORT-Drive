/** Zoom levels of the desktop document viewer, in percent (canvas `DocViewer`). */
export const ZOOM_LEVELS = [50, 75, 100, 125, 150, 200] as const;

/** The largest pinch zoom on phones, as a multiple of the fitted page width. */
export const MAX_PINCH = 3;

/**
 * Moves one zoom level in or out, stopping at the ends. A value between levels snaps to the nearest one first.
 *
 * @param current - The current zoom, in percent.
 * @param direction - 1 to zoom in, -1 to zoom out.
 * @returns The new zoom, in percent.
 */
export function stepZoom(current: number, direction: 1 | -1): number {
  const nearest = ZOOM_LEVELS.reduce((best, level, i) => (Math.abs(level - current) < Math.abs(ZOOM_LEVELS[best] - current) ? i : best), 0);
  const index = Math.min(ZOOM_LEVELS.length - 1, Math.max(0, nearest + direction));
  return ZOOM_LEVELS[index];
}

/**
 * The zoom while two fingers pinch: the zoom when the pinch began, scaled by how far the fingers moved apart,
 * kept between the fitted width (1) and {@link MAX_PINCH}.
 *
 * @param startZoom - The zoom when the pinch began.
 * @param startDistance - The distance between the fingers when the pinch began.
 * @param distance - The distance between the fingers now.
 * @returns The new zoom.
 */
export function pinchZoom(startZoom: number, startDistance: number, distance: number): number {
  if (startDistance <= 0) {
    return startZoom;
  }
  return Math.min(MAX_PINCH, Math.max(1, (startZoom * distance) / startDistance));
}
