import { describe, expect, it } from "vitest";
import { archiveHref, checkCsvText, formatBytes, MAX_CSV_CHARACTERS, readArchiveQuery } from "./archive";

/**
 * The archive page's helpers (Epic 8): sizes in the report, the `?state=` / `?page=` query, and the check the CSV file
 * passes in the browser before it is sent (the API checks again: at most 1 MB of text, not empty).
 */

describe("formatBytes", () => {
  it.each([
    [0, "0 B"],
    [999, "999 B"],
    [1_500, "1.5 KB"],
    [4_000_000_000, "4.0 GB"],
    [107_374_182_400, "107.4 GB"],
  ])("writes %d bytes as %s", (bytes, text) => {
    expect(formatBytes(bytes)).toBe(text);
  });
});

describe("readArchiveQuery", () => {
  it("keeps a known state and a page", () => {
    expect(readArchiveQuery({ state: "new", page: "3" })).toEqual({ state: "new", page: 3 });
  });

  it("drops an unknown state and a bad page", () => {
    expect(readArchiveQuery({ state: "gone", page: "-2" })).toEqual({ state: null, page: 1 });
  });
});

describe("archiveHref", () => {
  it("writes only what is set", () => {
    expect(archiveHref({ state: null, page: 1 })).toBe("/admin/archive");
    expect(archiveHref({ state: "imported", page: 2 })).toBe("/admin/archive?state=imported&page=2");
  });
});

describe("checkCsvText", () => {
  it("accepts a file with text", () => {
    expect(checkCsvText("Id,Name\n1,Ali")).toBeNull();
  });

  it("refuses an empty file", () => {
    expect(checkCsvText("  \n")).toBe("empty");
  });

  it("refuses a file over 1 MB", () => {
    expect(checkCsvText("a".repeat(MAX_CSV_CHARACTERS + 1))).toBe("tooLarge");
  });
});
