import Image from "next/image";
import { brandImages, viewerCopy } from "./copy";

/** Props of {@link BrandLogo}. */
type BrandLogoProps = {
  /** Rendered height in pixels; the width follows the logo's ratio. */
  height: number;
  /** Extra class names. */
  className?: string;
  /** Loads the logo eagerly (above the fold). */
  priority?: boolean;
  /** Alternative text; empty when a parent link already names it. */
  alt?: string;
};

/**
 * The SS-SPORT horizontal lockup for dark backgrounds.
 *
 * @param props - See {@link BrandLogoProps}.
 * @returns The logo image.
 */
export function BrandLogo({ height, className, priority, alt = viewerCopy.brand }: BrandLogoProps) {
  const { src, width: w, height: h } = brandImages.logo;
  return (
    <Image
      src={src}
      alt={alt}
      height={height}
      width={Math.round((w / h) * height)}
      className={className}
      priority={priority}
      unoptimized
    />
  );
}
