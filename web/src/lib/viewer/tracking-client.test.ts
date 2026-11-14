import { describe, expect, it, vi } from "vitest";
import { TrackingRequestError, createWatchTransport, watchPath } from "./tracking-client";

/**
 * The browser side of watch tracking (backlog W-2): the same-origin routes under a video's `…/watch` path, and the
 * transport the tracker sends through. Every call is a same-origin JSON POST (the routes' CSRF check), never cached;
 * heartbeats and events use `keepalive` so the last one still goes out when the viewer leaves the page.
 */

const PLAYER = "0b6f2c1e-1111-4a4a-8888-000000000001";
const VIDEO = "0b6f2c1e-2222-4a4a-8888-000000000002";
const WATCH = "019a3e1c-5b2a-7f10-a4c3-8d9e0f1a2b3c";
const PATH = `/api/links/Ab3xYz/players/${PLAYER}/videos/${VIDEO}/watch`;

/**
 * A fetch stub that answers every call the same way.
 *
 * @param status - The status.
 * @param body - The JSON body, or null for none.
 * @returns The stub.
 */
function api(status: number, body: unknown = null) {
  return vi.fn<typeof fetch>(async () => (body === null ? new Response(null, { status }) : Response.json(body, { status })));
}

/**
 * The URL and options of one recorded fetch call.
 *
 * @param fetchMock - The stub.
 * @param index - Which call.
 * @returns The URL and the options.
 */
function call(fetchMock: ReturnType<typeof api>, index = 0): [string, RequestInit] {
  return fetchMock.mock.calls[index] as unknown as [string, RequestInit];
}

describe("watchPath", () => {
  it("is the video's watch route on this site", () => {
    expect(watchPath("Ab3xYz", PLAYER, VIDEO)).toBe(PATH);
  });

  it("encodes the link code", () => {
    expect(watchPath("A b", PLAYER, VIDEO)).toContain("/api/links/A%20b/");
  });
});

describe("createWatchTransport", () => {
  it("starts a watch with a same-origin JSON POST and reads the watch id and interval", async () => {
    const fetchMock = api(201, { watchId: WATCH, heartbeatSeconds: 10 });

    const started = await createWatchTransport(PATH, fetchMock).start();

    expect(started).toEqual({ watchId: WATCH, heartbeatSeconds: 10 });
    const [url, init] = call(fetchMock);
    expect(url).toBe(PATH);
    expect(init.method).toBe("POST");
    expect(init.credentials).toBe("same-origin");
    expect(init.cache).toBe("no-store");
    expect(new Headers(init.headers).get("Content-Type")).toBe("application/json");
    expect(init.body).toBe("{}");
  });

  it("refuses a start answer without a real watch id", async () => {
    const fetchMock = api(201, { watchId: "../../admin", heartbeatSeconds: 10 });

    await expect(createWatchTransport(PATH, fetchMock).start()).rejects.toThrow();
  });

  it("falls back to a 10 s interval when the answer has none that makes sense", async () => {
    const fetchMock = api(201, { watchId: WATCH, heartbeatSeconds: 0 });

    const started = await createWatchTransport(PATH, fetchMock).start();

    expect(started.heartbeatSeconds).toBe(10);
  });

  it("sends a heartbeat with the range, kept alive", async () => {
    const fetchMock = api(204);

    await createWatchTransport(PATH, fetchMock).heartbeat(WATCH, 20, 30);

    const [url, init] = call(fetchMock);
    expect(url).toBe(`${PATH}/${WATCH}/heartbeat`);
    expect(init.method).toBe("POST");
    expect(init.keepalive).toBe(true);
    expect(new Headers(init.headers).get("Content-Type")).toBe("application/json");
    expect(JSON.parse(String(init.body))).toEqual({ from: 20, to: 30 });
  });

  it("sends an event with its type and position, kept alive", async () => {
    const fetchMock = api(204);

    await createWatchTransport(PATH, fetchMock).event(WATCH, "Pause", 42.5);

    const [url, init] = call(fetchMock);
    expect(url).toBe(`${PATH}/${WATCH}/events`);
    expect(init.keepalive).toBe(true);
    expect(JSON.parse(String(init.body))).toEqual({ type: "Pause", position: 42.5 });
  });

  it.each([401, 404, 422, 429, 502])("throws a TrackingRequestError carrying a %i", async (status) => {
    const fetchMock = api(status);

    const error = await createWatchTransport(PATH, fetchMock).heartbeat(WATCH, 0, 10).catch((caught: unknown) => caught);

    expect(error).toBeInstanceOf(TrackingRequestError);
    expect((error as TrackingRequestError).status).toBe(status);
  });
});
