import { afterEach, describe, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { RequestAccessModal } from "./request-access-modal";
import type { RequestAccessClient } from "./types";

afterEach(cleanup);

const player = { slug: "arash-tehrani", name: "Arash Tehrani", position: "OPP" };

/**
 * A client whose calls succeed unless a test says otherwise.
 *
 * @param overrides - Replacement methods.
 * @returns The client, with spies.
 */
function fakeClient(overrides: Partial<RequestAccessClient> = {}): RequestAccessClient {
  return {
    requestCode: vi.fn().mockResolvedValue({ kind: "sent", resendAfterSeconds: 60 }),
    submit: vi.fn().mockResolvedValue({ kind: "sent" }),
    ...overrides,
  };
}

/**
 * Fills the form of a signed-out visitor and asks for a code.
 *
 * @param email - What goes in the email box.
 * @param about - What goes in the About you box.
 */
function fillForm(email: string, about: string) {
  fireEvent.change(screen.getByLabelText("Email"), { target: { value: email } });
  fireEvent.change(screen.getByLabelText("About you"), { target: { value: about } });
  fireEvent.click(screen.getByRole("button", { name: "Send verification code" }));
}

describe("RequestAccessModal (P-3 UI, canvas Locked modal), signed out", () => {
  it("is a labelled modal dialog naming the player", () => {
    render(<RequestAccessModal player={player} viewer={null} client={fakeClient()} onClose={() => {}} />);

    const dialog = screen.getByRole("dialog", { name: "Request access" });
    expect(dialog.getAttribute("aria-modal")).toBe("true");
    expect(screen.getByText("ARASH TEHRANI · OPP")).toBeTruthy();
    expect(screen.getByLabelText("About you").getAttribute("placeholder")).toBe("Your club or agency, your role, and what you're looking for.");
    expect(screen.getByRole("link", { name: "Sign in" }).getAttribute("href")).toBe("/signin");
  });

  it("refuses a bad email or an empty description without calling the server", () => {
    const client = fakeClient();
    render(<RequestAccessModal player={player} viewer={null} client={client} onClose={() => {}} />);

    fillForm("not-an-email", "Head scout at Club Aurora");
    expect(screen.getByText("Add a valid email and a few words about you.")).toBeTruthy();

    fillForm("scout@club.com", "   ");
    expect(screen.getByText("Add a valid email and a few words about you.")).toBeTruthy();
    expect(client.requestCode).not.toHaveBeenCalled();
  });

  it("refuses a description over 1000 characters", () => {
    const client = fakeClient();
    render(<RequestAccessModal player={player} viewer={null} client={client} onClose={() => {}} />);

    fillForm("scout@club.com", "x".repeat(1001));

    expect(screen.getByText("Add a valid email and a few words about you.")).toBeTruthy();
    expect(client.requestCode).not.toHaveBeenCalled();
  });

  it("sends a code to the trimmed email and moves to the code step", async () => {
    const client = fakeClient();
    render(<RequestAccessModal player={player} viewer={null} client={client} onClose={() => {}} />);

    fillForm("  scout@club.com ", "Head scout at Club Aurora");

    expect(await screen.findByRole("heading", { name: "Check your inbox" })).toBeTruthy();
    expect(client.requestCode).toHaveBeenCalledWith("scout@club.com");
    expect(screen.getByText("scout@club.com")).toBeTruthy();
    expect(screen.getByText("Resend in 1:00")).toBeTruthy();
  });

  it("stays on the form and shows why when the code was refused", async () => {
    const client = fakeClient({
      requestCode: vi.fn().mockResolvedValue({ kind: "refused", code: "email_code.too_many", message: "Too many codes. Try again later.", retryAfterSeconds: 600 }),
    });
    render(<RequestAccessModal player={player} viewer={null} client={client} onClose={() => {}} />);

    fillForm("scout@club.com", "Head scout at Club Aurora");

    expect(await screen.findByText("Too many codes. Try again later.")).toBeTruthy();
    expect(screen.queryByRole("heading", { name: "Check your inbox" })).toBeNull();
  });

  it("keeps only digits in the code box, six at most", async () => {
    render(<RequestAccessModal player={player} viewer={null} client={fakeClient()} onClose={() => {}} />);
    fillForm("scout@club.com", "Head scout at Club Aurora");
    const code = (await screen.findByLabelText("6-digit code")) as HTMLInputElement;

    fireEvent.change(code, { target: { value: "12a-34 5678" } });

    expect(code.value).toBe("123456");
    expect(code.getAttribute("inputmode")).toBe("numeric");
    expect(code.getAttribute("autocomplete")).toBe("one-time-code");
  });

  it("asks for all six digits before sending", async () => {
    const client = fakeClient();
    render(<RequestAccessModal player={player} viewer={null} client={client} onClose={() => {}} />);
    fillForm("scout@club.com", "Head scout at Club Aurora");

    fireEvent.change(await screen.findByLabelText("6-digit code"), { target: { value: "123" } });
    fireEvent.click(screen.getByRole("button", { name: "Verify and send request" }));

    expect(screen.getByText("Enter all 6 digits.")).toBeTruthy();
    expect(client.submit).not.toHaveBeenCalled();
  });

  it("sends the request with the code and shows the confirmation", async () => {
    const client = fakeClient();
    render(<RequestAccessModal player={player} viewer={null} client={client} onClose={() => {}} />);
    fillForm("scout@club.com", "  Head scout at Club Aurora ");

    fireEvent.change(await screen.findByLabelText("6-digit code"), { target: { value: "042917" } });
    fireEvent.click(screen.getByRole("button", { name: "Verify and send request" }));

    expect(await screen.findByRole("heading", { name: "Request sent" })).toBeTruthy();
    expect(client.submit).toHaveBeenCalledWith({
      playerSlug: "arash-tehrani",
      about: "Head scout at Club Aurora",
      email: "scout@club.com",
      code: "042917",
      turnstileToken: null,
    });
    expect(screen.getByText("Email verified")).toBeTruthy();
    expect(screen.getByRole("link", { name: "Back to search" }).getAttribute("href")).toBe("/");
  });

  it("stays on the code step and shows why when the code is wrong", async () => {
    const client = fakeClient({
      submit: vi.fn().mockResolvedValue({ kind: "refused", code: "email_code.invalid", message: "That code is not right. 4 tries left.", attemptsLeft: 4 }),
    });
    render(<RequestAccessModal player={player} viewer={null} client={client} onClose={() => {}} />);
    fillForm("scout@club.com", "Head scout at Club Aurora");

    fireEvent.change(await screen.findByLabelText("6-digit code"), { target: { value: "111111" } });
    fireEvent.click(screen.getByRole("button", { name: "Verify and send request" }));

    expect(await screen.findByText("That code is not right. 4 tries left.")).toBeTruthy();
    expect(screen.getByRole("heading", { name: "Check your inbox" })).toBeTruthy();
  });

  it("goes back to the form, email kept, from Wrong email?", async () => {
    render(<RequestAccessModal player={player} viewer={null} client={fakeClient()} onClose={() => {}} />);
    fillForm("scout@club.com", "Head scout at Club Aurora");

    fireEvent.click(await screen.findByRole("button", { name: "Wrong email?" }));

    expect((screen.getByLabelText("Email") as HTMLInputElement).value).toBe("scout@club.com");
  });

  it("calls onClose from the Close button", () => {
    const onClose = vi.fn();
    render(<RequestAccessModal player={player} viewer={null} client={fakeClient()} onClose={onClose} />);

    fireEvent.click(screen.getByRole("button", { name: "Close" }));

    expect(onClose).toHaveBeenCalledOnce();
  });

  it("passes the Turnstile token when the page provides one", async () => {
    const client = fakeClient();
    render(<RequestAccessModal player={player} viewer={null} client={client} onClose={() => {}} getTurnstileToken={() => "tt-123"} />);
    fillForm("scout@club.com", "Head scout at Club Aurora");

    fireEvent.change(await screen.findByLabelText("6-digit code"), { target: { value: "042917" } });
    fireEvent.click(screen.getByRole("button", { name: "Verify and send request" }));

    await screen.findByRole("heading", { name: "Request sent" });
    expect(client.submit).toHaveBeenCalledWith(expect.objectContaining({ turnstileToken: "tt-123" }));
  });
});

describe("RequestAccessModal, any ordinary email", () => {
  it("labels the box Email, not Work email, and accepts a personal address", async () => {
    const client = fakeClient();
    render(<RequestAccessModal player={player} viewer={null} client={client} onClose={() => {}} />);
    expect(screen.queryByLabelText("Work email")).toBeNull();

    fillForm("someone.private@gmail.com", "Independent agent looking for an outside hitter");

    expect(await screen.findByRole("heading", { name: "Check your inbox" })).toBeTruthy();
    expect(client.requestCode).toHaveBeenCalledWith("someone.private@gmail.com");
  });
});

describe("RequestAccessModal, signed in (P-7)", () => {
  const viewer = { email: "j.demir@aurora-volley.example" };

  it("asks for no email and no code", () => {
    render(<RequestAccessModal player={player} viewer={viewer} client={fakeClient()} onClose={() => {}} />);

    expect(screen.queryByLabelText("Email")).toBeNull();
    expect(screen.getByText("j.demir@aurora-volley.example")).toBeTruthy();
    expect(screen.getByText(/signed in, no code needed/)).toBeTruthy();
    expect(screen.getByRole("button", { name: "Send request" })).toBeTruthy();
  });

  it("sends the request in one click, straight to the confirmation", async () => {
    const client = fakeClient();
    render(<RequestAccessModal player={player} viewer={viewer} client={client} onClose={() => {}} />);

    fireEvent.change(screen.getByLabelText("About you"), { target: { value: "Head scout at Club Aurora" } });
    fireEvent.click(screen.getByRole("button", { name: "Send request" }));

    expect(await screen.findByRole("heading", { name: "Request sent" })).toBeTruthy();
    expect(client.requestCode).not.toHaveBeenCalled();
    expect(client.submit).toHaveBeenCalledWith({ playerSlug: "arash-tehrani", about: "Head scout at Club Aurora", turnstileToken: null });
    expect(screen.getByText("j.demir@aurora-volley.example")).toBeTruthy();
  });

  it("still needs a few words about the viewer", () => {
    const client = fakeClient();
    render(<RequestAccessModal player={player} viewer={viewer} client={client} onClose={() => {}} />);

    fireEvent.click(screen.getByRole("button", { name: "Send request" }));

    expect(screen.getByText("Add a valid email and a few words about you.")).toBeTruthy();
    expect(client.submit).not.toHaveBeenCalled();
  });
});
