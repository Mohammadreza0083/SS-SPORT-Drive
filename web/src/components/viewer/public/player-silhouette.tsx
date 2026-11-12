import { useId } from "react";

/** Props of {@link PlayerPhoto}. */
type PlayerPhotoProps = {
  /** The photo URL on this site's own origin; the canvas silhouette is drawn while it is missing. */
  src?: string;
  /** The CSS class of the image or the silhouette. */
  className: string;
};

/**
 * A player's photo, or the canvas silhouette while there is none.
 *
 * @param props - See {@link PlayerPhotoProps}.
 * @returns The image or the silhouette.
 */
export function PlayerPhoto({ src, className }: PlayerPhotoProps) {
  const gradientId = useId();
  if (src) {
    // A plain <img>: photos come through the same-origin photo route and are already sized by ingest.
    // eslint-disable-next-line @next/next/no-img-element
    return <img src={src} alt="" className={className} draggable={false} loading="lazy" />;
  }

  return (
    <svg className={className} viewBox="0 0 120 150" aria-hidden="true">
      <defs>
        <linearGradient id={gradientId} x1="0" y1="0" x2="0" y2="1">
          <stop offset="0" stopColor="#e6cdb9" />
          <stop offset="1" stopColor="#2a1a16" />
        </linearGradient>
      </defs>
      <g fill={`url(#${gradientId})`}>
        <ellipse cx="62" cy="34" rx="17" ry="20" />
        <path d="M54 52 h16 l2 10 C96 66 112 80 116 110 L120 150 H4 L8 110 C12 80 28 66 52 62 Z" />
      </g>
    </svg>
  );
}

/** The padlock icon used on locks and badges. */
export function LockIcon({ size = 14 }: { size?: number }) {
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.2" aria-hidden="true">
      <rect x="5" y="11" width="14" height="10" rx="2" />
      <path d="M8 11V8a4 4 0 0 1 8 0v3" />
    </svg>
  );
}
