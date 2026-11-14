import { afterEach, describe, expect, it } from "vitest";
import { cleanup, render, screen } from "@testing-library/react";
import { AdminHome } from "./admin-home";

afterEach(cleanup);

/**
 * `/admin` after sign-in (W-1). The console itself is Epic 7; until then the page only says who is signed in and
 * offers Sign out, a POST form (so a prefetch can never sign anyone out).
 */
describe("AdminHome", () => {
  it("names the signed-in admin and offers a sign-out form", () => {
    const { container } = render(<AdminHome admin={{ email: "owner@ss-sport.co", displayName: "Owner" }} />);

    expect(screen.getByRole("heading", { level: 1, name: "SS-SPORT Drive console" })).toBeTruthy();
    expect(screen.getByText("Signed in as Owner (owner@ss-sport.co)")).toBeTruthy();
    const form = container.querySelector("form");
    expect(form?.getAttribute("method")).toBe("post");
    expect(form?.getAttribute("action")).toBe("/api/admin/sign-out");
    expect(screen.getByRole("button", { name: "Sign out" }).getAttribute("type")).toBe("submit");
  });
});
