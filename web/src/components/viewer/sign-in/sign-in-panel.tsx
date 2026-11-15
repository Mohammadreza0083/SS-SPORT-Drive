"use client";

import { useState, type FormEvent } from "react";
import type { Audience } from "@/lib/auth/sign-in-server";
import { BrandLogo } from "../brand-logo";
import { createSignInClient, type SignInClient } from "./sign-in-client";
import { signInCopy } from "./copy";

/** Props of {@link SignInPanel}. */
type SignInPanelProps = {
  /** Who signs in. */
  audience: Audience;
  /** The one-time token of an emailed link; its page shows only a Continue button. */
  token?: string;
  /** The `?error=` value of the address; only `expired` and `unavailable` have a message. */
  error?: string;
  /**
   * Asks the server for a link; tests pass a stub. Pages leave it out: a server component cannot hand functions to a
   * client component, so the panel builds the real client in the browser.
   */
  client?: SignInClient;
};

/** A simple email shape check; the server validates for real. */
const EMAIL_SHAPE = /.+@.+\..+/;

/**
 * The body of `/sign-in` and `/admin/sign-in` (backlog W-1): an email box that asks for a magic link, the "check your
 * email" step, and, when opened from the emailed link, a Continue button. The button posts the token, so a mail
 * scanner that merely opens the link cannot spend it.
 *
 * @param props - See {@link SignInPanelProps}.
 * @returns The panel, in the gate's look.
 */
export function SignInPanel({ audience, token, error, client }: SignInPanelProps) {
  const [email, setEmail] = useState("");
  const [sentTo, setSentTo] = useState<string | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [linkClient] = useState(() => client ?? createSignInClient(audience));
  const title = audience === "admin" ? signInCopy.adminTitle : signInCopy.viewerTitle;
  const pageError = error ? signInCopy.errors[error] : undefined;

  /**
   * Asks for the link; a malformed email never reaches the server.
   *
   * @param event - The form submit.
   */
  async function send(event: FormEvent) {
    event.preventDefault();
    const address = email.trim();
    setMessage(null);
    if (!EMAIL_SHAPE.test(address)) {
      setMessage(signInCopy.invalidEmail);
      return;
    }

    setBusy(true);
    const result = await linkClient.requestLink(address);
    setBusy(false);
    switch (result.kind) {
      case "sent":
        setSentTo(address);
        break;
      case "too-many":
        setMessage(signInCopy.tooMany);
        break;
      case "invalid":
        setMessage(signInCopy.invalidEmail);
        break;
      default:
        setMessage(signInCopy.failed);
    }
  }

  return (
    <main className="gate">
      <div className="gate-column">
        <header className="gate-top">
          <BrandLogo height={30} priority />
        </header>
        <div className="gate-body">
          {pageError && (
            <div role="alert" className="gate-error">
              {pageError}
            </div>
          )}

          {token ? (
            <form className="gate-step" method="post" action={`/api/auth/${audience}/session`}>
              <h1 className="gate-title">{title}</h1>
              <p className="gate-intro">{signInCopy.continueIntro}</p>
              <input type="hidden" name="token" value={token} />
              <button type="submit" className="gate-button">
                {signInCopy.continue}
              </button>
            </form>
          ) : sentTo ? (
            <div className="gate-step">
              <h1 className="gate-title">{signInCopy.sentTitle}</h1>
              <p className="gate-intro">
                {signInCopy.sentBefore}
                <b>{sentTo}</b>
                {signInCopy.sentAfter}
              </p>
              <p className="gate-note">{signInCopy.sentValidity}</p>
            </div>
          ) : (
            <form className="gate-step" onSubmit={send} noValidate>
              <h1 className="gate-title">{title}</h1>
              <p className="gate-intro">{audience === "admin" ? signInCopy.adminIntro : signInCopy.intro}</p>
              <label htmlFor="sign-in-email" className="gate-label">
                {signInCopy.emailLabel}
              </label>
              <input
                id="sign-in-email"
                className="gate-input"
                type="email"
                autoComplete="email"
                inputMode="email"
                required
                value={email}
                placeholder={signInCopy.emailPlaceholder}
                onChange={(event) => setEmail(event.target.value)}
                aria-invalid={message ? true : undefined}
              />
              {message && (
                <div role="alert" className="gate-error">
                  {message}
                </div>
              )}
              <button type="submit" className="gate-button" disabled={busy}>
                {busy ? signInCopy.sending : signInCopy.send}
              </button>
            </form>
          )}
        </div>
      </div>
    </main>
  );
}
