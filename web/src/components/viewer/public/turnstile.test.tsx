import { afterEach, describe, expect, it, vi } from "vitest";
import { cleanup, render } from "@testing-library/react";
import { TURNSTILE_SCRIPT, TurnstileWidget } from "./turnstile";

/**
 * The Cloudflare Turnstile widget in the request-access dialog (P-8). It renders explicitly into its own box once
 * Cloudflare's script is ready, hands the token to the dialog, clears it when the token expires, and is removed with
 * the dialog. Without a site key (local development) nothing is rendered and no script is loaded.
 */

/** The stub `window.turnstile` API. */
type TurnstileStub = { render: ReturnType<typeof vi.fn>; remove: ReturnType<typeof vi.fn> };

afterEach(() => {
  cleanup();
  delete (window as unknown as { turnstile?: TurnstileStub }).turnstile;
  document.head.querySelectorAll("script").forEach((script) => script.remove());
});

/**
 * Installs a stub Turnstile API.
 *
 * @returns The stub.
 */
function installTurnstile(): TurnstileStub {
  const stub = { render: vi.fn().mockReturnValue("widget-1"), remove: vi.fn() };
  (window as unknown as { turnstile: TurnstileStub }).turnstile = stub;
  return stub;
}

describe("TurnstileWidget", () => {
  it("loads Cloudflare's script for explicit rendering", () => {
    expect(TURNSTILE_SCRIPT).toBe("https://challenges.cloudflare.com/turnstile/v0/api.js?render=explicit");
  });

  it("renders with the site key and passes the token on", () => {
    const turnstile = installTurnstile();
    const onToken = vi.fn();

    render(<TurnstileWidget siteKey="0x4AAAAAAAsite" onToken={onToken} />);

    expect(turnstile.render).toHaveBeenCalledTimes(1);
    const [container, options] = turnstile.render.mock.calls[0];
    expect(container).toBeInstanceOf(HTMLElement);
    expect(options.sitekey).toBe("0x4AAAAAAAsite");
    options.callback("token-1");
    expect(onToken).toHaveBeenLastCalledWith("token-1");
    options["expired-callback"]();
    expect(onToken).toHaveBeenLastCalledWith(null);
    options["error-callback"]();
    expect(onToken).toHaveBeenLastCalledWith(null);
  });

  it("removes the widget when the dialog closes", () => {
    const turnstile = installTurnstile();

    const { unmount } = render(<TurnstileWidget siteKey="0x4AAAAAAAsite" onToken={() => {}} />);
    unmount();

    expect(turnstile.remove).toHaveBeenCalledWith("widget-1");
  });

  it("renders nothing and loads no script without a site key", () => {
    const { container } = render(<TurnstileWidget siteKey={undefined} onToken={() => {}} />);

    expect(container.innerHTML).toBe("");
    expect(document.querySelector(`script[src="${TURNSTILE_SCRIPT}"]`)).toBeNull();
  });
});
