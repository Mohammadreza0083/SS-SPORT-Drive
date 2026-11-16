import { afterEach, describe, expect, it } from "vitest";
import { cleanup, render, screen, within } from "@testing-library/react";
import { AdminNav } from "./admin-nav";

/**
 * The console's side navigation (every admin board on the canvas shares it): the sections in canvas order, the current
 * one marked, red count badges for the waiting list and access requests (hidden at zero), and the admin's card with the
 * session's days left and a Sign out button that posts to `/api/admin/sign-out` (a POST, so a prefetch never signs out).
 */

afterEach(cleanup);

const admin = { displayName: "Mohammadreza", email: "mr@ss-sport.co", expiresAt: "2026-11-20T11:00:00Z" };

describe("AdminNav", () => {
  it("lists the sections in canvas order with their addresses", () => {
    render(<AdminNav current="dashboard" counts={{ waiting: 5, accessRequests: 3 }} admin={admin} now={new Date("2026-11-14T10:00:00Z")} />);

    const nav = screen.getByRole("navigation", { name: "Admin" });
    const links = within(nav).getAllByRole("link");
    expect(links.map((link) => [link.textContent?.replace(/\d+$/, "").trim(), link.getAttribute("href")])).toEqual([
      ["Dashboard", "/admin"],
      ["Players", "/admin/players"],
      ["Waiting list", "/admin/waiting"],
      ["Archive", "/admin/archive"],
      ["Access requests", "/admin/requests"],
      ["Share links", "/admin/links"],
      ["Viewers", "/admin/viewers"],
      ["Jobs & email queue", "/admin/jobs"],
      ["Users & roles", "/admin/users"],
      ["Settings", "/admin/settings"],
    ]);
  });

  it("marks the current section", () => {
    render(<AdminNav current="viewers" counts={{ waiting: 0, accessRequests: 0 }} admin={admin} now={new Date("2026-11-14T10:00:00Z")} />);

    expect(screen.getByRole("link", { name: /Viewers/ }).getAttribute("aria-current")).toBe("page");
    expect(screen.getByRole("link", { name: /Dashboard/ }).getAttribute("aria-current")).toBeNull();
  });

  it("shows count badges only when something waits", () => {
    render(<AdminNav current="dashboard" counts={{ waiting: 5, accessRequests: 0 }} admin={admin} now={new Date("2026-11-14T10:00:00Z")} />);

    expect(screen.getByRole("link", { name: /Waiting list/ }).textContent).toContain("5");
    expect(screen.getByRole("link", { name: /Access requests/ }).textContent).toBe("Access requests");
  });

  it("shows who is signed in, the session's days left and a POST sign-out", () => {
    const { container } = render(<AdminNav current="dashboard" counts={{ waiting: 0, accessRequests: 0 }} admin={admin} now={new Date("2026-11-14T10:00:00Z")} />);

    expect(screen.getByText("Mohammadreza")).toBeTruthy();
    expect(screen.getByText("Session · 6 days left")).toBeTruthy();
    const form = container.querySelector("form");
    expect(form?.getAttribute("method")?.toLowerCase()).toBe("post");
    expect(form?.getAttribute("action")).toBe("/api/admin/sign-out");
    expect(within(form as HTMLElement).getByRole("button", { name: "Sign out" })).toBeTruthy();
  });
});
