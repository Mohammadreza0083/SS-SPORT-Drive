import { describe, expect, it, vi } from "vitest";
import {
  checkVolleybox,
  createPlayer,
  fetchCountries,
  fetchMiniAppProfile,
  forwardToApi,
  isDriveFolderLink,
  readInitData,
  togglePosition,
  type NewPlayerForm,
} from "./miniapp";

const profile = {
  id: "0199f2a4-6c1e-7b3d-9a52-3f0e4d8c1b27",
  telegramId: 123456789,
  displayName: "Reza Ahmadi",
  username: "reza_analyst",
  role: "Analyst",
};

const tma = { headers: { Authorization: "tma user=1&hash=ab" }, cache: "no-store" };

/**
 * Builds a ProblemDetails response like the API's.
 *
 * @param status - The HTTP status.
 * @param extra - Extra members such as `code` or `errors`.
 * @returns The response.
 */
function problem(status: number, extra: Record<string, unknown> = {}): Response {
  return new Response(JSON.stringify({ status, ...extra }), { status, headers: { "Content-Type": "application/problem+json" } });
}

describe("readInitData", () => {
  it("returns the initData Telegram gave the page", () => {
    expect(readInitData({ Telegram: { WebApp: { initData: "user=1&hash=ab" } } })).toBe("user=1&hash=ab");
  });

  it("returns null outside Telegram or when Telegram sends nothing", () => {
    expect(readInitData({})).toBeNull();
    expect(readInitData({ Telegram: { WebApp: { initData: "" } } })).toBeNull();
  });
});

describe("fetchMiniAppProfile", () => {
  it("sends the initData as a tma Authorization header and returns the profile", async () => {
    const fetchMock = vi.fn().mockResolvedValue(Response.json(profile));

    const result = await fetchMiniAppProfile("user=1&hash=ab", fetchMock);

    expect(result).toEqual({ kind: "ok", profile });
    expect(fetchMock).toHaveBeenCalledWith("/api/miniapp/me", tma);
  });

  it.each([
    [401, "unauthorized"],
    [403, "forbidden"],
    [500, "error"],
  ] as const)("maps status %i to %s", async (status, kind) => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(null, { status }));

    expect(await fetchMiniAppProfile("x", fetchMock)).toEqual({ kind });
  });

  it("reports a network failure as an error", async () => {
    const fetchMock = vi.fn().mockRejectedValue(new TypeError("offline"));

    expect(await fetchMiniAppProfile("x", fetchMock)).toEqual({ kind: "error" });
  });
});

describe("fetchCountries", () => {
  it("returns the catalogue, or an empty list when it cannot be read", async () => {
    const countries = [{ code: "IR", name: "Iran" }];

    expect(await fetchCountries("user=1&hash=ab", vi.fn().mockResolvedValue(Response.json(countries)))).toEqual(countries);
    expect(await fetchCountries("x", vi.fn().mockResolvedValue(new Response(null, { status: 500 })))).toEqual([]);
  });
});

describe("checkVolleybox", () => {
  it("reports a new player with the parsed id", async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      Response.json({ volleyboxId: 48213, url: "https://volleybox.net/a-p48213", existingPlayerId: null, existingPlayerName: null }),
    );

    const result = await checkVolleybox("volleybox.net/a-p48213 ", "user=1&hash=ab", fetchMock);

    expect(result).toEqual({ kind: "new", volleyboxId: 48213 });
    expect(fetchMock).toHaveBeenCalledWith("/api/miniapp/volleybox?url=volleybox.net%2Fa-p48213", tma);
  });

  it("names the player who already has the id", async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      Response.json({ volleyboxId: 7, url: "u", existingPlayerId: "p1", existingPlayerName: "Arash Tehrani" }),
    );

    expect(await checkVolleybox("u", "x", fetchMock)).toEqual({ kind: "existing", volleyboxId: 7, playerName: "Arash Tehrani" });
  });

  it("passes on the rule the link broke", async () => {
    const fetchMock = vi.fn().mockResolvedValue(problem(422, { code: "player.volleybox_not_a_player" }));

    expect(await checkVolleybox("u", "x", fetchMock)).toEqual({ kind: "invalid", code: "player.volleybox_not_a_player" });
  });

  it("does not call the API for an empty link", async () => {
    const fetchMock = vi.fn();

    expect(await checkVolleybox("  ", "x", fetchMock)).toEqual({ kind: "empty" });
    expect(fetchMock).not.toHaveBeenCalled();
  });
});

describe("createPlayer", () => {
  const form: NewPlayerForm = {
    category: "Men",
    name: "Arash Tehrani",
    nationalityCode: "IR",
    positions: ["OH"],
    volleyboxUrl: "https://volleybox.net/arash-tehrani-p48213",
    driveFolderUrl: "https://drive.google.com/drive/folders/1AbC-dEf_GhIjKlMnOp",
  };

  it("posts the form as JSON with the tma header", async () => {
    const fetchMock = vi.fn().mockResolvedValue(Response.json({ id: "p1", name: "Arash Tehrani", status: "Waiting" }, { status: 201 }));

    const result = await createPlayer(form, "user=1&hash=ab", fetchMock);

    expect(result).toEqual({ kind: "created", player: { id: "p1", name: "Arash Tehrani", status: "Waiting" } });
    expect(fetchMock).toHaveBeenCalledWith("/api/miniapp/players", {
      method: "POST",
      headers: { Authorization: "tma user=1&hash=ab", "Content-Type": "application/json" },
      body: JSON.stringify(form),
      cache: "no-store",
    });
  });

  it("returns the broken rule's code", async () => {
    const fetchMock = vi.fn().mockResolvedValue(problem(422, { code: "player.volleybox_taken" }));

    expect(await createPlayer(form, "x", fetchMock)).toEqual({ kind: "rejected", code: "player.volleybox_taken" });
  });

  it("returns a generic rejection for validation errors and failures", async () => {
    expect(await createPlayer(form, "x", vi.fn().mockResolvedValue(problem(400, { errors: { name: ["x"] } })))).toEqual({ kind: "rejected", code: null });
    expect(await createPlayer(form, "x", vi.fn().mockRejectedValue(new TypeError("offline")))).toEqual({ kind: "rejected", code: null });
  });
});

describe("isDriveFolderLink", () => {
  it.each([
    "https://drive.google.com/drive/folders/1AbC-dEf_GhIjKlMnOp",
    "https://drive.google.com/drive/u/0/folders/1AbC-dEf_GhIjKlMnOp?usp=sharing",
    "drive.google.com/open?id=1AbC-dEf_GhIjKlMnOp",
  ])("accepts %s", (link) => {
    expect(isDriveFolderLink(link)).toBe(true);
  });

  it.each(["https://drive.google.com/file/d/1AbC-dEf_GhIjKlMnOp/view", "https://example.com/drive/folders/1AbC-dEf_GhIjKlMnOp", ""])(
    "refuses %s",
    (link) => {
      expect(isDriveFolderLink(link)).toBe(false);
    },
  );
});

describe("togglePosition", () => {
  it("adds the primary, then the secondary, and refuses a third", () => {
    expect(togglePosition([], "OH")).toEqual(["OH"]);
    expect(togglePosition(["OH"], "OPP")).toEqual(["OH", "OPP"]);
    expect(togglePosition(["OH", "OPP"], "MB")).toEqual(["OH", "OPP"]);
  });

  it("removes a chosen position; the secondary then becomes primary", () => {
    expect(togglePosition(["OH", "OPP"], "OH")).toEqual(["OPP"]);
  });
});

describe("forwardToApi", () => {
  it("forwards the method, Authorization, Content-Type and body to the API path and passes the answer back", async () => {
    const fetchMock = vi.fn().mockResolvedValue(problem(422, { code: "drive_folder.taken" }));
    const request = new Request("http://localhost:3000/api/miniapp/players", {
      method: "POST",
      headers: { Authorization: "tma user=1&hash=ab", "Content-Type": "application/json", Cookie: "secret=1" },
      body: '{"name":"A"}',
    });

    const response = await forwardToApi(request, "http://api:8080/", "/miniapp/players", fetchMock);

    expect(fetchMock).toHaveBeenCalledWith("http://api:8080/miniapp/players", {
      method: "POST",
      headers: { Authorization: "tma user=1&hash=ab", "Content-Type": "application/json" },
      body: '{"name":"A"}',
      cache: "no-store",
    });
    expect(response.status).toBe(422);
    expect(response.headers.get("Content-Type")).toBe("application/problem+json");
  });

  it("keeps the query string of GET requests and sends no body", async () => {
    const fetchMock = vi.fn().mockResolvedValue(Response.json({}));
    const request = new Request("http://localhost:3000/api/miniapp/volleybox?url=a%2Fb", { headers: { Authorization: "tma x" } });

    await forwardToApi(request, "http://api:8080", "/miniapp/volleybox", fetchMock);

    expect(fetchMock).toHaveBeenCalledWith("http://api:8080/miniapp/volleybox?url=a%2Fb", {
      method: "GET",
      headers: { Authorization: "tma x" },
      body: undefined,
      cache: "no-store",
    });
  });

  it("answers 401 without calling the API when there is no Authorization header", async () => {
    const fetchMock = vi.fn();

    const response = await forwardToApi(new Request("http://localhost:3000/api/miniapp/me"), "http://api:8080", "/miniapp/me", fetchMock);

    expect(response.status).toBe(401);
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("answers 502 when the API cannot be reached", async () => {
    const fetchMock = vi.fn().mockRejectedValue(new TypeError("connect ECONNREFUSED"));
    const request = new Request("http://localhost:3000/api/miniapp/me", { headers: { Authorization: "tma x" } });

    const response = await forwardToApi(request, "http://api:8080", "/miniapp/me", fetchMock);

    expect(response.status).toBe(502);
  });
});
