import type { HTMLAttributes, ReactNode } from "react";

/** Props of {@link Eyebrow}. */
type EyebrowProps = HTMLAttributes<HTMLDivElement> & {
  /** The label text. */
  children: ReactNode;
};

/**
 * The small red mono label above headings, e.g. "SS-SPORT DRIVE · PLAYER SEARCH".
 *
 * @param props - See {@link EyebrowProps}; other div attributes pass through.
 * @returns The label.
 */
export function Eyebrow({ className, children, ...rest }: EyebrowProps) {
  return (
    <div className={["ss-eyebrow", className].filter(Boolean).join(" ")} {...rest}>
      {children}
    </div>
  );
}
