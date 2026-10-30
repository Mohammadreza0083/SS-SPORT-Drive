"use client";

import { useEffect, useRef, type CSSProperties } from "react";

/** Props of {@link CountUp}. */
type CountUpProps = {
  /** The number to count up to. */
  value: number;
};

/**
 * A number that counts up from zero over 2.2 s once it scrolls into view. The counting is pure CSS
 * (an animated `--n` integer shown through a counter) started by adding `is-counting` to the element,
 * so it costs no re-renders; screen readers get the final value straight away. With reduced motion
 * the final value shows without counting.
 *
 * @param props - See {@link CountUpProps}.
 * @returns The counter.
 */
export function CountUp({ value }: CountUpProps) {
  const ref = useRef<HTMLSpanElement>(null);

  useEffect(() => {
    const element = ref.current;
    if (!element) {
      return;
    }
    if (typeof IntersectionObserver === "undefined") {
      element.classList.add("is-counting");
      return;
    }
    const observer = new IntersectionObserver((entries) => {
      if (entries.some((entry) => entry.isIntersecting)) {
        element.classList.add("is-counting");
        observer.disconnect();
      }
    });
    observer.observe(element);
    return () => observer.disconnect();
  }, []);

  return (
    <>
      <span ref={ref} aria-hidden="true" className="ss-count" style={{ "--n": value } as CSSProperties} />
      <span className="ss-sr-only">{value}</span>
    </>
  );
}
