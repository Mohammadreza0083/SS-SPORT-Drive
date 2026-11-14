import { describe, expect, it } from "vitest";
import { missingDetails, volleyboxId } from "./waiting";

/**
 * The waiting-list drawer's checks (A-5, canvas `Waiting`), the same rules as the API's `Player.MissingDetails()`:
 * photo, full name, nationality, at least one position (at most two) and a Volleybox player link ending in `-p<id>`.
 */

describe("volleyboxId", () => {
  it.each([
    ["volleybox.net/ali-hoshmand-p60214", 60214],
    ["https://volleybox.net/ali-hoshmand-p60214", 60214],
    ["https://www.volleybox.net/ali-hoshmand-p60214/clubs", 60214],
  ])("reads the id from %s", (url, id) => {
    expect(volleyboxId(url)).toBe(id);
  });

  it.each(["", "volleybox.net/ali-hoshmand", "https://example.com/ali-p60214", "volleybox.net/club-c123"])("finds no id in %s", (url) => {
    expect(volleyboxId(url)).toBeNull();
  });
});

describe("missingDetails", () => {
  const complete = { name: "Ali Hoshmand", nationalityCode: "IR", positions: ["OH"], volleyboxUrl: "volleybox.net/ali-hoshmand-p60214", hasPhoto: true };

  it("is empty for a complete player", () => {
    expect(missingDetails(complete)).toEqual([]);
  });

  it("lists what is missing in the API's order", () => {
    expect(missingDetails({ name: " ", nationalityCode: null, positions: [], volleyboxUrl: "volleybox.net/x", hasPhoto: false })).toEqual([
      "Name",
      "Nationality",
      "Positions",
      "Volleybox",
      "Photo",
    ]);
  });

  it("treats more than two positions as missing a valid choice", () => {
    expect(missingDetails({ ...complete, positions: ["OH", "OPP", "MB"] })).toEqual(["Positions"]);
  });
});
