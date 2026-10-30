import { describe, expect, it } from "vitest";
import { FORBIDDEN_ZONES, WATERMARK, overlaps, pickPosition, type Rect } from "./watermark";

/**
 * A small seeded random generator (mulberry32), so the tests are repeatable.
 *
 * @param seed - The seed.
 * @returns A function giving numbers in [0, 1).
 */
function seeded(seed: number): () => number {
  let a = seed;
  return () => {
    a = (a + 0x6d2b79f5) | 0;
    let t = Math.imul(a ^ (a >>> 15), 1 | a);
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

describe("WATERMARK (decisions §6, approved 2026-10-24)", () => {
  it("shows the email at 20 % opacity, 2.2 % of the frame height", () => {
    expect(WATERMARK.opacity).toBe(0.2);
    expect(WATERMARK.heightRatio).toBe(0.022);
  });

  it("moves every 8 seconds with a 0.5 second cross-fade", () => {
    expect(WATERMARK.intervalMs).toBe(8000);
    expect(WATERMARK.fadeMs).toBe(500);
  });
});

describe("FORBIDDEN_ZONES", () => {
  it("covers the burned-in logo (top-left) and slogan (bottom-right) corners", () => {
    const [logo, slogan] = FORBIDDEN_ZONES;
    expect(logo.x).toBe(0);
    expect(logo.y).toBe(0);
    expect(slogan.x + slogan.w).toBeCloseTo(1);
    expect(slogan.y + slogan.h).toBeCloseTo(1);
  });
});

describe("overlaps", () => {
  it("detects rectangles that share an area", () => {
    expect(overlaps({ x: 0, y: 0, w: 0.5, h: 0.5 }, { x: 0.4, y: 0.4, w: 0.2, h: 0.2 })).toBe(true);
  });

  it("treats rectangles that only touch as apart", () => {
    expect(overlaps({ x: 0, y: 0, w: 0.5, h: 0.5 }, { x: 0.5, y: 0, w: 0.2, h: 0.2 })).toBe(false);
  });
});

describe("pickPosition", () => {
  const label = { w: 0.3, h: 0.04 };

  it("always lands inside the frame and outside both watermark corners", () => {
    const random = seeded(42);
    for (let i = 0; i < 2000; i++) {
      const at = pickPosition(label, random);
      const rect: Rect = { x: at.x, y: at.y, ...label };
      expect(at.x).toBeGreaterThanOrEqual(0);
      expect(at.y).toBeGreaterThanOrEqual(0);
      expect(at.x + label.w).toBeLessThanOrEqual(1);
      expect(at.y + label.h).toBeLessThanOrEqual(1);
      for (const zone of FORBIDDEN_ZONES) {
        expect(overlaps(rect, zone)).toBe(false);
      }
    }
  });

  it("tries again when a draw falls on the logo", () => {
    const draws = [0, 0, 0.5, 0.5];
    const at = pickPosition(label, () => draws.shift() ?? 0.5);

    expect(at).toEqual({ x: 0.5 * (1 - label.w), y: 0.5 * (1 - label.h) });
  });

  it("spreads over the frame instead of sticking to one spot", () => {
    const random = seeded(7);
    const xs = new Set(Array.from({ length: 50 }, () => Math.round(pickPosition(label, random).x * 10)));

    expect(xs.size).toBeGreaterThan(3);
  });

  it("falls back to the centre when nothing fits", () => {
    const at = pickPosition({ w: 0.99, h: 0.99 }, () => 0);

    expect(at.x).toBeCloseTo(0.005);
    expect(at.y).toBeCloseTo(0.005);
  });
});
