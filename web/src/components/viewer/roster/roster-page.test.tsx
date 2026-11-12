import { afterEach, describe, expect, it } from "vitest";
import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { RosterPage } from "./roster-page";
import { sampleRoster } from "./sample";

afterEach(cleanup);

describe("RosterPage", () => {
  it("is headed Roster and says who it was shared with and how many players", () => {
    render(<RosterPage data={sampleRoster} year={2026} />);

    expect(screen.getByRole("heading", { level: 1, name: "Roster" })).toBeTruthy();
    expect(screen.getByText((_, el) => el?.textContent === "Shared privately with Club Aurora · 9 players" && el.tagName === "DIV")).toBeTruthy();
    expect(screen.getByText("SS-SPORT PLAYERS · 2026")).toBeTruthy();
  });

  it("says one player, not one players", () => {
    render(<RosterPage data={{ ...sampleRoster, playerCount: 1, players: sampleRoster.players.slice(0, 1) }} year={2026} />);

    expect(screen.getByText((_, el) => el?.textContent === "Shared privately with Club Aurora · 1 player" && el.tagName === "DIV")).toBeTruthy();
  });

  it("shows a card per player that opens the player's page", () => {
    render(<RosterPage data={sampleRoster} year={2026} />);

    const cards = screen.getAllByRole("link", { name: /View player/ });
    expect(cards).toHaveLength(9);
    expect(cards[0].getAttribute("href")).toBe(sampleRoster.players[0].href);
    expect(cards[0].textContent).toContain("OPP");
    expect(cards[0].textContent).toContain("Iran · 6 videos");
  });

  it("puts the first name and the rest of the name on two lines", () => {
    const { container } = render(<RosterPage data={sampleRoster} year={2026} />);

    const name = container.querySelector(".rs-name")!;
    expect(name.textContent).toBe("Arash\nTehrani");
  });

  it("filters by position and marks the chosen chip", () => {
    render(<RosterPage data={sampleRoster} year={2026} />);

    fireEvent.click(screen.getByRole("button", { name: "OH" }));

    expect(screen.getByRole("button", { name: "OH" }).getAttribute("aria-pressed")).toBe("true");
    expect(screen.getByRole("button", { name: "All" }).getAttribute("aria-pressed")).toBe("false");
    const cards = screen.getAllByRole("link", { name: /View player/ });
    expect(cards).toHaveLength(3);
    cards.forEach((card) => expect(card.textContent).toContain("OH"));

    fireEvent.click(screen.getByRole("button", { name: "All" }));
    expect(screen.getAllByRole("link", { name: /View player/ })).toHaveLength(9);
  });

  it("shows the photo when there is one and the canvas silhouette otherwise", () => {
    const players = [{ ...sampleRoster.players[0], photoUrl: "/api/links/Ab3xYz/players/p1/photo" }, sampleRoster.players[1]];
    const { container } = render(<RosterPage data={{ ...sampleRoster, playerCount: 2, players }} year={2026} />);

    const photos = container.querySelectorAll(".rs-photo");
    expect(photos[0].tagName).toBe("IMG");
    expect(photos[0].getAttribute("src")).toBe("/api/links/Ab3xYz/players/p1/photo");
    expect(photos[0].getAttribute("draggable")).toBe("false");
    expect(photos[1].tagName.toLowerCase()).toBe("svg");
  });

  it("offers the email contact", () => {
    render(<RosterPage data={sampleRoster} year={2026} />);

    expect(screen.getByRole("link", { name: "Contact SS-SPORT" }).getAttribute("href")).toBe("mailto:info@ss-sport.co");
  });
});
