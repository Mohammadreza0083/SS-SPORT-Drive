import { afterEach, describe, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { SignInPanel } from "./sign-in-panel";
import type { SignInClient } from "./sign-in-client";

afterEach(cleanup);

/**
 * A client whose request succeeds unless a test says otherwise.
 *
 * @param overrides - Replacement methods.
 * @returns The client, with spies.
 */
function fakeClient(overrides: Partial<SignInClient> = {}): SignInClient {
  return { requestLink: vi.fn().mockResolvedValue({ kind: "sent" }), ...overrides };
}

/**
 * Types an email and presses the send button.
 *
 * @param email - What goes in the box.
 */
function ask(email: string) {
  fireEvent.change(screen.getByLabelText("Email"), { target: { value: email } });
  fireEvent.click(screen.getByRole("button", { name: "Send sign-in link" }));
}

describe("SignInPanel (W-1, P-5 web): asking for a link", () => {
  it("shows the viewer's heading and an Email box (any ordinary email works)", () => {
    render(<SignInPanel audience="viewer" client={fakeClient()} />);

    expect(screen.getByRole("heading", { level: 1, name: "Sign in" })).toBeTruthy();
    const box = screen.getByLabelText("Email");
    expect(box.getAttribute("type")).toBe("email");
    expect(box.getAttribute("autocomplete")).toBe("email");
  });

  it("names the admin console for admins", () => {
    render(<SignInPanel audience="admin" client={fakeClient()} />);

    expect(screen.getByRole("heading", { level: 1, name: "Admin sign-in" })).toBeTruthy();
  });

  it("refuses a malformed email without calling the server", () => {
    const client = fakeClient();
    render(<SignInPanel audience="viewer" client={client} />);

    ask("not-an-email");

    expect(screen.getByText("Enter a valid email.")).toBeTruthy();
    expect(client.requestLink).not.toHaveBeenCalled();
  });

  it("sends the trimmed email and then says to check the inbox", async () => {
    const client = fakeClient();
    render(<SignInPanel audience="viewer" client={client} />);

    ask("  scout@club.com ");

    expect(await screen.findByRole("heading", { name: "Check your email" })).toBeTruthy();
    expect(client.requestLink).toHaveBeenCalledWith("scout@club.com");
    expect(screen.getByText(/scout@club\.com/)).toBeTruthy();
    expect(screen.getByText(/works once, for 15 minutes/)).toBeTruthy();
  });

  it("asks to wait when there were too many links", async () => {
    render(<SignInPanel audience="viewer" client={fakeClient({ requestLink: vi.fn().mockResolvedValue({ kind: "too-many", retryAfterSeconds: 60 }) })} />);

    ask("scout@club.com");

    expect(await screen.findByText("Too many sign-in links. Try again in a minute.")).toBeTruthy();
  });

  it("says when something went wrong", async () => {
    render(<SignInPanel audience="viewer" client={fakeClient({ requestLink: vi.fn().mockResolvedValue({ kind: "failed" }) })} />);

    ask("scout@club.com");

    expect(await screen.findByText("Something went wrong. Please try again.")).toBeTruthy();
  });

  it("explains an expired or used link above the form", () => {
    render(<SignInPanel audience="viewer" error="expired" client={fakeClient()} />);

    expect(screen.getByRole("alert").textContent).toBe("That sign-in link has expired or was already used. Ask for a new one.");
    expect(screen.getByLabelText("Email")).toBeTruthy();
  });

  it("explains when the service is unavailable", () => {
    render(<SignInPanel audience="admin" error="unavailable" client={fakeClient()} />);

    expect(screen.getByRole("alert").textContent).toBe("Sign-in is unavailable right now. Please try again shortly.");
  });

  it("ignores an unknown error value", () => {
    render(<SignInPanel audience="viewer" error="<script>" client={fakeClient()} />);

    expect(screen.queryByRole("alert")).toBeNull();
  });
});

describe("SignInPanel: opening the emailed link", () => {
  it("never spends the token by itself: Continue posts it to the viewer session route", () => {
    const client = fakeClient();
    const { container } = render(<SignInPanel audience="viewer" token="one-time-token" client={client} />);

    const form = container.querySelector("form");
    expect(form?.getAttribute("method")).toBe("post");
    expect(form?.getAttribute("action")).toBe("/api/auth/viewer/session");
    const hidden = form?.querySelector('input[name="token"]');
    expect(hidden?.getAttribute("type")).toBe("hidden");
    expect(hidden?.getAttribute("value")).toBe("one-time-token");
    expect(screen.getByRole("button", { name: "Continue" }).getAttribute("type")).toBe("submit");
    expect(screen.queryByLabelText("Email")).toBeNull();
    expect(client.requestLink).not.toHaveBeenCalled();
  });

  it("posts an admin's token to the admin session route", () => {
    const { container } = render(<SignInPanel audience="admin" token="one-time-token" client={fakeClient()} />);

    expect(container.querySelector("form")?.getAttribute("action")).toBe("/api/auth/admin/session");
  });
});
