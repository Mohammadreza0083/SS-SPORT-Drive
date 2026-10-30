import { afterEach, describe, expect, it } from "vitest";
import { cleanup, render, screen } from "@testing-library/react";
import { ContactButton, NavLink, SiteNav } from "./site-nav";
import { SiteFooter } from "./site-footer";
import { ButtonLink } from "./button";
import { Chip } from "./chip";
import { Eyebrow } from "./eyebrow";
import { DriftMark, Glow } from "./backdrop";

afterEach(cleanup);

describe("SiteNav", () => {
  it("shows the reversed SS-SPORT logo at the canvas height", () => {
    render(<SiteNav />);

    const logo = screen.getByRole("img", { name: "SS-SPORT" });
    expect(logo.getAttribute("src")).toContain("logo-horizontal-reversed.png");
    expect(logo.getAttribute("height")).toBe("40");
  });

  it("links the logo home when asked", () => {
    render(<SiteNav logoHref="/" />);

    expect(screen.getByRole("link", { name: "SS-SPORT home" }).getAttribute("href")).toBe("/");
  });

  it("holds the page links and the email-only contact button", () => {
    render(
      <SiteNav>
        <NavLink href="#videos">Videos</NavLink>
        <ContactButton />
      </SiteNav>,
    );

    expect(screen.getByRole("link", { name: "Videos" }).getAttribute("href")).toBe("#videos");
    expect(screen.getByRole("link", { name: "Contact SS-SPORT" }).getAttribute("href")).toBe("mailto:info@ss-sport.co");
  });
});

describe("SiteFooter", () => {
  it("shows the logo and the line it is given", () => {
    render(<SiteFooter>Shared privately with Club Aurora</SiteFooter>);

    expect(screen.getByRole("contentinfo").textContent).toContain("Shared privately with Club Aurora");
    expect(screen.getByRole("img", { name: "SS-SPORT" }).getAttribute("height")).toBe("30");
  });
});

describe("ButtonLink", () => {
  it.each([
    ["red", "ss-btn-red"],
    ["ghost", "ss-btn-ghost"],
    ["light", "ss-btn-light"],
  ] as const)("renders the %s variant as a real link", (variant, className) => {
    render(
      <ButtonLink href="#videos" variant={variant}>
        Watch highlights
      </ButtonLink>,
    );

    const link = screen.getByRole("link", { name: "Watch highlights" });
    expect(link.className).toContain("ss-btn");
    expect(link.className).toContain(className);
  });
});

describe("Chip", () => {
  it("is a toggle button that reports its pressed state", () => {
    render(<Chip pressed>OPP</Chip>);

    const chip = screen.getByRole("button", { name: "OPP" });
    expect(chip.getAttribute("aria-pressed")).toBe("true");
    expect(chip.className).toContain("is-on");
  });

  it("is off by default", () => {
    render(<Chip>All</Chip>);

    expect(screen.getByRole("button", { name: "All" }).getAttribute("aria-pressed")).toBe("false");
  });
});

describe("Eyebrow", () => {
  it("renders the mono red label", () => {
    render(<Eyebrow>SS-SPORT DRIVE · PLAYER SEARCH</Eyebrow>);

    expect(screen.getByText("SS-SPORT DRIVE · PLAYER SEARCH").className).toContain("ss-eyebrow");
  });
});

describe("backdrop", () => {
  it("hides the glow and the drifting mark from assistive technology", () => {
    const { container } = render(
      <>
        <Glow />
        <DriftMark />
      </>,
    );

    expect(container.querySelector(".ss-glow")?.getAttribute("aria-hidden")).toBe("true");
    const mark = container.querySelector("img.ss-drift");
    expect(mark?.getAttribute("alt")).toBe("");
    expect(mark?.getAttribute("aria-hidden")).toBe("true");
    expect(mark?.getAttribute("src")).toContain("mark.png");
  });
});
