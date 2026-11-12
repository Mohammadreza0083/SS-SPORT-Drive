"use client";

import { useEffect, useId, useState, type FormEvent } from "react";
import { BrandLogo } from "../brand-logo";
import { brandImages, viewerCopy } from "../copy";
import { CODE_LENGTH, codeDigits, formatCountdown, openWithDevice, requestCode, verifyCode, type GateLink } from "@/lib/viewer/gate";
import { gateCopy } from "./copy";

/** Props of {@link EmailGate}. */
type EmailGateProps = {
  /** The link, as `GET /links/{code}` answered it. */
  link: GateLink;
  /** Whether the viewer already has a session for this link: the gate opens on "You're in". */
  signedIn?: boolean;
};

/** The gate's steps, as in the canvas. */
type Step = "email" | "code" | "done" | "pending";

/**
 * Picks the message for a refusal: the API's own text, a countdown for rate limits, or a generic line.
 *
 * @param message - The API's message, if any.
 * @param retryAfterSeconds - The wait of a 429, if any.
 * @returns The text.
 */
function refusalText(message: string | null, retryAfterSeconds: number | null = null): string {
  if (message) {
    return message;
  }

  return retryAfterSeconds ? gateCopy.tooMany(retryAfterSeconds) : gateCopy.genericError;
}

/**
 * The email gate of a personal link (backlog L-2, canvas `Gate`): the player card, then "Confirm it's you" (email),
 * "Check your inbox" (6-digit code, resend countdown, wrong email) and "You're in". A device verified before opens
 * without the gate. The tokens never reach this component: the site's routes keep them in `HttpOnly` cookies.
 *
 * @param props - See {@link EmailGateProps}.
 * @returns The gate.
 */
export function EmailGate({ link, signedIn = false }: EmailGateProps) {
  const silhouetteId = useId();
  const [step, setStep] = useState<Step>(signedIn ? "done" : "email");
  const [email, setEmail] = useState("");
  const [code, setCode] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [incomplete, setIncomplete] = useState(false);
  const [shakes, setShakes] = useState(0);
  const [busy, setBusy] = useState(false);
  const [resendAt, setResendAt] = useState(0);
  // Set when a code is sent; reading the clock during render would break prerendering.
  const [now, setNow] = useState(0);

  // A device verified before (or a link that skips the gate) opens at once.
  useEffect(() => {
    if (signedIn) {
      return;
    }

    let active = true;
    void openWithDevice(link.code).then((opening) => {
      if (active && opening !== "gate") {
        setStep(opening === "opened" ? "done" : "pending");
      }
    });
    return () => {
      active = false;
    };
  }, [link.code, signedIn]);

  // The resend countdown ticks while the code step is shown.
  useEffect(() => {
    if (step !== "code") {
      return;
    }

    const timer = window.setInterval(() => setNow(Date.now()), 1000);
    return () => window.clearInterval(timer);
  }, [step]);

  /**
   * Asks for a code, then shows the code step with its countdown.
   *
   * @param event - The form submit, if any.
   */
  async function sendCode(event?: FormEvent) {
    event?.preventDefault();
    if (email.trim().length === 0) {
      setError(gateCopy.emailMissing);
      return;
    }

    setBusy(true);
    setError(null);
    const result = await requestCode(link.code, email);
    setBusy(false);
    if (result.kind === "refused") {
      setError(refusalText(result.message, result.retryAfterSeconds));
      return;
    }

    const sentAt = Date.now();
    setNow(sentAt);
    setResendAt(sentAt + result.resendAfterSeconds * 1000);
    setCode("");
    setIncomplete(false);
    setStep("code");
  }

  /**
   * Checks the typed code; a short code shakes without calling the API.
   *
   * @param event - The form submit.
   */
  async function verify(event: FormEvent) {
    event.preventDefault();
    setError(null);
    if (code.length !== CODE_LENGTH) {
      setIncomplete(true);
      setShakes((count) => count + 1);
      return;
    }

    setBusy(true);
    const result = await verifyCode(link.code, email, code);
    setBusy(false);
    if (result.kind !== "refused") {
      setStep(result.kind === "verified" ? "done" : "pending");
      return;
    }

    setShakes((count) => count + 1);
    setError(refusalText(result.message));
  }

  const secondsLeft = Math.ceil((resendAt - now) / 1000);
  const shaking = (incomplete || error) && shakes > 0;
  const firstName = link.player?.name.split(" ")[0];
  const lastName = link.player?.name.split(" ").slice(1).join(" ");

  return (
    <main className="gate">
      <div aria-hidden="true" className="gate-glow ss-glow" />
      <div className="gate-column">
        <header className="gate-top">
          <BrandLogo height={30} priority />
        </header>

        <section className="gate-card" aria-label={link.player?.name ?? gateCopy.players(link.playerCount)}>
          <div className="gate-card-bar" />
          {/* A plain <img>: the mark is decorative and scaled by CSS. */}
          {/* eslint-disable-next-line @next/next/no-img-element */}
          <img src={brandImages.mark.src} alt="" aria-hidden="true" className="gate-card-mark" />
          <svg className="gate-card-figure" viewBox="0 0 120 150" aria-hidden="true">
            <defs>
              <linearGradient id={silhouetteId} x1="0" y1="0" x2="0" y2="1">
                <stop offset="0" stopColor="#ead2bf" />
                <stop offset="1" stopColor="#3b2620" />
              </linearGradient>
            </defs>
            <g fill={`url(#${silhouetteId})`}>
              <ellipse cx="62" cy="34" rx="17" ry="20" />
              <path d="M54 52 h16 l2 10 C96 66 112 80 116 110 L120 150 H4 L8 110 C12 80 28 66 52 62 Z" />
            </g>
          </svg>
          <div className="gate-card-text">
            {link.player ? (
              <>
                <div className="gate-eyebrow">{`${link.player.position} · ${link.player.nationality.name.toUpperCase()}`}</div>
                <div className="gate-name">
                  <span>{firstName}</span>
                  <br />
                  <span>{lastName}</span>
                </div>
              </>
            ) : (
              <div className="gate-name">{gateCopy.players(link.playerCount)}</div>
            )}
          </div>
        </section>

        <div className="gate-body">
          {step === "email" && (
            <form className="gate-step" onSubmit={sendCode} noValidate>
              <h1 className="gate-title">{gateCopy.emailTitle}</h1>
              <p className="gate-intro">
                {gateCopy.emailIntroBefore}
                <b>{link.recipientHint}</b>
                {gateCopy.emailIntroAfter}
              </p>
              <label htmlFor="gate-email" className="gate-label">
                {gateCopy.emailLabel}
              </label>
              <input
                id="gate-email"
                className="gate-input"
                type="email"
                autoComplete="email"
                inputMode="email"
                required
                value={email}
                placeholder={gateCopy.emailPlaceholder}
                onChange={(event) => setEmail(event.target.value)}
                aria-invalid={error ? true : undefined}
              />
              {error && (
                <div role="alert" className="gate-error">
                  {error}
                </div>
              )}
              <button type="submit" className="gate-button" disabled={busy}>
                {busy ? gateCopy.sending : gateCopy.sendCode}
              </button>
              <p className="gate-note">
                {gateCopy.privacyNote}
                <a href="#privacy">{viewerCopy.privacy}</a>
              </p>
            </form>
          )}

          {step === "code" && (
            <form className="gate-step" onSubmit={verify} noValidate>
              <h1 className="gate-title">{gateCopy.codeTitle}</h1>
              <p className="gate-intro">
                {gateCopy.codeIntroBefore}
                <b>{email.trim()}</b>
                {gateCopy.codeIntroAfter}
              </p>
              <label htmlFor="gate-code" className="gate-label">
                {gateCopy.codeLabel}
              </label>
              <div key={shakes} className={shaking ? "gate-shake" : undefined}>
                <input
                  id="gate-code"
                  className="gate-input gate-code"
                  inputMode="numeric"
                  autoComplete="one-time-code"
                  maxLength={CODE_LENGTH + 4}
                  value={code}
                  placeholder={gateCopy.codePlaceholder}
                  onChange={(event) => {
                    setCode(codeDigits(event.target.value));
                    setIncomplete(false);
                  }}
                  aria-invalid={incomplete || error ? true : undefined}
                />
              </div>
              {incomplete && <div className="gate-error">{gateCopy.codeIncomplete}</div>}
              {error && (
                <div role="alert" className="gate-error">
                  {error}
                </div>
              )}
              <button type="submit" className="gate-button" disabled={busy}>
                {busy ? gateCopy.checking : gateCopy.watch}
              </button>
              <div className="gate-row">
                {secondsLeft > 0 ? (
                  <span>{gateCopy.resendIn(formatCountdown(secondsLeft))}</span>
                ) : (
                  <button type="button" className="gate-link" onClick={() => void sendCode()} disabled={busy}>
                    {gateCopy.resend}
                  </button>
                )}
                <button
                  type="button"
                  className="gate-link"
                  onClick={() => {
                    setError(null);
                    setStep("email");
                  }}
                >
                  {gateCopy.wrongEmail}
                </button>
              </div>
            </form>
          )}

          {step === "done" && (
            <div className="gate-done" role="status">
              <div className="gate-check">
                <span className="gate-burst" />
                <svg
                  width="44"
                  height="44"
                  viewBox="0 0 48 48"
                  fill="none"
                  stroke="#fff"
                  strokeWidth="5"
                  strokeLinecap="round"
                  strokeLinejoin="round"
                  aria-hidden="true"
                >
                  <path className="gate-check-path" d="M12 25l8 8 16-17" />
                </svg>
              </div>
              <h1 className="gate-title gate-done-title">{gateCopy.doneTitle}</h1>
              <p className="gate-intro gate-done-line">{link.player ? gateCopy.opening(link.player.name) : gateCopy.openingRoster}</p>
              <div className="gate-loadbar">
                <div />
              </div>
              <p className="gate-note gate-done-note">{gateCopy.remembered}</p>
              <p className="gate-note gate-done-note-2">{gateCopy.overlayNote}</p>
            </div>
          )}

          {step === "pending" && (
            <div className="gate-step gate-pending" role="status">
              <h1 className="gate-title">{gateCopy.pendingTitle}</h1>
              <p className="gate-intro">{gateCopy.pendingText}</p>
              <p className="gate-note">{gateCopy.pendingNote}</p>
            </div>
          )}
        </div>
      </div>
    </main>
  );
}
