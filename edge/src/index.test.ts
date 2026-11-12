import { afterEach, describe, expect, it, vi } from "vitest";
import worker, { type Env } from "./index";

/**
 * Sends a GET request for the given path to the worker.
 *
 * @param path - Path on the edge host.
 * @returns The worker's response.
 */
function get(path: string): Promise<Response> {
  return worker.fetch(new Request(`https://drive.ss-sport.co${path}`), {} as Env);
}

afterEach(() => {
  vi.unstubAllGlobals();
});

describe("edge worker", () => {
  it("answers the health probe with 200", async () => {
    const response = await get("/health");

    expect(response.status).toBe(200);
  });

  // V-5b (owner, 2026-11-12): paths the edge does not serve itself go on to the viewer site, which answers its own 404s.
  it("passes unknown paths on to the site", async () => {
    const site = vi.fn(async () => new Response("Not found", { status: 404 }));
    vi.stubGlobal("fetch", site);

    const response = await get("/does-not-exist");

    expect(site).toHaveBeenCalledTimes(1);
    expect(response.status).toBe(404);
  });
});
