"use client";

import { useEffect, useRef } from "react";
import { WATERMARK, pickPosition } from "@/lib/viewer/watermark";

/** Props of {@link EmailOverlay}. */
type EmailOverlayProps = {
  /** The signed-in viewer's email (from the OTP-verified session, L-2). */
  email: string;
  /** Random numbers in [0, 1), for tests; `Math.random` by default. */
  random?: () => number;
  /** White text over video (default) or dark text over white document pages (V-6). */
  tone?: "light" | "dark";
};

/** The class that carries the overlay's look; tampering with it is undone. */
const CLASS_NAME = "ss-wm";

/**
 * The viewer's email laid faintly over the video (backlog V-4, decisions §6): 20 % opacity, 2.2 % of the
 * frame height, moving every 8 seconds with a 0.5 second cross-fade to a random spot clear of the burned-in
 * logo and slogan. It is the main deterrent against screen recording, so it is also hard to switch off:
 * removing it, hiding it or editing its text in the browser's developer tools is undone at once.
 *
 * The element's content and position are written directly to the DOM (not through React state), so the
 * guard can restore them without fighting React.
 *
 * @param props - See {@link EmailOverlayProps}.
 * @returns The overlay element, positioned inside the player frame.
 */
export function EmailOverlay({ email, random = Math.random, tone = "light" }: EmailOverlayProps) {
  const ref = useRef<HTMLSpanElement>(null);

  useEffect(() => {
    const node = ref.current;
    const frame = node?.parentElement;
    if (!node || !frame) {
      return;
    }

    let position = { x: 0, y: 0 };
    let fading = false;

    /** Writes the overlay's expected text, class, visibility and position onto the element. */
    const apply = () => {
      const base = tone === "dark" ? `${CLASS_NAME} ss-wm-dark` : CLASS_NAME;
      const className = fading ? `${base} is-fading` : base;
      if (node.className !== className) {
        node.className = className;
      }
      if (node.textContent !== email) {
        node.textContent = email;
      }
      if (node.hasAttribute("hidden")) {
        node.removeAttribute("hidden");
      }
      const style = `--wm-x: ${(position.x * 100).toFixed(2)}%; --wm-y: ${(position.y * 100).toFixed(2)}%;`;
      if (node.getAttribute("style") !== style) {
        node.setAttribute("style", style);
      }
      if (node.getAttribute("aria-hidden") !== "true") {
        node.setAttribute("aria-hidden", "true");
      }
      if (!node.isConnected || node.parentElement !== frame) {
        frame.appendChild(node);
      }
    };

    /** Picks a new spot for the label, measured against the frame. */
    const move = () => {
      const box = frame.getBoundingClientRect();
      const label = node.getBoundingClientRect();
      position = pickPosition(
        { w: box.width ? label.width / box.width : 0.3, h: box.height ? label.height / box.height : 0.04 },
        random,
      );
    };

    move();
    apply();

    let fadeTimer: ReturnType<typeof setTimeout> | undefined;
    const moveTimer = setInterval(() => {
      fading = true;
      apply();
      fadeTimer = setTimeout(() => {
        move();
        fading = false;
        apply();
      }, WATERMARK.fadeMs / 2);
    }, WATERMARK.intervalMs);

    // Undo removal, hiding or edits made outside this component.
    const guard = new MutationObserver(() => apply());
    guard.observe(frame, { childList: true });
    guard.observe(node, { attributes: true, childList: true, characterData: true, subtree: true });

    return () => {
      guard.disconnect();
      clearInterval(moveTimer);
      clearTimeout(fadeTimer);
    };
  }, [email, random, tone]);

  return <span ref={ref} className={tone === "dark" ? `${CLASS_NAME} ss-wm-dark` : CLASS_NAME} aria-hidden="true" />;
}
