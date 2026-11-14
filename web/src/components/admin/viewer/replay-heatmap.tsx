import { adminCopy } from "../copy";

/** Props of {@link ReplayHeatmap}. */
type ReplayHeatmapProps = {
  /** The video's title. */
  title: string;
  /** Per bucket, how many plays covered it. */
  buckets: number[];
  /** How wide each bucket is, in seconds. */
  bucketSeconds: number;
};

/**
 * Writes seconds as `m:ss`.
 *
 * @param seconds - The seconds.
 * @returns The text.
 */
function clock(seconds: number): string {
  const whole = Math.round(seconds);
  return `${Math.floor(whole / 60)}:${String(whole % 60).padStart(2, "0")}`;
}

/**
 * Picks a bar's colour: darker red for more replays (canvas `AdminViewer`).
 *
 * @param ratio - The bucket's share of the busiest bucket, 0 to 1.
 * @returns A CSS colour.
 */
function shade(ratio: number): string {
  if (ratio > 0.7) {
    return "#FF4545";
  }

  return ratio > 0.45 ? "#b11a1a" : "#5a1010";
}

/**
 * The replay heatmap of the viewer page (A-7): one bar per bucket of the API's heatmap, its height relative to the
 * most-watched bucket. It is an image with a text summary for screen readers; a video nobody watched says so instead.
 *
 * @param props - See {@link ReplayHeatmapProps}.
 * @returns The heatmap.
 */
export function ReplayHeatmap({ title, buckets, bucketSeconds }: ReplayHeatmapProps) {
  const peak = Math.max(0, ...buckets);
  return (
    <div className="adm-heat">
      <div className="adm-heat-head">
        <div className="adm-label">{adminCopy.heatmap.title(title)}</div>
        <span className="adm-heat-legend">{adminCopy.heatmap.legend}</span>
      </div>
      {peak === 0 ? (
        <p className="adm-muted">{adminCopy.heatmap.none}</p>
      ) : (
        <div
          role="img"
          className="adm-heat-bars"
          aria-label={adminCopy.heatmap.label(
            title,
            `${clock(buckets.indexOf(peak) * bucketSeconds)}–${clock((buckets.indexOf(peak) + 1) * bucketSeconds)}`,
            peak,
          )}
        >
          {buckets.map((count, index) => (
            <span key={index} data-bucket={index} className="adm-heat-bar" style={{ height: `${Math.round((count / peak) * 100)}%`, background: shade(count / peak) }} />
          ))}
        </div>
      )}
    </div>
  );
}
