import { describe, expect, it } from "vitest";
import worker from "./index";

/**
 * Sends a GET request for the given path to the worker.
 *
 * @param path - Path on the edge host.
 * @returns The worker's response.
 */
function get(path: string): Promise<Response> {
  return Promise.resolve(worker.fetch(new Request(`https://drive.ss-sport.co${path}`)));
}

describe("edge worker", () => {
  it("answers the health probe with 200", async () => {
    const response = await get("/health");

    expect(response.status).toBe(200);
  });

  it("returns 404 for unknown paths", async () => {
    const response = await get("/does-not-exist");

    expect(response.status).toBe(404);
  });
});
