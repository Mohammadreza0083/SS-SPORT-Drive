import { describe, expect, it } from "vitest";
import { formatPercent, formatRelative, formatWatchTime, sessionDaysLeft } from "./format";

/**
 * How the console writes times and numbers, as on the canvas: watch time `41m 10s`, relative times `now`, `2 min ago`,
 * `1 h ago`, `Yesterday`, `3 days ago`, then a short date `12 Oct`; completion as a whole percent.
 */

const NOW = new Date("2026-11-14T10:00:00Z");

describe("formatWatchTime", () => {
  it.each([
    [0, "0m"],
    [55, "0m 55s"],
    [2470, "41m 10s"],
    [3600, "1h 0m"],
    [6490, "1h 48m"],
  ])("writes %d seconds as %s", (seconds, text) => {
    expect(formatWatchTime(seconds)).toBe(text);
  });
});

describe("formatRelative", () => {
  it.each([
    ["2026-11-14T09:59:30Z", "now"],
    ["2026-11-14T09:58:00Z", "2 min ago"],
    ["2026-11-14T09:00:00Z", "1 h ago"],
    ["2026-11-13T09:00:00Z", "Yesterday"],
    ["2026-11-11T09:00:00Z", "3 days ago"],
    ["2026-10-12T09:00:00Z", "12 Oct"],
  ])("writes %s as %s", (at, text) => {
    expect(formatRelative(at, NOW)).toBe(text);
  });

  it("writes a missing time as a dash", () => {
    expect(formatRelative(null, NOW)).toBe("—");
  });
});

describe("formatPercent", () => {
  it("rounds to a whole percent and shows a dash for no data", () => {
    expect(formatPercent(0.716)).toBe("72%");
    expect(formatPercent(1)).toBe("100%");
    expect(formatPercent(null)).toBe("—");
  });
});

describe("sessionDaysLeft", () => {
  it("counts whole days left on the admin session, as the nav card shows", () => {
    expect(sessionDaysLeft("2026-11-20T11:00:00Z", NOW)).toBe(6);
    expect(sessionDaysLeft("2026-11-14T12:00:00Z", NOW)).toBe(0);
  });
});
