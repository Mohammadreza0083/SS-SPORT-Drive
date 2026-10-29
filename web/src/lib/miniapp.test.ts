import { describe, expect, it, vi } from "vitest";
import { fetchMiniAppProfile, forwardProfileRequest, readInitData } from "./miniapp";

const profile = {
  id: "0199f2a4-6c1e-7b3d-9a52-3f0e4d8c1b27",
  telegramId: 123456789,
  displayName: "Reza Ahmadi",
  username: "reza_analyst",
  role: "Analyst",
};

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
    expect(fetchMock).toHaveBeenCalledWith("/api/miniapp/me", {
      headers: { Authorization: "tma user=1&hash=ab" },
      cache: "no-store",
    });
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

describe("forwardProfileRequest", () => {
  it("forwards only the Authorization header to the API and passes the answer back", async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ status: 403 }), { status: 403, headers: { "Content-Type": "application/problem+json" } }),
    );
    const request = new Request("http://localhost:3000/api/miniapp/me", {
      headers: { Authorization: "tma user=1&hash=ab", Cookie: "secret=1" },
    });

    const response = await forwardProfileRequest(request, "http://api:8080/", fetchMock);

    expect(fetchMock).toHaveBeenCalledWith("http://api:8080/miniapp/me", {
      headers: { Authorization: "tma user=1&hash=ab" },
      cache: "no-store",
    });
    expect(response.status).toBe(403);
    expect(response.headers.get("Content-Type")).toBe("application/problem+json");
  });

  it("answers 401 without calling the API when there is no Authorization header", async () => {
    const fetchMock = vi.fn();

    const response = await forwardProfileRequest(new Request("http://localhost:3000/api/miniapp/me"), "http://api:8080", fetchMock);

    expect(response.status).toBe(401);
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("answers 502 when the API cannot be reached", async () => {
    const fetchMock = vi.fn().mockRejectedValue(new TypeError("connect ECONNREFUSED"));
    const request = new Request("http://localhost:3000/api/miniapp/me", { headers: { Authorization: "tma x" } });

    const response = await forwardProfileRequest(request, "http://api:8080", fetchMock);

    expect(response.status).toBe(502);
  });
});
