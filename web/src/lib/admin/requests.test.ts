import { describe, expect, it } from "vitest";
import { buildApproval, readRequestsQuery, requesterInitials, requestsHref } from "./requests";

/**
 * The access-request inbox (A-12, canvas `AdminRequests`) keeps its tab, page and open request in the address bar, so a
 * view can be reloaded and shared. Values from the address are checked; bad ones fall back to the defaults and never
 * reach the API. The approval body follows `POST /admin/access-requests/{id}/approve` (P-4): 7, 30 or 90 days, one to
 * twenty players, an optional note.
 */

const ID = "019a3e1c-5b2a-7f10-a4c3-8d9e0f1a2b3c";

describe("readRequestsQuery", () => {
  it("defaults to the pending tab, page 1 and no open request", () => {
    expect(readRequestsQuery({})).toEqual({ status: "pending", page: 1, id: null });
  });

  it("reads the tab, page and open request", () => {
    expect(readRequestsQuery({ status: "approved", page: "3", id: ID })).toEqual({ status: "approved", page: 3, id: ID });
  });

  it("accepts the tab in any case", () => {
    expect(readRequestsQuery({ status: "Declined" }).status).toBe("declined");
  });

  it("falls back on an unknown tab, a bad page and an id that is not a GUID", () => {
    expect(readRequestsQuery({ status: "all", page: "-2", id: "../users" })).toEqual({ status: "pending", page: 1, id: null });
    expect(readRequestsQuery({ page: "1.5" }).page).toBe(1);
    expect(readRequestsQuery({ page: "abc" }).page).toBe(1);
  });

  it("takes the first value of a repeated parameter", () => {
    expect(readRequestsQuery({ status: ["approved", "declined"] }).status).toBe("approved");
  });
});

describe("requestsHref", () => {
  it("leaves the defaults out", () => {
    expect(requestsHref({ status: "pending", page: 1, id: null })).toBe("/admin/requests");
  });

  it("writes the tab, page and open request", () => {
    expect(requestsHref({ status: "approved", page: 2, id: ID })).toBe(`/admin/requests?status=approved&page=2&id=${ID}`);
  });
});

describe("buildApproval", () => {
  it("sends the expiry, the players and the trimmed note", () => {
    expect(buildApproval({ playerIds: ["p1", "p2"], expiresInDays: 90, note: "  Full 2026 footage.  " })).toEqual({
      expiresInDays: 90,
      playerIds: ["p1", "p2"],
      note: "Full 2026 footage.",
    });
  });

  it("drops an empty note and repeated players", () => {
    expect(buildApproval({ playerIds: ["p1", "p1"], expiresInDays: 30, note: "   " })).toEqual({ expiresInDays: 30, playerIds: ["p1"] });
  });

  it("keeps at most twenty players", () => {
    const ids = Array.from({ length: 25 }, (_, index) => `p${index}`);
    expect(buildApproval({ playerIds: ids, expiresInDays: 7, note: "" }).playerIds).toHaveLength(20);
  });
});

describe("requesterInitials", () => {
  it("uses the first two letters of the address", () => {
    expect(requesterInitials("scout@vakifbank-volley.example")).toBe("SC");
  });

  it("skips symbols and copes with a one-letter name", () => {
    expect(requesterInitials("j.demir@club.example")).toBe("JD");
    expect(requesterInitials("x@club.example")).toBe("X");
  });
});
