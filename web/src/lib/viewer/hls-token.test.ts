import { describe, expect, it } from "vitest";
import { REFRESH_EVERY_MS, swapToken, tokenFromUrl } from "./hls-token";

describe("tokenFromUrl", () => {
  it("reads the token from an edge HLS URL", () => {
    expect(tokenFromUrl("/hls/abc.def/players/a/videos/b/hls/master.m3u8")).toBe("abc.def");
    expect(tokenFromUrl("https://drive.ss-sport.co/hls/abc.def/players/a/hls/720p/seg_1.ts")).toBe("abc.def");
  });

  it("finds none in other URLs", () => {
    expect(tokenFromUrl("https://test-streams.mux.dev/x/x.m3u8")).toBeNull();
  });
});

describe("swapToken", () => {
  it("puts the latest token into every edge request, keeping the rest of the URL", () => {
    expect(swapToken("https://drive.ss-sport.co/hls/old.sig/players/a/hls/720p/seg_00042.ts", "new.sig")).toBe(
      "https://drive.ss-sport.co/hls/new.sig/players/a/hls/720p/seg_00042.ts",
    );
  });

  it("leaves URLs without a token alone", () => {
    expect(swapToken("https://test-streams.mux.dev/x/x.m3u8", "new.sig")).toBe("https://test-streams.mux.dev/x/x.m3u8");
  });

  it("leaves the URL alone when there is no newer token", () => {
    expect(swapToken("/hls/old.sig/a/master.m3u8", null)).toBe("/hls/old.sig/a/master.m3u8");
  });
});

describe("REFRESH_EVERY_MS", () => {
  it("renews well inside the 30-minute token lifetime", () => {
    expect(REFRESH_EVERY_MS).toBe(20 * 60 * 1000);
  });
});
