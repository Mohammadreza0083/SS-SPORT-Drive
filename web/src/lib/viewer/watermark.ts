/**
 * The moving viewer-email overlay (backlog V-4; values approved in decisions §6, 2026-10-24).
 * Positions and sizes are fractions of the video frame, so they hold at every size and in fullscreen.
 */
export const WATERMARK = {
  /** Opacity of the email text. */
  opacity: 0.2,
  /** Text height as a share of the frame height. */
  heightRatio: 0.022,
  /** How often the email moves. */
  intervalMs: 8000,
  /** The cross-fade around each move (half out, half in). */
  fadeMs: 500,
} as const;

/** A rectangle in frame fractions: x and y from the top-left corner, w and h as widths. */
export type Rect = { x: number; y: number; w: number; h: number };

/**
 * Corners the email never moves into, because the burned-in watermark lives there (decisions §6):
 * the logo top-left (10 % of the width, 2.5 % margins) and the slogan bottom-right (about a quarter of the
 * width, 3.2 % of the height, 2.5 % margins), each with some room to spare.
 */
export const FORBIDDEN_ZONES: readonly Rect[] = [
  { x: 0, y: 0, w: 0.17, h: 0.14 },
  { x: 0.68, y: 0.86, w: 0.32, h: 0.14 },
];

/** How many random spots to try before falling back to the centre. */
const MAX_TRIES = 40;

/**
 * Whether two rectangles share any area (touching edges do not count).
 *
 * @param a - One rectangle.
 * @param b - The other.
 * @returns True when they overlap.
 */
export function overlaps(a: Rect, b: Rect): boolean {
  return a.x < b.x + b.w && b.x < a.x + a.w && a.y < b.y + b.h && b.y < a.y + a.h;
}

/**
 * Picks a random spot for the email label: fully inside the frame and clear of both watermark corners.
 *
 * @param label - The label's size in frame fractions.
 * @param random - Random numbers in [0, 1); `Math.random` by default.
 * @returns The label's top-left corner in frame fractions.
 */
export function pickPosition(label: { w: number; h: number }, random: () => number = Math.random): { x: number; y: number } {
  const maxX = Math.max(0, 1 - label.w);
  const maxY = Math.max(0, 1 - label.h);
  for (let i = 0; i < MAX_TRIES; i++) {
    const at = { x: random() * maxX, y: random() * maxY };
    if (!FORBIDDEN_ZONES.some((zone) => overlaps({ ...at, ...label }, zone))) {
      return at;
    }
  }
  return { x: maxX / 2, y: maxY / 2 };
}
