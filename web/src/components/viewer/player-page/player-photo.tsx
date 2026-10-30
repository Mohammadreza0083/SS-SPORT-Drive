import { useId } from "react";

/** Props of {@link PlayerPhoto}. */
type PlayerPhotoProps = {
  /** The cut-out photo (transparent background); the canvas silhouette is shown while it is missing. */
  src?: string;
  /** The player's name, for the image's alternative text. */
  name: string;
};

/**
 * The hero photo, rising in from a blur on page load (the one blur the motion rules allow).
 *
 * @param props - See {@link PlayerPhotoProps}.
 * @returns The photo, or the canvas silhouette.
 */
export function PlayerPhoto({ src, name }: PlayerPhotoProps) {
  const gradientId = useId();

  if (src) {
    // A plain <img>: cut-out photos come from object storage and are already sized by ingest.
    // eslint-disable-next-line @next/next/no-img-element
    return <img src={src} alt={name} className="pp-photo ss-sil" />;
  }

  return (
    <svg className="pp-photo ss-sil" viewBox="0 0 120 150" aria-hidden="true">
      <defs>
        <linearGradient id={gradientId} x1="0" y1="0" x2="0" y2="1">
          <stop offset="0" stopColor="#ead2bf" />
          <stop offset="1" stopColor="#3b2620" />
        </linearGradient>
      </defs>
      <g fill={`url(#${gradientId})`}>
        <ellipse cx="62" cy="34" rx="17" ry="20" />
        <path d="M54 52 h16 l2 10 C96 66 112 80 116 110 L120 150 H4 L8 110 C12 80 28 66 52 62 Z" />
      </g>
    </svg>
  );
}
