import type { ButtonHTMLAttributes, ReactNode } from "react";

/** Props of {@link Chip}. */
type ChipProps = Omit<ButtonHTMLAttributes<HTMLButtonElement>, "aria-pressed"> & {
  /** Whether the filter is selected. */
  pressed?: boolean;
  /** The smaller chip used above the video playlist. */
  small?: boolean;
  /** The label. */
  children: ReactNode;
};

/**
 * A filter chip (video tags, positions): a toggle button that turns red when selected.
 *
 * @param props - See {@link ChipProps}; other button attributes pass through.
 * @returns The chip.
 */
export function Chip({ pressed = false, small = false, className, children, ...rest }: ChipProps) {
  const classes = ["ss-chip", small && "ss-chip-sm", pressed && "is-on", className].filter(Boolean).join(" ");
  return (
    <button type="button" aria-pressed={pressed} className={classes} {...rest}>
      {children}
    </button>
  );
}
