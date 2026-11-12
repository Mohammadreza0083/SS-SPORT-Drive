import { describe, expect, it, vi } from "vitest";
import { publicAccessClient, requestAccessCode, submitAccessRequest } from "./public-access";

/**
 * Builds a ProblemDetails response like the API's.
 *
 * @param status - The HTTP status.
 * @param extra - Extra members such as `code`.
 * @param headers - Extra headers.
 * @returns The response.
 */
function problem(status: number, extra: Record<string, unknown>, headers: Record<string, string> = {}): Response {
  return Response.json({ status, ...extra }, { status, headers });
}

describe("requestAccessCode", () => {
  it("posts the trimmed email as JSON to the same-origin route", async () => {
    const fetchMock = vi.fn().mockResolvedValue(Response.json({ resendAfterSeconds: 60 }, { status: 202 }));

    const result = await requestAccessCode(" scout@club.com ", fetchMock);

    expect(result).toEqual({ kind: "sent", resendAfterSeconds: 60 });
    expect(fetchMock).toHaveBeenCalledWith("/api/public/access-code", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: '{"email":"scout@club.com"}',
      cache: "no-store",
    });
  });

  it("falls back to a 60-second resend wait when the answer does not say", async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(null, { status: 202 }));

    expect(await requestAccessCode("scout@club.com", fetchMock)).toEqual({ kind: "sent", resendAfterSeconds: 60 });
  });

  it("reads a refusal's code, message and wait", async () => {
    const fetchMock = vi.fn().mockResolvedValue(problem(429, { code: "email_code.too_many", title: "Wait a moment." }, { "Retry-After": "42" }));

    expect(await requestAccessCode("scout@club.com", fetchMock)).toEqual({
      kind: "refused",
      code: "email_code.too_many",
      message: "Wait a moment.",
      retryAfterSeconds: 42,
    });
  });

  it("treats a network failure as a refusal without a code", async () => {
    const result = await requestAccessCode("scout@club.com", vi.fn().mockRejectedValue(new TypeError("offline")));

    expect(result).toEqual({ kind: "refused", code: null, message: null, retryAfterSeconds: null });
  });
});

describe("submitAccessRequest", () => {
  const request = { playerSlug: "arash-tehrani", about: "Head scout", email: "scout@club.com", code: "042917", turnstileToken: "tt-1" };

  it("posts the request as JSON and answers sent on success", async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(null, { status: 202 }));

    expect(await submitAccessRequest(request, fetchMock)).toEqual({ kind: "sent" });
    expect(fetchMock.mock.calls[0][0]).toBe("/api/public/access-requests");
    const init = fetchMock.mock.calls[0][1] as RequestInit;
    expect(init.method).toBe("POST");
    expect(init.cache).toBe("no-store");
    expect(JSON.parse(init.body as string)).toEqual(request);
  });

  it("leaves email and code out for a signed-in viewer", async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(null, { status: 202 }));

    await submitAccessRequest({ playerSlug: "arash-tehrani", about: "Head scout", turnstileToken: null }, fetchMock);

    expect(JSON.parse((fetchMock.mock.calls[0][1] as RequestInit).body as string)).toEqual({ playerSlug: "arash-tehrani", about: "Head scout", turnstileToken: null });
  });

  it("answers why the request was refused, with the tries left", async () => {
    const fetchMock = vi.fn().mockResolvedValue(problem(422, { code: "email_code.invalid", title: "That code is not right. 3 tries left.", attemptsLeft: 3 }));

    expect(await submitAccessRequest(request, fetchMock)).toEqual({
      kind: "refused",
      code: "email_code.invalid",
      message: "That code is not right. 3 tries left.",
      attemptsLeft: 3,
    });
  });

  it("treats a network failure as a refusal without a code", async () => {
    expect(await submitAccessRequest(request, vi.fn().mockRejectedValue(new TypeError("offline")))).toEqual({
      kind: "refused",
      code: null,
      message: null,
      attemptsLeft: null,
    });
  });
});

describe("publicAccessClient", () => {
  it("is the RequestAccessClient the locked page uses by default", () => {
    expect(typeof publicAccessClient.requestCode).toBe("function");
    expect(typeof publicAccessClient.submit).toBe("function");
  });
});
