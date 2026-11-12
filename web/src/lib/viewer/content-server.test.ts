import { describe, expect, it, vi } from "vitest";
import { clientAddress, forwardPhoto, loadPlayerPage, loadViewerPage } from "./content-server";

const API = "http://api:8080";
const SESSION = "session-token-from-cookie";

/** A Personal link's overview as the API answers it. */
const personal = {
  type: "Personal",
  playerCount: 1,
  recipient: { name: "Jan Demir", organisation: "Club Aurora" },
  note: "Hi Jan, as discussed.",
  viewerEmail: "j.demir@aurora-volley.example",
};

/** One player's card and stats. */
const player = {
  id: "0b6f2c1e-1111-4a4a-8888-000000000001",
  name: "Arash Tehrani",
  firstName: "Arash",
  position: { code: "OPP", label: "Opposite" },
  nationality: { code: "IR", name: "Iran" },
  volleyboxUrl: "https://volleybox.net/arash-tehrani-p80001",
  hasPhoto: true,
  stats: { videos: 2, minutes: 110, documents: 1 },
};

/**
 * Wraps items in the API's paged shape.
 *
 * @param items - The items.
 * @returns The page.
 */
function paged<T>(items: T[]) {
  return { items, page: 1, pageSize: 100, totalCount: items.length, totalPages: 1 };
}

/**
 * A fetch stub that answers by path.
 *
 * @param routes - Path (after the API base) to status and body.
 * @returns The stub.
 */
function api(routes: Record<string, { status?: number; body?: unknown }>) {
  return vi.fn(async (url: string | URL | Request) => {
    const path = String(url).replace(API, "").split("?")[0];
    const route = routes[path];
    if (!route) {
      return Response.json({ status: 404 }, { status: 404 });
    }

    return Response.json(route.body ?? {}, { status: route.status ?? 200 });
  });
}

/**
 * The headers one stub call was made with.
 *
 * @param fetchMock - The stub.
 * @param index - Which call.
 * @returns The headers.
 */
function headersOf(fetchMock: ReturnType<typeof api>, index = 0): Headers {
  const init = fetchMock.mock.calls[index] as unknown as [string, RequestInit];
  return new Headers(init[1]?.headers);
}

describe("clientAddress", () => {
  it("takes the first address the proxy forwarded", () => {
    expect(clientAddress(new Headers({ "X-Forwarded-For": "203.0.113.9, 10.0.0.2" }))).toBe("203.0.113.9");
  });

  it("falls back to X-Real-IP, then to nothing", () => {
    expect(clientAddress(new Headers({ "X-Real-IP": "203.0.113.10" }))).toBe("203.0.113.10");
    expect(clientAddress(new Headers())).toBeNull();
  });
});

describe("loadViewerPage", () => {
  it("shows the gate without asking the API when there is no session cookie", async () => {
    const fetchMock = api({});

    const page = await loadViewerPage(API, "Ab3xYz", undefined, null, fetchMock);

    expect(page).toEqual({ kind: "gate" });
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("shows the gate when the API no longer accepts the session", async () => {
    const fetchMock = api({ "/links/Ab3xYz/content": { status: 401, body: { code: "viewer.verification_required" } } });

    const page = await loadViewerPage(API, "Ab3xYz", SESSION, null, fetchMock);

    expect(page).toEqual({ kind: "gate" });
  });

  it("sends the session and the viewer's address to the API", async () => {
    const fetchMock = api({ "/links/Ab3xYz/content": { status: 401 } });

    await loadViewerPage(API, "Ab3xYz", SESSION, "203.0.113.9", fetchMock);

    expect(headersOf(fetchMock).get("X-Viewer-Session")).toBe(SESSION);
    expect(headersOf(fetchMock).get("X-Forwarded-For")).toBe("203.0.113.9");
  });

  it("opens a Personal link straight on its player's page", async () => {
    const fetchMock = api({
      "/links/Ab3xYz/content": { body: personal },
      "/links/Ab3xYz/players": { body: paged([{ ...player, videoCount: 2 }]) },
      [`/links/Ab3xYz/players/${player.id}`]: { body: player },
      [`/links/Ab3xYz/players/${player.id}/videos`]: { body: paged([]) },
      [`/links/Ab3xYz/players/${player.id}/documents`]: { body: paged([]) },
    });

    const page = await loadViewerPage(API, "Ab3xYz", SESSION, null, fetchMock);

    expect(page.kind).toBe("player");
    expect(page.kind === "player" && page.data.player.name).toBe("Arash Tehrani");
  });

  it("opens a Roster link on the roster, with every card linking to its player", async () => {
    const second = { ...player, id: "0b6f2c1e-1111-4a4a-8888-000000000002", name: "Daniyar Seitov", firstName: "Daniyar", hasPhoto: false, position: { code: "OH", label: "Outside hitter" }, nationality: { code: "KZ", name: "Kazakhstan" }, videoCount: 4 };
    const fetchMock = api({
      "/links/Ab3xYz/content": { body: { ...personal, type: "Roster", playerCount: 2 } },
      "/links/Ab3xYz/players": { body: paged([{ ...player, videoCount: 6 }, second]) },
    });

    const page = await loadViewerPage(API, "Ab3xYz", SESSION, null, fetchMock);

    expect(page).toEqual({
      kind: "roster",
      data: {
        sharedWith: "Club Aurora",
        playerCount: 2,
        players: [
          { id: player.id, href: `/r/Ab3xYz/p/${player.id}`, name: "Arash Tehrani", position: "OPP", nationality: "Iran", videos: 6, photoUrl: `/api/links/Ab3xYz/players/${player.id}/photo` },
          { id: second.id, href: `/r/Ab3xYz/p/${second.id}`, name: "Daniyar Seitov", position: "OH", nationality: "Kazakhstan", videos: 4, photoUrl: undefined },
        ],
      },
    });
    expect(String(fetchMock.mock.calls[1][0])).toContain("pageSize=20");
  });

  it("names the recipient when the link has no organisation", async () => {
    const fetchMock = api({
      "/links/Ab3xYz/content": { body: { ...personal, type: "Roster", recipient: { name: "Jan Demir", organisation: null } } },
      "/links/Ab3xYz/players": { body: paged([]) },
    });

    const page = await loadViewerPage(API, "Ab3xYz", SESSION, null, fetchMock);

    expect(page.kind === "roster" && page.data.sharedWith).toBe("Jan Demir");
  });

  it("fails loudly when the API breaks, rather than showing the gate", async () => {
    const fetchMock = api({ "/links/Ab3xYz/content": { status: 500 } });

    await expect(loadViewerPage(API, "Ab3xYz", SESSION, null, fetchMock)).rejects.toThrow();
  });
});

describe("loadPlayerPage", () => {
  /** The API with one player who has a hero full match, a highlight and a document. */
  const playerApi = () =>
    api({
      "/links/Ab3xYz/content": { body: personal },
      [`/links/Ab3xYz/players/${player.id}`]: { body: player },
      [`/links/Ab3xYz/players/${player.id}/videos`]: {
        body: paged([
          { id: "v1", title: "Full match vs Paykan", tag: "Full match", durationSeconds: 6490 },
          { id: "v2", title: "Highlights 2026", tag: "Highlight", durationSeconds: 84 },
        ]),
      },
      [`/links/Ab3xYz/players/${player.id}/documents`]: { body: paged([{ id: "d1", title: "Season stats 2026", pageCount: 2 }]) },
    });

  it("builds the player page from the API", async () => {
    const page = await loadPlayerPage(API, "Ab3xYz", player.id, SESSION, null, playerApi());

    expect(page).toEqual({
      kind: "player",
      data: {
        player: {
          name: "Arash Tehrani",
          firstName: "Arash",
          position: { code: "OPP", label: "Opposite" },
          nationality: { code: "IR", name: "Iran" },
          volleyboxUrl: "https://volleybox.net/arash-tehrani-p80001",
          photoUrl: `/api/links/Ab3xYz/players/${player.id}/photo`,
        },
        viewerEmail: "j.demir@aurora-volley.example",
        recipient: { organisation: "Club Aurora", name: "Jan Demir" },
        note: { text: "Hi Jan, as discussed.", author: "SS-SPORT" },
        stats: { videos: 2, minutes: 110, documents: 1 },
        videos: [
          { id: "v1", title: "Full match vs Paykan", tag: "Full match", durationSeconds: 6490, src: "" },
          { id: "v2", title: "Highlights 2026", tag: "Highlight", durationSeconds: 84, src: "" },
        ],
        documents: [{ id: "d1", title: "Season stats 2026", pageCount: 2, pages: [] }],
      },
    });
  });

  it("asks for the largest page of videos and documents, never everything", async () => {
    const fetchMock = playerApi();

    await loadPlayerPage(API, "Ab3xYz", player.id, SESSION, null, fetchMock);

    const urls = fetchMock.mock.calls.map((call) => String(call[0]));
    expect(urls.find((url) => url.includes("/videos"))).toContain("pageSize=100");
    expect(urls.find((url) => url.includes("/documents"))).toContain("pageSize=100");
  });

  it("leaves out the note and the organisation when the link has none", async () => {
    const fetchMock = api({
      "/links/Ab3xYz/content": { body: { ...personal, note: null, recipient: { name: "Jan Demir", organisation: null } } },
      [`/links/Ab3xYz/players/${player.id}`]: { body: { ...player, hasPhoto: false } },
      [`/links/Ab3xYz/players/${player.id}/videos`]: { body: paged([]) },
      [`/links/Ab3xYz/players/${player.id}/documents`]: { body: paged([]) },
    });

    const page = await loadPlayerPage(API, "Ab3xYz", player.id, SESSION, null, fetchMock);

    expect(page.kind).toBe("player");
    if (page.kind === "player") {
      expect(page.data.note).toBeUndefined();
      expect(page.data.recipient).toEqual({ name: "Jan Demir" });
      expect(page.data.player.photoUrl).toBeUndefined();
    }
  });

  it("answers not found for a player who is not on the link", async () => {
    const fetchMock = api({ "/links/Ab3xYz/content": { body: personal } });

    const page = await loadPlayerPage(API, "Ab3xYz", "0b6f2c1e-1111-4a4a-8888-00000000dead", SESSION, null, fetchMock);

    expect(page).toEqual({ kind: "not-found" });
  });

  it("refuses a player id that is not a GUID without asking the API", async () => {
    const fetchMock = playerApi();

    const page = await loadPlayerPage(API, "Ab3xYz", "../../admin", SESSION, null, fetchMock);

    expect(page).toEqual({ kind: "not-found" });
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("sends the viewer back to the gate without a session", async () => {
    const page = await loadPlayerPage(API, "Ab3xYz", player.id, undefined, null, playerApi());

    expect(page).toEqual({ kind: "gate" });
  });
});

describe("forwardPhoto", () => {
  /**
   * Builds the browser's request for a photo.
   *
   * @param cookie - The Cookie header, if any.
   * @returns The request.
   */
  const photoRequest = (cookie?: string) =>
    new Request(`http://web:3000/api/links/Ab3xYz/players/${player.id}/photo`, {
      headers: cookie ? { Cookie: cookie, "X-Forwarded-For": "203.0.113.9" } : {},
    });

  it("answers 401 without a session cookie and without asking the API", async () => {
    const fetchMock = vi.fn();

    const response = await forwardPhoto(photoRequest(), API, "Ab3xYz", player.id, fetchMock);

    expect(response.status).toBe(401);
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("streams the photo with the session in a header and a private cache", async () => {
    const fetchMock = vi.fn(async () => new Response(new Uint8Array([1, 2, 3]), { headers: { "Content-Type": "image/webp" } }));

    const response = await forwardPhoto(photoRequest(`__Host-ssd_vs=${SESSION}`), API, "Ab3xYz", player.id, fetchMock);

    expect(response.status).toBe(200);
    expect(response.headers.get("Content-Type")).toBe("image/webp");
    expect(response.headers.get("Cache-Control")).toBe("private, max-age=300");
    expect(new Uint8Array(await response.arrayBuffer())).toEqual(new Uint8Array([1, 2, 3]));
    const [url, init] = fetchMock.mock.calls[0] as unknown as [string, RequestInit];
    expect(url).toBe(`${API}/links/Ab3xYz/players/${player.id}/photo`);
    expect(new Headers(init.headers).get("X-Viewer-Session")).toBe(SESSION);
    expect(new Headers(init.headers).get("X-Forwarded-For")).toBe("203.0.113.9");
  });

  it("passes a 404 through without the API's body", async () => {
    const fetchMock = vi.fn(async () => Response.json({ title: "Photo was not found." }, { status: 404 }));

    const response = await forwardPhoto(photoRequest(`__Host-ssd_vs=${SESSION}`), API, "Ab3xYz", player.id, fetchMock);

    expect(response.status).toBe(404);
    expect(await response.text()).toBe("");
  });

  it("refuses a player id that is not a GUID", async () => {
    const fetchMock = vi.fn();

    const response = await forwardPhoto(photoRequest(`__Host-ssd_vs=${SESSION}`), API, "Ab3xYz", "x/../../y", fetchMock);

    expect(response.status).toBe(404);
    expect(fetchMock).not.toHaveBeenCalled();
  });
});
