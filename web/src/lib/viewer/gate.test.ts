import { describe, expect, it, vi } from "vitest";
import { codeDigits, formatCountdown, openWithDevice, requestCode, verifyCode } from "./gate";

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

describe("requestCode", () => {
  it("posts the email as JSON to the same-origin route", async () => {
    const fetchMock = vi.fn().mockResolvedValue(Response.json({ email: "m***@lube.it", resendAfterSeconds: 60, expiresInSeconds: 600 }, { status: 202 }));

    const result = await requestCode("Ab3xYz", " marco@lube.it ", fetchMock);

    expect(result).toEqual({ kind: "sent", resendAfterSeconds: 60 });
    expect(fetchMock).toHaveBeenCalledWith("/api/links/Ab3xYz/email-code", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: '{"email":"marco@lube.it"}',
      cache: "no-store",
    });
  });

  it("reads a refusal's code, message and wait", async () => {
    const fetchMock = vi.fn().mockResolvedValue(problem(429, { code: "email_code.too_many", title: "Wait a moment." }, { "Retry-After": "42" }));

    expect(await requestCode("Ab3xYz", "marco@lube.it", fetchMock)).toEqual({ kind: "refused", code: "email_code.too_many", message: "Wait a moment.", retryAfterSeconds: 42 });
  });

  it("treats a network failure as a refusal without a code", async () => {
    const result = await requestCode("Ab3xYz", "marco@lube.it", vi.fn().mockRejectedValue(new TypeError("offline")));

    expect(result).toEqual({ kind: "refused", code: null, message: null, retryAfterSeconds: null });
  });
});

describe("verifyCode", () => {
  it("answers verified when the route accepts the code", async () => {
    const fetchMock = vi.fn().mockResolvedValue(Response.json({ ok: true }));

    expect(await verifyCode("Ab3xYz", "marco@lube.it", "042917", fetchMock)).toEqual({ kind: "verified" });
    expect(fetchMock.mock.calls[0][0]).toBe("/api/links/Ab3xYz/verify");
    expect(JSON.parse((fetchMock.mock.calls[0][1] as RequestInit).body as string)).toEqual({ email: "marco@lube.it", code: "042917" });
  });

  it("answers pending when the email is a forward an admin has not kept yet (L-3)", async () => {
    const fetchMock = vi.fn().mockResolvedValue(Response.json({ ok: true, awaitingApproval: true }));

    expect(await verifyCode("Ab3xYz", "scout@other.com", "042917", fetchMock)).toEqual({ kind: "pending" });
  });

  it("answers why a code was refused, with the tries left", async () => {
    const fetchMock = vi.fn().mockResolvedValue(problem(422, { code: "email_code.invalid", title: "That code is not right. 3 tries left.", attemptsLeft: 3 }));

    expect(await verifyCode("Ab3xYz", "marco@lube.it", "000000", fetchMock)).toEqual({
      kind: "refused",
      code: "email_code.invalid",
      message: "That code is not right. 3 tries left.",
      attemptsLeft: 3,
    });
  });
});

describe("openWithDevice", () => {
  it("answers opened when the device opens the link", async () => {
    const fetchMock = vi.fn().mockResolvedValue(Response.json({ ok: true }));

    expect(await openWithDevice("Ab3xYz", fetchMock)).toBe("opened");
    expect(fetchMock).toHaveBeenCalledWith("/api/links/Ab3xYz/session", expect.objectContaining({ method: "POST" }));
  });

  it("answers pending when the device's forward waits for an admin (L-3)", async () => {
    expect(await openWithDevice("Ab3xYz", vi.fn().mockResolvedValue(problem(403, { code: "share_link.forward_pending" })))).toBe("pending");
  });

  it("answers gate when the gate is needed, or the call fails", async () => {
    expect(await openWithDevice("Ab3xYz", vi.fn().mockResolvedValue(problem(401, { code: "viewer.verification_required" })))).toBe("gate");
    expect(await openWithDevice("Ab3xYz", vi.fn().mockRejectedValue(new TypeError("offline")))).toBe("gate");
  });
});

describe("codeDigits and formatCountdown", () => {
  it("keeps up to six digits", () => {
    expect(codeDigits("04 29-17x9")).toBe("042917");
    expect(codeDigits("abc")).toBe("");
  });

  it("formats seconds as m:ss", () => {
    expect(formatCountdown(42)).toBe("0:42");
    expect(formatCountdown(60)).toBe("1:00");
    expect(formatCountdown(5)).toBe("0:05");
    expect(formatCountdown(-3)).toBe("0:00");
  });
});
