import { describe, expect, it } from "vitest";
import { PLAYER_POSITIONS, PLAYER_STATUSES, parsePlayersQuery, playersApiPath, playersCsvHref, playersHref, withFilter } from "./players-query";

/**
 * The players list keeps its search, filters and page in the address bar (A-3, decisions §2b), so a filtered view can
 * be bookmarked, shared and reloaded. Bad values from the address are dropped, never sent to the API; defaults stay out
 * of the URL; changing a filter goes back to page 1; the CSV export uses the same filters without paging.
 */

describe("parsePlayersQuery", () => {
  it("reads search, position, status, page and page size", () => {
    expect(parsePlayersQuery(new URLSearchParams("q=arash&position=OPP&status=live&page=3&pageSize=50"))).toEqual({
      q: "arash",
      position: "OPP",
      status: "live",
      page: 3,
      pageSize: 50,
    });
  });

  it("falls back to the defaults for missing or bad values", () => {
    expect(parsePlayersQuery(new URLSearchParams("position=XX&status=hidden&page=-2&pageSize=500&q=%20%20"))).toEqual({
      q: "",
      position: null,
      status: null,
      page: 1,
      pageSize: 25,
    });
  });

  it("accepts a plain record as Next.js gives searchParams, taking the first of repeated values", () => {
    expect(parsePlayersQuery({ q: ["kian", "x"], position: "MB", page: "2" })).toMatchObject({ q: "kian", position: "MB", page: 2 });
  });

  it("trims the search and caps it at 100 characters", () => {
    expect(parsePlayersQuery(new URLSearchParams(`q=${"a".repeat(150)}`)).q).toHaveLength(100);
  });

  it("knows the canvas positions and console statuses", () => {
    expect(PLAYER_POSITIONS).toEqual(["S", "OH", "OPP", "MB", "L", "DS"]);
    expect(PLAYER_STATUSES).toEqual(["live", "processing", "attention", "waiting"]);
  });
});

describe("playersHref", () => {
  it("leaves defaults out of the address", () => {
    expect(playersHref({ q: "", position: null, status: null, page: 1, pageSize: 25 })).toBe("/admin/players");
  });

  it("writes only what differs from the defaults, in a stable order", () => {
    expect(playersHref({ q: "tehrani", position: "OPP", status: "live", page: 2, pageSize: 50 })).toBe(
      "/admin/players?q=tehrani&position=OPP&status=live&page=2&pageSize=50",
    );
  });
});

describe("withFilter", () => {
  it("goes back to page 1 when a filter changes", () => {
    const query = { q: "", position: null, status: null, page: 4, pageSize: 25 };

    expect(withFilter(query, { position: "MB" })).toEqual({ ...query, position: "MB", page: 1 });
  });

  it("clears a filter with null (the All chip)", () => {
    expect(withFilter({ q: "", position: "MB", status: "live", page: 1, pageSize: 25 }, { position: null })).toMatchObject({ position: null, status: "live" });
  });
});

describe("playersApiPath and playersCsvHref", () => {
  it("asks the API for the same page and filters", () => {
    expect(playersApiPath({ q: "a b", position: "S", status: "waiting", page: 2, pageSize: 25 })).toBe(
      "/admin/players?page=2&pageSize=25&q=a+b&position=S&status=waiting",
    );
  });

  it("exports the same filters as CSV, without paging, through the same-origin route", () => {
    expect(playersCsvHref({ q: "a", position: null, status: "live", page: 3, pageSize: 50 })).toBe("/api/admin/players.csv?q=a&status=live");
  });
});
