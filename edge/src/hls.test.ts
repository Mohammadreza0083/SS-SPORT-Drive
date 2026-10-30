import { afterEach, describe, expect, it, vi } from "vitest";
import vectors from "../../contracts/hls-token-vectors.json";
import worker, { type Env } from "./index";

/** Objects the fake R2 bucket holds, by key. */
const stored = new Map<string, string>([
  ["players/7f1c2a9e/videos/3b5d8c41/hls/master.m3u8", "#EXTM3U\n#EXT-X-STREAM-INF:BANDWIDTH=800000\n360p/index.m3u8\n"],
  ["players/7f1c2a9e/videos/3b5d8c41/hls/720p/seg_00042.ts", "segment-bytes"],
]);

/** Keys the worker asked R2 for. */
const requested: string[] = [];

/** A stand-in for the R2 binding that serves {@link stored}. */
const media = {
  async get(key: string) {
    requested.push(key);
    const body = stored.get(key);
    return body === undefined ? null : { body: new Response(body).body, size: body.length, httpEtag: '"etag"' };
  },
} as unknown as R2Bucket;

/** The environment with the shared key and the fake bucket. */
const env: Env = { HLS_SIGNING_KEY: vectors.signingKeyBase64, MEDIA: media };

/**
 * Sends a request to the worker at a fixed time, with or without the viewer's session cookie.
 *
 * @param path - Path on the edge host.
 * @param cookie - The session cookie value, or null for none.
 * @param nowUnix - The current time, Unix seconds.
 * @param method - The HTTP method.
 * @returns The worker's response.
 */
async function send(path: string, cookie: string | null, nowUnix: number, method = "GET"): Promise<Response> {
  vi.setSystemTime(nowUnix * 1000);
  const headers = new Headers();
  if (cookie !== null) {
    headers.set("Cookie", `theme=dark; ${vectors.sessionCookieName}=${cookie}`);
  }
  return worker.fetch(new Request(`https://drive.ss-sport.co${path}`, { method, headers }), env);
}

afterEach(() => {
  vi.useRealTimers();
  requested.length = 0;
});

describe("HLS edge tokens (shared vectors)", () => {
  for (const request of vectors.requests) {
    it(`${request.name} → ${request.expect}`, async () => {
      vi.useFakeTimers();

      const response = await send(request.path, request.cookie, request.nowUnix);

      expect(response.status).toBe(request.expect);
      if (request.expect === 200) {
        expect(requested).toEqual([request.r2Key]);
      } else {
        expect(requested).toEqual([]);
      }
    });
  }
});

describe("HLS delivery", () => {
  const { token, pathPrefix, sessionCookie, expiresAtUnix } = vectors.issue;

  it("serves playlists as HLS and never lets anyone cache them", async () => {
    vi.useFakeTimers();

    const response = await send(`/hls/${token}/${pathPrefix}/master.m3u8`, sessionCookie, expiresAtUnix - 60);

    expect(response.headers.get("Content-Type")).toBe("application/vnd.apple.mpegurl");
    expect(response.headers.get("Cache-Control")).toBe("private, no-store");
    expect(await response.text()).toContain("#EXTM3U");
  });

  it("serves segments as MPEG-TS, cacheable only by the viewer's browser", async () => {
    vi.useFakeTimers();

    const response = await send(`/hls/${token}/${pathPrefix}/720p/seg_00042.ts`, sessionCookie, expiresAtUnix - 60);

    expect(response.headers.get("Content-Type")).toBe("video/mp2t");
    expect(response.headers.get("Cache-Control")).toBe("private, max-age=1800");
    expect(response.headers.get("X-Content-Type-Options")).toBe("nosniff");
  });

  it("answers 404 for a file the video does not have", async () => {
    vi.useFakeTimers();

    const response = await send(`/hls/${token}/${pathPrefix}/1080p/index.m3u8`, sessionCookie, expiresAtUnix - 60);

    expect(response.status).toBe(404);
  });

  it("refuses anything but GET and HEAD", async () => {
    vi.useFakeTimers();

    const response = await send(`/hls/${token}/${pathPrefix}/master.m3u8`, sessionCookie, expiresAtUnix - 60, "POST");

    expect(response.status).toBe(405);
  });

  it("gives no reason when it refuses, so nothing can be probed", async () => {
    vi.useFakeTimers();

    const response = await send(`/hls/${token}/${pathPrefix}/master.m3u8`, null, expiresAtUnix - 60);

    expect(await response.text()).toBe("Forbidden");
  });
});
