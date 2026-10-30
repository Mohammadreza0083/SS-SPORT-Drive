import type { CSSProperties } from "react";
import { brandImages } from "./copy";

/** Props of {@link Glow}. */
type GlowProps = {
  /** Overrides the position and size; by default it sits top right as in the canvas hero of each breakpoint. */
  style?: CSSProperties;
};

/**
 * The breathing red radial glow behind heroes.
 *
 * @param props - See {@link GlowProps}.
 * @returns A decorative element hidden from assistive technology.
 */
export function Glow({ style }: GlowProps) {
  return <div aria-hidden="true" className="ss-glow-orb ss-glow" style={style} />;
}

/** Props of {@link DriftMark}. */
type DriftMarkProps = {
  /** Overrides the position, width and opacity; by default it follows the canvas hero of each breakpoint. */
  style?: CSSProperties;
};

/**
 * The SS-SPORT infinity mark drifting slowly behind the content.
 *
 * @param props - See {@link DriftMarkProps}.
 * @returns A decorative image hidden from assistive technology.
 */
export function DriftMark({ style }: DriftMarkProps) {
  return (
    // A plain <img>: the mark is decorative, scaled by CSS and needs no optimisation.
    // eslint-disable-next-line @next/next/no-img-element
    <img src={brandImages.mark.src} alt="" aria-hidden="true" className="ss-mark ss-drift" style={style} />
  );
}
