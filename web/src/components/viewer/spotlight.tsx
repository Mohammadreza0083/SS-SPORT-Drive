"use client";

import type { HTMLAttributes, MouseEvent } from "react";

/** Props of {@link Spotlight}: any section attributes. */
type SpotlightProps = HTMLAttributes<HTMLElement>;

/**
 * A hero section with a soft red light that follows the cursor (desktop only; hidden on touch screens).
 * The position is written straight to CSS variables, so moving the mouse never re-renders React.
 *
 * @param props - See {@link SpotlightProps}.
 * @returns The section.
 */
export function Spotlight({ className, children, onMouseMove, ...rest }: SpotlightProps) {
  /**
   * Moves the light to the cursor.
   *
   * @param event - The mouse move.
   */
  const follow = (event: MouseEvent<HTMLElement>) => {
    const target = event.currentTarget;
    const box = target.getBoundingClientRect();
    target.style.setProperty("--mx", `${Math.round(event.clientX - box.left)}px`);
    target.style.setProperty("--my", `${Math.round(event.clientY - box.top)}px`);
    onMouseMove?.(event);
  };

  return (
    <section className={["ss-spotlight", className].filter(Boolean).join(" ")} onMouseMove={follow} {...rest}>
      {children}
    </section>
  );
}
