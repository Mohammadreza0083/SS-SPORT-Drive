import { describe, expect, it } from "vitest";
import { formatDuration, tickerItems, videoTags } from "./format";
import { samplePlayerPage } from "./sample";

describe("formatDuration", () => {
  it.each([
    [84, "1:24"],
    [192, "3:12"],
    [6490, "1:48:10"],
    [5, "0:05"],
    [3600, "1:00:00"],
  ])("writes %i s as %s", (seconds, text) => {
    expect(formatDuration(seconds)).toBe(text);
  });
});

describe("videoTags", () => {
  it("lists All first, then each tag once in display order", () => {
    expect(videoTags(samplePlayerPage.videos)).toEqual(["All", "Highlight", "Spike", "Serve", "Block", "Full match"]);
  });
});

describe("tickerItems", () => {
  it("scrolls name, position, country, video count, full match and the brand", () => {
    expect(tickerItems(samplePlayerPage)).toEqual(["Arash Tehrani", "Opposite", "Iran", "6 Videos", "Full Match Included", "SS-SPORT"]);
  });

  it("leaves out the full-match line when there is none, and says Video for one", () => {
    const data = { ...samplePlayerPage, stats: { ...samplePlayerPage.stats, videos: 1 }, videos: samplePlayerPage.videos.slice(0, 1) };

    expect(tickerItems(data)).toEqual(["Arash Tehrani", "Opposite", "Iran", "1 Video", "SS-SPORT"]);
  });
});
