import { describe, expect, it, vi } from "vitest";
import { createSignInClient } from "./sign-in-client";

/**
 * The browser side of asking for a sign-in link (W-1): it calls the same-origin `/api/auth/{audience}/link` route
 * with JSON and turns the answer into what the panel shows. The API answers 202 for every well-formed email, so
 * nobody can tell whether an address belongs to an admin.
 */
describe("createSignInClient", () => {
  it("posts the email as JSON to the same-origin route of its audience", async () => {
    const fetchImpl = vi.fn(async () => new Response(null, { status: 202 }));

    const result = await createSignInClient("admin", fetchImpl).requestLink("owner@ss-sport.co");

    expect(result).toEqual({ kind: "sent" });
    const [url, init] = fetchImpl.mock.calls[0] as unknown as [string, RequestInit];
    expect(url).toBe("/api/auth/admin/link");
    expect(init.method).toBe("POST");
    expect(new Headers(init.headers).get("Content-Type")).toBe("application/json");
    expect(JSON.parse(String(init.body))).toEqual({ email: "owner@ss-sport.co" });
  });

  it("reads a rate limit with its wait", async () => {
    const fetchImpl = vi.fn(async () => new Response(null, { status: 429, headers: { "Retry-After": "42" } }));

    expect(await createSignInClient("viewer", fetchImpl).requestLink("scout@club.com")).toEqual({ kind: "too-many", retryAfterSeconds: 42 });
  });

  it("reads a refused email as invalid", async () => {
    const bad = vi.fn(async () => new Response(null, { status: 400 }));
    const unprocessable = vi.fn(async () => new Response(null, { status: 422 }));

    expect(await createSignInClient("viewer", bad).requestLink("x")).toEqual({ kind: "invalid" });
    expect(await createSignInClient("viewer", unprocessable).requestLink("x")).toEqual({ kind: "invalid" });
  });

  it("reads anything else, and a network failure, as failed", async () => {
    const broken = vi.fn(async () => new Response(null, { status: 502 }));
    const offline = vi.fn(async () => {
      throw new TypeError("offline");
    });

    expect(await createSignInClient("viewer", broken).requestLink("scout@club.com")).toEqual({ kind: "failed" });
    expect(await createSignInClient("viewer", offline).requestLink("scout@club.com")).toEqual({ kind: "failed" });
  });
});
