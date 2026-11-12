import { afterEach, describe, expect, it } from "vitest";
import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { HomePage } from "./home-page";
import { sampleHome } from "./sample";

afterEach(cleanup);

/** The player cards on the page (each is a link named "View <name>"). */
const cards = () => screen.queryAllByRole("link", { name: /^View / });

describe("HomePage (P-1, canvas Home)", () => {
  it("is headed like the canvas", () => {
    render(<HomePage data={sampleHome} />);

    expect(screen.getByRole("heading", { level: 1, name: "Find your next player." })).toBeTruthy();
    expect(screen.getByText("SS-SPORT DRIVE · PLAYER SEARCH")).toBeTruthy();
  });

  it("shows the canvas's eight sample players, each opening its locked page", () => {
    render(<HomePage data={sampleHome} />);

    expect(sampleHome.players).toHaveLength(8);
    expect(cards()).toHaveLength(8);
    const first = cards()[0];
    expect(first.getAttribute("href")).toBe(`/players/${sampleHome.players[0].slug}`);
    expect(first.textContent).toContain("OPP");
    expect(first.textContent).toContain("Iran");
    expect(first.textContent).toContain("6 videos · 2 docs");
    expect(first.textContent).toContain("Private");
  });

  it("puts the first name and the rest of the name on two lines", () => {
    const { container } = render(<HomePage data={sampleHome} />);

    expect(container.querySelector(".ph-name")!.textContent).toBe("Arash\nTehrani");
  });

  it("has a labelled search box with the live result count", () => {
    render(<HomePage data={sampleHome} />);

    const search = screen.getByRole("searchbox", { name: "Search players by name" });
    expect(search.getAttribute("placeholder")).toBe("Search by player name…");
    expect(screen.getByText("8 players")).toBeTruthy();
  });

  it("filters the cards as the visitor types", () => {
    render(<HomePage data={sampleHome} />);

    fireEvent.change(screen.getByRole("searchbox", { name: "Search players by name" }), { target: { value: "tehr" } });

    expect(cards()).toHaveLength(1);
    expect(screen.getByText("1 player")).toBeTruthy();
  });

  it("filters by position and marks the chosen chip", () => {
    render(<HomePage data={sampleHome} />);

    fireEvent.click(screen.getByRole("button", { name: "OH" }));

    expect(screen.getByRole("button", { name: "OH" }).getAttribute("aria-pressed")).toBe("true");
    expect(screen.getByRole("button", { name: "All" }).getAttribute("aria-pressed")).toBe("false");
    expect(cards()).toHaveLength(3);
    cards().forEach((card) => expect(card.textContent).toContain("OH"));
  });

  it("says nothing matched, with the contact address, when no player fits", () => {
    render(<HomePage data={sampleHome} />);

    fireEvent.change(screen.getByRole("searchbox", { name: "Search players by name" }), { target: { value: "zzz" } });

    expect(cards()).toHaveLength(0);
    expect(screen.getByText('No players match "zzz"')).toBeTruthy();
    expect(screen.getAllByRole("link", { name: "info@ss-sport.co" })[0].getAttribute("href")).toBe("mailto:info@ss-sport.co");
  });

  it("explains how access works in three steps", () => {
    render(<HomePage data={sampleHome} />);

    expect(screen.getByRole("heading", { level: 2, name: "How access works" })).toBeTruthy();
    expect(screen.getByText("Find a player")).toBeTruthy();
    expect(screen.getByText("Request access")).toBeTruthy();
    expect(screen.getByText("Get your private link")).toBeTruthy();
    expect(screen.getByRole("link", { name: "How access works" }).getAttribute("href")).toBe("#how");
  });

  it("offers Sign in to a visitor who is signed out (P-7)", () => {
    render(<HomePage data={{ ...sampleHome, viewer: null }} />);

    expect(screen.getByRole("link", { name: "Sign in" }).getAttribute("href")).toBe("/signin");
    expect(screen.queryByRole("button", { name: "Sign out" })).toBeNull();
  });

  it("shows a signed-in viewer's email and a Sign out button that posts (P-7)", () => {
    render(<HomePage data={{ ...sampleHome, viewer: { email: "j.demir@aurora-volley.example" } }} />);

    expect(screen.getByText("j.demir@aurora-volley.example")).toBeTruthy();
    const signOut = screen.getByRole("button", { name: "Sign out" });
    const form = signOut.closest("form")!;
    expect(form.getAttribute("method")).toBe("post");
    expect(form.getAttribute("action")).toBe("/api/viewer/sign-out");
    expect(screen.queryByRole("link", { name: "Sign in" })).toBeNull();
  });

  it("shows the photo when there is one and the canvas silhouette otherwise", () => {
    const players = [{ ...sampleHome.players[0], photoUrl: "/api/public/players/arash-tehrani/photo" }, sampleHome.players[1]];
    const { container } = render(<HomePage data={{ ...sampleHome, players }} />);

    const photos = container.querySelectorAll(".ph-photo");
    expect(photos[0].tagName).toBe("IMG");
    expect(photos[0].getAttribute("src")).toBe("/api/public/players/arash-tehrani/photo");
    expect(photos[0].getAttribute("draggable")).toBe("false");
    expect(photos[1].tagName.toLowerCase()).toBe("svg");
  });
});
