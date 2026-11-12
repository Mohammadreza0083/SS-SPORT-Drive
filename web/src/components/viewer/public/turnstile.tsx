"use client";

import { useEffect, useRef } from "react";

/** Cloudflare's widget script, loaded for explicit rendering so the widget can live inside the dialog. */
export const TURNSTILE_SCRIPT = "https://challenges.cloudflare.com/turnstile/v0/api.js?render=explicit";

/** The part of Cloudflare's `window.turnstile` this site uses. */
type TurnstileApi = {
  render(container: HTMLElement, options: Record<string, unknown>): string;
  remove(widgetId: string): void;
};

/**
 * Reads Cloudflare's global API, if its script has run.
 *
 * @returns The API, or undefined before the script is ready.
 */
function turnstileApi(): TurnstileApi | undefined {
  return (window as unknown as { turnstile?: TurnstileApi }).turnstile;
}

/**
 * Runs a function once Cloudflare's script is ready, loading it the first time it is needed.
 *
 * @param ready - Called with the API; never called after the returned cancel function ran.
 * @returns A function that cancels the wait.
 */
function whenReady(ready: (api: TurnstileApi) => void): () => void {
  const now = turnstileApi();
  if (now) {
    ready(now);
    return () => {};
  }

  let cancelled = false;
  const onLoad = () => {
    const api = turnstileApi();
    if (!cancelled && api) {
      ready(api);
    }
  };
  let script = document.head.querySelector<HTMLScriptElement>(`script[src="${TURNSTILE_SCRIPT}"]`);
  if (!script) {
    script = document.createElement("script");
    script.src = TURNSTILE_SCRIPT;
    script.async = true;
    script.defer = true;
    document.head.appendChild(script);
  }

  script.addEventListener("load", onLoad);
  return () => {
    cancelled = true;
    script.removeEventListener("load", onLoad);
  };
}

/** Props of {@link TurnstileWidget}. */
type TurnstileWidgetProps = {
  /** The public site key (`NEXT_PUBLIC_TURNSTILE_SITE_KEY`); without one nothing is rendered or loaded. */
  siteKey: string | undefined;
  /** Called with the token when the check passes, and with null when the token expires or the check fails. */
  onToken: (token: string | null) => void;
};

/**
 * The Cloudflare Turnstile widget of the request-access dialog (backlog P-8). It renders into its own box, hands the
 * token to the dialog, clears it when it expires, and is removed with the dialog.
 *
 * @param props - See {@link TurnstileWidgetProps}.
 * @returns The widget's box, or nothing without a site key.
 */
export function TurnstileWidget({ siteKey, onToken }: TurnstileWidgetProps) {
  const box = useRef<HTMLDivElement>(null);
  const tokenCallback = useRef(onToken);

  useEffect(() => {
    tokenCallback.current = onToken;
  }, [onToken]);

  useEffect(() => {
    if (!siteKey) {
      return;
    }

    let widgetId: string | undefined;
    let api: TurnstileApi | undefined;
    const cancel = whenReady((ready) => {
      if (!box.current) {
        return;
      }

      api = ready;
      widgetId = ready.render(box.current, {
        sitekey: siteKey,
        callback: (token: string) => tokenCallback.current(token),
        "expired-callback": () => tokenCallback.current(null),
        "error-callback": () => tokenCallback.current(null),
      });
    });

    return () => {
      cancel();
      if (api && widgetId !== undefined) {
        api.remove(widgetId);
      }
    };
  }, [siteKey]);

  if (!siteKey) {
    return null;
  }

  return <div ref={box} className="pm-turnstile" />;
}
