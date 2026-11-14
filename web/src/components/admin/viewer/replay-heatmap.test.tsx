import { afterEach, describe, expect, it } from "vitest";
import { cleanup, render, screen } from "@testing-library/react";
import { ReplayHeatmap } from "./replay-heatmap";

/**
 * The replay heatmap of the viewer page (A-7, canvas `AdminViewer`): one bar per bucket of
 * `GET /admin/viewers/{id}/videos/{assetId}/heatmap`, its height relative to the most-watched bucket, darker red for
 * more replays ("darker = watched more"). An image with a text summary for screen readers.
 */

afterEach(cleanup);

describe("ReplayHeatmap", () => {
  it("draws one bar per bucket, scaled to the busiest one", () => {
    const { container } = render(<ReplayHeatmap title="Highlights 2026" buckets={[1, 2, 1, 0]} bucketSeconds={5} />);

    const bars = container.querySelectorAll("[data-bucket]");
    expect(bars).toHaveLength(4);
    expect((bars[1] as HTMLElement).style.height).toBe("100%");
    expect((bars[0] as HTMLElement).style.height).toBe("50%");
    expect((bars[3] as HTMLElement).style.height).toBe("0%");
  });

  it("is an image summarised for screen readers, with the most replayed moment", () => {
    render(<ReplayHeatmap title="Highlights 2026" buckets={[1, 2, 1, 0]} bucketSeconds={5} />);

    const image = screen.getByRole("img");
    expect(image.getAttribute("aria-label")).toBe("Replay heatmap of Highlights 2026: most replayed at 0:05–0:10 (2 plays).");
    expect(screen.getByText("Replay heatmap · Highlights 2026")).toBeTruthy();
    expect(screen.getByText("darker = watched more")).toBeTruthy();
  });

  it("says so when nothing was watched", () => {
    render(<ReplayHeatmap title="Serve compilation" buckets={[0, 0, 0]} bucketSeconds={5} />);

    expect(screen.getByText("Not watched yet.")).toBeTruthy();
    expect(screen.queryByRole("img")).toBeNull();
  });
});
