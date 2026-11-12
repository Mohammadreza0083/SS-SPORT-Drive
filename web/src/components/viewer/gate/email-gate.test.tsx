import { afterEach, describe, expect, it, vi } from "vitest";
import { act, cleanup, fireEvent, render, screen } from "@testing-library/react";
import type { GateLink } from "@/lib/viewer/gate";
import { EmailGate } from "./email-gate";
import { LinkUnavailable } from "./link-unavailable";

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
  vi.useRealTimers();
});

const personal: GateLink = {
  code: "Ab3xYz",
  type: "Personal",
  playerCount: 1,
  recipientHint: "m***@lube.it",
  player: {
    name: "Arash Tehrani",
    position: "OPP",
    nationality: { code: "IR", name: "Iran" },
  },
};

/** The answers the stubbed routes give, by the route's last segment. */
type Routes = Partial<Record<"session" | "email-code" | "verify", () => Response>>;

/**
 * Stubs `fetch` for the gate routes; a route without an answer refuses with 401.
 *
 * @param routes - The answers.
 * @returns The mock.
 */
function stubRoutes(routes: Routes) {
  const fetchMock = vi.fn(async (input: RequestInfo | URL) => {
    const action = String(input).split("/").pop() as keyof Routes;
    return routes[action]?.() ?? Response.json({ status: 401, code: "viewer.verification_required" }, { status: 401 });
  });
  vi.stubGlobal("fetch", fetchMock);
  return fetchMock;
}

/** The answer to a code request. */
const sent = () => Response.json({ email: "m***@lube.it", resendAfterSeconds: 60, expiresInSeconds: 600 }, { status: 202 });

/**
 * Types an email and sends it.
 *
 * @param email - The email.
 */
async function sendEmail(email = "marco@lube.it") {
  fireEvent.change(await screen.findByLabelText("Email"), {
    target: { value: email },
  });
  fireEvent.click(screen.getByRole("button", { name: "Send code" }));
}

describe("EmailGate (L-2, canvas Gate)", () => {
  it("shows the player card and the masked recipient before anything is typed", async () => {
    stubRoutes({});

    render(<EmailGate link={personal} />);

    expect(await screen.findByRole("heading", { name: "Confirm it's you" })).toBeTruthy();
    expect(screen.getByText("OPP · IRAN")).toBeTruthy();
    expect(screen.getByText("Arash").closest(".gate-name")?.textContent).toBe("ArashTehrani");
    expect(screen.getByText("m***@lube.it").tagName).toBe("B");
    expect(screen.getByRole("link", { name: "Privacy" })).toBeTruthy();
  });

  it("a roster link shows how many players it holds", async () => {
    stubRoutes({});

    render(<EmailGate link={{ ...personal, type: "Roster", playerCount: 6, player: null }} />);

    expect(await screen.findByText("6 players")).toBeTruthy();
  });

  it("goes from the email to the code, then to You're in", async () => {
    const fetchMock = stubRoutes({
      "email-code": sent,
      verify: () => Response.json({ ok: true }),
    });
    render(<EmailGate link={personal} />);

    await sendEmail();
    expect(await screen.findByRole("heading", { name: "Check your inbox" })).toBeTruthy();
    expect(screen.getByText("marco@lube.it").tagName).toBe("B");

    fireEvent.change(screen.getByLabelText("6-digit code"), {
      target: { value: "04 29 17" },
    });
    expect((screen.getByLabelText("6-digit code") as HTMLInputElement).value).toBe("042917");
    fireEvent.click(screen.getByRole("button", { name: "Watch" }));

    expect(await screen.findByRole("heading", { name: "You're in" })).toBeTruthy();
    expect(screen.getByText("Opening Arash Tehrani's page…")).toBeTruthy();
    expect(screen.getByText("This device will be remembered for 90 days.")).toBeTruthy();
    expect(fetchMock.mock.calls.map(([url]) => String(url))).toEqual(["/api/links/Ab3xYz/session", "/api/links/Ab3xYz/email-code", "/api/links/Ab3xYz/verify"]);
  });

  it("a short code shakes and says to enter all six digits, without calling the API", async () => {
    const fetchMock = stubRoutes({ "email-code": sent });
    render(<EmailGate link={personal} />);
    await sendEmail();
    await screen.findByRole("heading", { name: "Check your inbox" });

    fireEvent.change(screen.getByLabelText("6-digit code"), {
      target: { value: "123" },
    });
    fireEvent.click(screen.getByRole("button", { name: "Watch" }));

    expect(screen.getByText("Enter all 6 digits.")).toBeTruthy();
    expect(screen.getByLabelText("6-digit code").closest(".gate-shake")).toBeTruthy();
    expect(fetchMock).toHaveBeenCalledTimes(2);
  });

  it("a wrong code shows the API's message and keeps the code step", async () => {
    stubRoutes({
      "email-code": sent,
      verify: () =>
        Response.json(
          {
            status: 422,
            code: "email_code.invalid",
            title: "That code is not right. 4 tries left.",
            attemptsLeft: 4,
          },
          { status: 422 },
        ),
    });
    render(<EmailGate link={personal} />);
    await sendEmail();
    await screen.findByRole("heading", { name: "Check your inbox" });

    fireEvent.change(screen.getByLabelText("6-digit code"), {
      target: { value: "000000" },
    });
    fireEvent.click(screen.getByRole("button", { name: "Watch" }));

    expect(await screen.findByRole("alert")).toHaveProperty("textContent", "That code is not right. 4 tries left.");
    expect(screen.getByRole("heading", { name: "Check your inbox" })).toBeTruthy();
  });

  it("an empty email asks for one without calling the API", async () => {
    const fetchMock = stubRoutes({});
    render(<EmailGate link={personal} />);

    fireEvent.click(await screen.findByRole("button", { name: "Send code" }));

    expect(screen.getByRole("alert")).toHaveProperty("textContent", "Enter your email.");
    expect(fetchMock).toHaveBeenCalledTimes(1);
  });

  it("an email the link does not admit shows the refusal on the email step", async () => {
    stubRoutes({
      "email-code": () =>
        Response.json(
          {
            status: 422,
            code: "share_link.email_not_allowed",
            title: "This link was shared with someone else. Use the email it was sent to.",
          },
          { status: 422 },
        ),
    });
    render(<EmailGate link={personal} />);

    await sendEmail("scout@other.com");

    expect(await screen.findByRole("alert")).toHaveProperty("textContent", "This link was shared with someone else. Use the email it was sent to.");
    expect(screen.getByRole("heading", { name: "Confirm it's you" })).toBeTruthy();
  });

  it("counts down before a new code may be sent, then offers to resend", async () => {
    // Only the clock and the countdown's interval are fake, so Testing Library's polling still runs on real timers.
    vi.useFakeTimers({ toFake: ["Date", "setInterval", "clearInterval"] });
    stubRoutes({ "email-code": sent });
    render(<EmailGate link={personal} />);
    await sendEmail();
    await screen.findByRole("heading", { name: "Check your inbox" });

    expect(screen.getByText("Resend in 1:00")).toBeTruthy();
    // Let React start the countdown's interval before the clock moves.
    await act(async () => {});
    await act(async () => {
      vi.advanceTimersByTime(18_000);
    });
    expect(await screen.findByText("Resend in 0:42")).toBeTruthy();
    await act(async () => {
      vi.advanceTimersByTime(42_000);
    });
    expect(await screen.findByRole("button", { name: "Resend code" })).toBeTruthy();
  });

  it("Wrong email? goes back to the email step", async () => {
    stubRoutes({ "email-code": sent });
    render(<EmailGate link={personal} />);
    await sendEmail();
    await screen.findByRole("heading", { name: "Check your inbox" });

    fireEvent.click(screen.getByRole("button", { name: "Wrong email?" }));

    expect(screen.getByRole("heading", { name: "Confirm it's you" })).toBeTruthy();
  });

  it("a forward waits for approval after the right code (L-3)", async () => {
    stubRoutes({
      "email-code": sent,
      verify: () => Response.json({ ok: true, awaitingApproval: true }),
    });
    render(<EmailGate link={personal} />);

    await sendEmail("scout@other.com");
    fireEvent.change(await screen.findByLabelText("6-digit code"), {
      target: { value: "042917" },
    });
    fireEvent.click(screen.getByRole("button", { name: "Watch" }));

    expect(await screen.findByRole("heading", { name: "Waiting for approval" })).toBeTruthy();
    expect(screen.queryByRole("heading", { name: "You're in" })).toBeNull();
  });

  it("a device whose forward still waits shows the waiting step at once (L-3)", async () => {
    stubRoutes({ session: () => Response.json({ status: 403, code: "share_link.forward_pending" }, { status: 403 }) });

    render(<EmailGate link={personal} />);

    expect(await screen.findByRole("heading", { name: "Waiting for approval" })).toBeTruthy();
  });

  it("a viewer with a session starts on You're in, without asking the API", () => {
    const fetchMock = stubRoutes({});

    render(<EmailGate link={personal} signedIn />);

    expect(screen.getByRole("heading", { name: "You're in" })).toBeTruthy();
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("a trusted device skips the gate", async () => {
    stubRoutes({ session: () => Response.json({ ok: true }) });

    render(<EmailGate link={personal} />);

    expect(await screen.findByRole("heading", { name: "You're in" })).toBeTruthy();
  });

  it("opens the page once the loading bar has run (V-2b)", async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    stubRoutes({ session: () => Response.json({ ok: true }) });
    const onOpened = vi.fn();

    render(<EmailGate link={personal} onOpened={onOpened} />);
    expect(await screen.findByRole("heading", { name: "You're in" })).toBeTruthy();
    // Let React run the effects of the "You're in" render before the clock moves.
    await act(async () => {});

    act(() => vi.advanceTimersByTime(2900));
    expect(onOpened).not.toHaveBeenCalled();
    act(() => vi.advanceTimersByTime(200));
    expect(onOpened).toHaveBeenCalledOnce();
  });

  it("does not reload a page that is already signed in", () => {
    vi.useFakeTimers();
    stubRoutes({});
    const onOpened = vi.fn();

    render(<EmailGate link={personal} signedIn onOpened={onOpened} />);
    act(() => vi.advanceTimersByTime(10_000));

    expect(onOpened).not.toHaveBeenCalled();
  });
});

describe("LinkUnavailable", () => {
  it("says the link does not open and offers the email contact", () => {
    render(<LinkUnavailable />);

    expect(screen.getByRole("heading", { name: "This link is not available" })).toBeTruthy();
    expect(screen.getByRole("link", { name: "info@ss-sport.co" }).getAttribute("href")).toBe("mailto:info@ss-sport.co");
  });
});
