import { afterEach, describe, expect, it, vi } from "vitest";
import { PATCH } from "./route";

const PLAYER_ID = "0199f2b1-2d4e-7c8a-b1f3-5e6a7b8c9d0e";
const FILE_ID = "0199f4a1-3b2c-7d4e-8f5a-6b7c8d9e0f1a";

/**
 * Builds the route context Next.js passes in.
 *
 * @param id - The player id.
 * @param fileId - The file id.
 * @returns The context.
 */
function context(id: string, fileId: string) {
  return { params: Promise.resolve({ id, fileId }) } as RouteContext<"/api/miniapp/players/[id]/files/[fileId]">;
}

/**
 * Builds the sheet's PATCH request.
 *
 * @returns The request.
 */
function patch() {
  return new Request("http://localhost/api/miniapp/players/x/files/y", {
    method: "PATCH",
    headers: { Authorization: "tma signed", "Content-Type": "application/json" },
    body: JSON.stringify({ title: "Best spikes" }),
  });
}

afterEach(() => {
  vi.unstubAllGlobals();
});

describe("PATCH /api/miniapp/players/{id}/files/{fileId}", () => {
  it("forwards the edit to the API's file path", async () => {
    const fetchMock = vi.fn().mockResolvedValue(Response.json({ id: FILE_ID }));
    vi.stubGlobal("fetch", fetchMock);

    const response = await PATCH(patch(), context(PLAYER_ID, FILE_ID));

    expect(response.status).toBe(200);
    expect(fetchMock).toHaveBeenCalledWith(`http://localhost:5080/miniapp/players/${PLAYER_ID}/files/${FILE_ID}`, expect.objectContaining({ method: "PATCH" }));
  });

  it("answers 404 without calling the API when either id is not a GUID", async () => {
    const fetchMock = vi.fn();
    vi.stubGlobal("fetch", fetchMock);

    const badFile = await PATCH(patch(), context(PLAYER_ID, "..%2F..%2Fme"));
    const badPlayer = await PATCH(patch(), context("not-a-guid", FILE_ID));

    expect(badFile.status).toBe(404);
    expect(badPlayer.status).toBe(404);
    expect(fetchMock).not.toHaveBeenCalled();
  });
});
