import { afterEach, describe, expect, it, vi } from "vitest";
import { cleanup, render, screen } from "@testing-library/react";
import { MiniAppHome } from "./mini-app-home";

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

/**
 * Pretends the page runs inside Telegram with the given initData, and stubs the profile endpoint.
 *
 * @param initData - What Telegram gives the page.
 * @param response - What `/api/miniapp/me` answers.
 */
function insideTelegram(initData: string, response: Response) {
  vi.stubGlobal("Telegram", { WebApp: { initData, ready: vi.fn(), expand: vi.fn() } });
  vi.stubGlobal("fetch", vi.fn().mockResolvedValue(response));
}

describe("MiniAppHome", () => {
  it("greets the signed-in team member with their role", async () => {
    insideTelegram("user=1&hash=ab", Response.json({ id: "1", telegramId: 1, displayName: "Reza Ahmadi", username: "reza", role: "Analyst" }));

    render(<MiniAppHome telegramReady />);

    expect((await screen.findByRole("heading", { level: 1 })).textContent).toBe("Hello, Reza Ahmadi");
    expect(screen.getByText("Analyst")).toBeTruthy();
    expect(screen.getByRole("link", { name: "My players" }).getAttribute("href")).toBe("/miniapp/players");
  });

  it("tells a user without team access to ask the bot", async () => {
    insideTelegram("user=1&hash=ab", new Response(null, { status: 403 }));

    render(<MiniAppHome telegramReady />);

    expect(await screen.findByText(/ask for access in the bot/i)).toBeTruthy();
  });

  it("asks to reopen the Mini App when the sign-in is rejected or stale", async () => {
    insideTelegram("user=1&hash=ab", new Response(null, { status: 401 }));

    render(<MiniAppHome telegramReady />);

    expect(await screen.findByText(/close and reopen/i)).toBeTruthy();
  });

  it("explains that the page only works inside Telegram", async () => {
    insideTelegram("", Response.json({}));

    render(<MiniAppHome telegramReady />);

    expect(await screen.findByText(/open it from the SS-SPORT Drive bot/i)).toBeTruthy();
    expect(fetch).not.toHaveBeenCalled();
  });

  it("waits for the Telegram script before signing in", () => {
    insideTelegram("user=1&hash=ab", Response.json({}));

    render(<MiniAppHome telegramReady={false} />);

    expect(screen.getByText(/signing you in/i)).toBeTruthy();
    expect(fetch).not.toHaveBeenCalled();
  });
});
