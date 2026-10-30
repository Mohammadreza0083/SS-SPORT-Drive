import { describe, expect, it } from "vitest";
import { MAX_PINCH, ZOOM_LEVELS, pinchZoom, stepZoom } from "./zoom";

describe("ZOOM_LEVELS", () => {
  it("goes from 50 % to 200 % as on the canvas", () => {
    expect(ZOOM_LEVELS).toEqual([50, 75, 100, 125, 150, 200]);
  });
});

describe("stepZoom", () => {
  it("moves one level in or out", () => {
    expect(stepZoom(100, 1)).toBe(125);
    expect(stepZoom(100, -1)).toBe(75);
  });

  it("stops at the ends", () => {
    expect(stepZoom(200, 1)).toBe(200);
    expect(stepZoom(50, -1)).toBe(50);
  });

  it("snaps an off-list value to the nearest level first", () => {
    expect(stepZoom(110, 1)).toBe(125);
  });
});

describe("pinchZoom", () => {
  it("scales with the distance between the two fingers", () => {
    expect(pinchZoom(1, 100, 150)).toBeCloseTo(1.5);
  });

  it("never shrinks the page below its fitted width or grows it past 3x", () => {
    expect(pinchZoom(1, 100, 40)).toBe(1);
    expect(pinchZoom(2, 100, 400)).toBe(MAX_PINCH);
  });

  it("keeps the zoom when the fingers have not moved apart yet", () => {
    expect(pinchZoom(1.4, 0, 0)).toBe(1.4);
  });
});
