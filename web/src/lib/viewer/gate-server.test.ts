import { describe, expect, it, vi } from "vitest";
import { forwardGate, readSession, resolveLink } from "./gate-server";

const API = "http://api:8080";

/**
 * Builds a browser request to one of the gate routes.
 *
 * @param action - The last path segment.
 * @param init - Headers and body; Origin and Host default to the site.
 * @returns The request.
 */
function browserRequest(action: string, init: { origin?: string | null; host?: string; cookie?: string; body?: unknown; contentType?: string | null } = {}): Request {
  const headers = new Headers({ Host: init.host ?? "drive.ss-sport.co" });
  if (init.origin !== null) {
    headers.set("Origin", init.origin ?? "https://drive.ss-sport.co");
  }

  if (init.contentType !== null) {
    headers.set("Content-Type", init.contentType ?? "application/json");
  }

  if (init.cookie) {
    headers.set("Cookie", init.cookie);
  }

  return new Request(`http://web:3000/api/links/Ab3xYz/${action}`, {
    method: "POST",
    headers,
    body: init.body === undefined ? undefined : JSON.stringify(init.body),
  });
}

const access = {
  deviceToken: "dev-token-from-api",
  deviceExpiresAt: "2027-02-04T09:00:00+00:00",
  sessionToken: "session-token-from-api",
  sessionExpiresAt: "2026-11-07T09:00:00+00:00",
};

describe("forwardGate (CSRF)", () => {
  it("refuses a request from another origin without calling the API", async () => {
    const fetchMock = vi.fn();

    const response = await forwardGate(browserRequest("email-code", { origin: "https://evil.example", body: { email: "a@b.co" } }), API, "Ab3xYz", "email-code", fetchMock);

    expect(response.status).toBe(403);
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("refuses a request with no Origin", async () => {
    const fetchMock = vi.fn();

    const response = await forwardGate(browserRequest("session", { origin: null }), API, "Ab3xYz", "session", fetchMock);

    expect(response.status).toBe(403);
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("refuses a form post that is not JSON", async () => {
    const fetchMock = vi.fn();

    const response = await forwardGate(browserRequest("verify", { contentType: "application/x-www-form-urlencoded", body: "x" }), API, "Ab3xYz", "verify", fetchMock);

    expect(response.status).toBe(415);
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("trusts the forwarded host behind the proxy", async () => {
    const fetchMock = vi.fn().mockResolvedValue(Response.json({ email: "m***@lube.it", resendAfterSeconds: 60, expiresInSeconds: 600 }, { status: 202 }));
    const request = browserRequest("email-code", { host: "web:3000", body: { email: "marco@lube.it" } });
    request.headers.set("X-Forwarded-Host", "drive.ss-sport.co");

    const response = await forwardGate(request, API, "Ab3xYz", "email-code", fetchMock);

    expect(response.status).toBe(202);
  });
});

describe("forwardGate (API calls)", () => {
  it("passes the viewer's address on to the API (V-2b)", async () => {
    const fetchMock = vi.fn().mockResolvedValue(Response.json({ email: "m***@lube.it", resendAfterSeconds: 60, expiresInSeconds: 600 }, { status: 202 }));
    const request = browserRequest("email-code", { body: { email: "marco@lube.it" } });
    request.headers.set("X-Forwarded-For", "203.0.113.9, 10.0.0.2");

    await forwardGate(request, API, "Ab3xYz", "email-code", fetchMock);

    const [, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect(new Headers(init.headers).get("X-Forwarded-For")).toBe("203.0.113.9");
  });


  it("asks for a code: forwards the JSON body and passes the answer back", async () => {
    const fetchMock = vi.fn().mockResolvedValue(Response.json({ email: "m***@lube.it", resendAfterSeconds: 60, expiresInSeconds: 600 }, { status: 202 }));

    const response = await forwardGate(browserRequest("email-code", { body: { email: "marco@lube.it" } }), API, "Ab3xYz", "email-code", fetchMock);

    expect(fetchMock).toHaveBeenCalledWith("http://api:8080/links/Ab3xYz/email-code", expect.objectContaining({ method: "POST", body: '{"email":"marco@lube.it"}' }));
    const sent = fetchMock.mock.calls[0][1] as RequestInit;
    expect(new Headers(sent.headers).get("X-Viewer-Device")).toBeNull();
    expect(response.status).toBe(202);
    expect(await response.json()).toEqual({ email: "m***@lube.it", resendAfterSeconds: 60, expiresInSeconds: 600 });
  });

  it("passes a refusal back with its Retry-After", async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      Response.json({ status: 429, code: "email_code.too_many", title: "Wait a moment." }, { status: 429, headers: { "Retry-After": "42", "Content-Type": "application/problem+json" } }),
    );

    const response = await forwardGate(browserRequest("email-code", { body: { email: "marco@lube.it" } }), API, "Ab3xYz", "email-code", fetchMock);

    expect(response.status).toBe(429);
    expect(response.headers.get("Retry-After")).toBe("42");
    expect((await response.json()).code).toBe("email_code.too_many");
  });

  it("verifies: sends the device cookie as a header, sets both cookies and keeps the tokens out of the body", async () => {
    const fetchMock = vi.fn().mockResolvedValue(Response.json(access));
    const now = Date.parse("2026-11-06T09:00:00Z");

    const response = await forwardGate(
      browserRequest("verify", { cookie: "theme=dark; __Host-ssd_dev=old-device", body: { email: "marco@lube.it", code: "042917" } }),
      API,
      "Ab3xYz",
      "verify",
      fetchMock,
      now,
    );

    const sent = fetchMock.mock.calls[0][1] as RequestInit;
    expect(new Headers(sent.headers).get("X-Viewer-Device")).toBe("old-device");
    expect(new Headers(sent.headers).get("Cookie")).toBeNull();
    expect(response.status).toBe(200);
    const body = await response.text();
    expect(body).not.toContain("token-from-api");
    expect(JSON.parse(body)).toEqual({ ok: true });

    const cookies = response.headers.getSetCookie();
    expect(cookies).toHaveLength(2);
    expect(cookies[0]).toBe("__Host-ssd_dev=dev-token-from-api; Path=/; Max-Age=7776000; HttpOnly; Secure; SameSite=Lax");
    expect(cookies[1]).toBe("__Host-ssd_vs=session-token-from-api; Path=/; Max-Age=86400; HttpOnly; Secure; SameSite=Lax");
  });

  it("a forward awaiting approval (L-3) gets only the device cookie, and the body says it waits", async () => {
    const fetchMock = vi.fn().mockResolvedValue(Response.json({ ...access, sessionToken: null, sessionExpiresAt: null, awaitingApproval: true }));

    const response = await forwardGate(
      browserRequest("verify", { body: { email: "scout@other.com", code: "042917" } }),
      API,
      "Ab3xYz",
      "verify",
      fetchMock,
      Date.parse("2026-11-06T09:00:00Z"),
    );

    expect(response.status).toBe(200);
    expect(await response.json()).toEqual({ ok: true, awaitingApproval: true });
    expect(response.headers.getSetCookie()).toEqual(["__Host-ssd_dev=dev-token-from-api; Path=/; Max-Age=7776000; HttpOnly; Secure; SameSite=Lax"]);
  });

  it("opens from a trusted device: only the session cookie is set", async () => {
    const fetchMock = vi.fn().mockResolvedValue(Response.json({ ...access, deviceToken: null, deviceExpiresAt: null }));

    const response = await forwardGate(browserRequest("session", { cookie: "__Host-ssd_dev=dev-1", contentType: null }), API, "Ab3xYz", "session", fetchMock, Date.parse("2026-11-06T09:00:00Z"));

    expect(new Headers((fetchMock.mock.calls[0][1] as RequestInit).headers).get("X-Viewer-Device")).toBe("dev-1");
    expect(response.headers.getSetCookie()).toEqual(["__Host-ssd_vs=session-token-from-api; Path=/; Max-Age=86400; HttpOnly; Secure; SameSite=Lax"]);
  });

  it("passes a wrong code back as it came", async () => {
    const fetchMock = vi.fn().mockResolvedValue(Response.json({ status: 422, code: "email_code.invalid", attemptsLeft: 3, title: "That code is not right. 3 tries left." }, { status: 422 }));

    const response = await forwardGate(browserRequest("verify", { body: { email: "marco@lube.it", code: "000000" } }), API, "Ab3xYz", "verify", fetchMock);

    expect(response.status).toBe(422);
    expect(response.headers.getSetCookie()).toEqual([]);
    expect((await response.json()).attemptsLeft).toBe(3);
  });

  it("escapes the link code in the API path", async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(null, { status: 404 }));

    await forwardGate(browserRequest("session", { contentType: null }), API, "a/../admin", "session", fetchMock);

    expect(fetchMock.mock.calls[0][0]).toBe("http://api:8080/links/a%2F..%2Fadmin/session");
  });

  it("answers 502 when the API cannot be reached", async () => {
    const fetchMock = vi.fn().mockRejectedValue(new TypeError("fetch failed"));

    const response = await forwardGate(browserRequest("session", { contentType: null }), API, "Ab3xYz", "session", fetchMock);

    expect(response.status).toBe(502);
  });
});

describe("resolveLink", () => {
  it("reads an open link for the gate card", async () => {
    const link = { code: "Ab3xYz", type: "Personal", playerCount: 1, recipientHint: "m***@lube.it", player: { name: "Arash Tehrani", position: "OPP", nationality: { code: "IR", name: "Iran" } } };
    const fetchMock = vi.fn().mockResolvedValue(Response.json(link));

    expect(await resolveLink(API, "Ab3xYz", fetchMock)).toEqual(link);
    expect(fetchMock).toHaveBeenCalledWith("http://api:8080/links/Ab3xYz", expect.objectContaining({ cache: "no-store" }));
  });

  it("sends the viewer's address, so the API's limits count each viewer (V-2b)", async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(null, { status: 404 }));

    await resolveLink(API, "Ab3xYz", fetchMock, "203.0.113.9");

    const [, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect(new Headers(init.headers).get("X-Forwarded-For")).toBe("203.0.113.9");
  });

  it("answers null for a link that does not open", async () => {
    expect(await resolveLink(API, "Zz9Zz9", vi.fn().mockResolvedValue(new Response(null, { status: 404 })))).toBeNull();
  });
});

describe("readSession", () => {
  it("reads the session with its token as a header", async () => {
    const fetchMock = vi.fn().mockResolvedValue(Response.json({ email: "marco@lube.it", isForward: false, expiresAt: "2026-11-07T09:00:00+00:00" }));

    const session = await readSession(API, "Ab3xYz", "vs-1", fetchMock);

    expect(session?.email).toBe("marco@lube.it");
    expect(new Headers((fetchMock.mock.calls[0][1] as RequestInit).headers).get("X-Viewer-Session")).toBe("vs-1");
  });

  it("answers null without a token, and for an ended session", async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(null, { status: 401 }));

    expect(await readSession(API, "Ab3xYz", undefined, fetchMock)).toBeNull();
    expect(fetchMock).not.toHaveBeenCalled();
    expect(await readSession(API, "Ab3xYz", "old", fetchMock)).toBeNull();
  });
});
