"use client";

import Link from "next/link";
import { useEffect, useId, useRef, useState } from "react";
import { codeDigits, CODE_LENGTH, formatCountdown } from "@/lib/viewer/gate";
import { accessCopy } from "./copy";
import { TurnstileWidget } from "./turnstile";
import type { PublicViewer, RequestAccessClient } from "./types";

/** Which step of the dialog is showing. */
type Step = "form" | "code" | "sent";

/** Props of {@link RequestAccessModal}. */
type RequestAccessModalProps = {
  /** The player the visitor wants to see. */
  player: { slug: string; name: string; position: string };
  /** The signed-in viewer (P-7), or null for a visitor who must verify an email. */
  viewer: PublicViewer;
  /** The browser side of the flow. */
  client: RequestAccessClient;
  /** Called when the dialog should close. */
  onClose: () => void;
  /** Returns the Cloudflare Turnstile token of the page, or null while the widget is not wired in. */
  getTurnstileToken?: () => string | null;
};

/** The public Turnstile site key; without one (local preview, tests) the canvas's static "human check" row is shown instead. */
const TURNSTILE_SITE_KEY = process.env.NEXT_PUBLIC_TURNSTILE_SITE_KEY || undefined;

/** A simple email shape check; the server validates for real. */
const EMAIL_SHAPE = /.+@.+\..+/;

/**
 * The 3-step request-access dialog of the canvas's `Locked` artboard: email and description, then the 6-digit code,
 * then "Request sent". A signed-in viewer (P-7) sees only the description and sends in one click. All checks here are
 * convenience; the server validates everything again.
 *
 * @param props - See {@link RequestAccessModalProps}.
 * @returns The dialog and its backdrop.
 */
export function RequestAccessModal({ player, viewer, client, onClose, getTurnstileToken = () => null }: RequestAccessModalProps) {
  const titleId = useId();
  const [step, setStep] = useState<Step>("form");
  const [typedEmail, setTypedEmail] = useState("");
  const [about, setAbout] = useState("");
  const [code, setCode] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [captcha, setCaptcha] = useState<string | null>(null);
  const [captchaRun, setCaptchaRun] = useState(0);
  const [resendAt, setResendAt] = useState(0);
  const [now, setNow] = useState(() => Date.now());
  const email = viewer ? viewer.email : typedEmail.trim();
  const secondsLeft = Math.max(0, Math.ceil((resendAt - now) / 1000));
  const closeRef = useRef<HTMLButtonElement>(null);

  useEffect(() => {
    closeRef.current?.focus();
  }, []);

  useEffect(() => {
    if (step !== "code" || secondsLeft <= 0) {
      return;
    }

    const timer = setInterval(() => setNow(Date.now()), 1000);
    return () => clearInterval(timer);
  }, [step, secondsLeft]);

  /**
   * Checks the form and either asks for a code (signed out) or sends the request (signed in).
   *
   * @returns Nothing; the step changes when the server agrees.
   */
  async function sendForm() {
    const text = about.trim();
    if ((!viewer && !EMAIL_SHAPE.test(email)) || text.length === 0 || text.length > accessCopy.maxAbout) {
      setError(accessCopy.formError);
      return;
    }

    setError(null);
    setBusy(true);
    try {
      if (viewer) {
        const result = await client.submit({ playerSlug: player.slug, about: text, turnstileToken: getTurnstileToken() });
        if (result.kind === "sent") {
          setStep("sent");
        } else {
          setError(result.message ?? accessCopy.genericError);
        }
        return;
      }

      const result = await client.requestCode(email);
      if (result.kind === "sent") {
        const started = Date.now();
        setCode("");
        setNow(started);
        setResendAt(started + result.resendAfterSeconds * 1000);
        setStep("code");
      } else {
        setError(result.message ?? accessCopy.genericError);
      }
    } finally {
      setBusy(false);
    }
  }

  /**
   * Sends the request together with the typed code.
   *
   * @returns Nothing; the dialog moves to "Request sent" when the code is right.
   */
  async function verify() {
    if (code.length !== CODE_LENGTH) {
      setError(accessCopy.codeError);
      return;
    }

    setError(null);
    setBusy(true);
    try {
      const result = await client.submit({ playerSlug: player.slug, about: about.trim(), email, code, turnstileToken: captcha ?? getTurnstileToken() });
      if (result.kind === "sent") {
        setStep("sent");
      } else {
        setError(result.message ?? accessCopy.genericError);
        // A Turnstile token works once; ask the widget for a new one before the next try.
        setCaptcha(null);
        setCaptchaRun((run) => run + 1);
      }
    } finally {
      setBusy(false);
    }
  }

  /**
   * Asks for a fresh code for the same email.
   *
   * @returns Nothing; the resend countdown restarts.
   */
  async function resend() {
    setBusy(true);
    try {
      const result = await client.requestCode(email);
      if (result.kind === "sent") {
        const started = Date.now();
        setNow(started);
        setResendAt(started + result.resendAfterSeconds * 1000);
        setError(null);
      } else {
        setError(result.message ?? accessCopy.genericError);
      }
    } finally {
      setBusy(false);
    }
  }

  const reached = step === "form" ? 0 : step === "code" ? 1 : 2;

  return (
    <>
      <div className="pm-dim" onClick={onClose} aria-hidden="true" />
      <div className="pm-wrap">
        <div className="pm-modal" role="dialog" aria-modal="true" aria-labelledby={titleId}>
          <button ref={closeRef} type="button" className="pm-close" onClick={onClose} aria-label={accessCopy.close}>
            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" aria-hidden="true">
              <path d="M6 6l12 12M18 6L6 18" />
            </svg>
          </button>
          <div className="pm-steps" aria-hidden="true">
            {[0, 1, 2].map((index) => (
              <span key={index} className={index <= reached ? "is-on" : undefined} />
            ))}
          </div>

          {step === "form" ? (
            <div className="pm-step">
              <div className="pm-eyebrow">{`${player.name.toUpperCase()} · ${player.position}`}</div>
              <h2 id={titleId}>{accessCopy.title}</h2>
              <p className="pm-lead">{accessCopy.lead}</p>
              {viewer ? (
                <div className="pm-signed">
                  <span className="pm-tick" aria-hidden="true" />
                  <span>
                    {accessCopy.signedInAs} <b>{viewer.email}</b> {accessCopy.signedInNote}
                  </span>
                </div>
              ) : (
                <div className="pm-field">
                  <label htmlFor="pm-email">{accessCopy.emailLabel}</label>
                  <input
                    id="pm-email"
                    className="pm-input"
                    type="email"
                    value={typedEmail}
                    onChange={(event) => setTypedEmail(event.target.value)}
                    placeholder={accessCopy.emailPlaceholder}
                    autoComplete="email"
                  />
                  <p className="pm-hint">
                    {accessCopy.skipCode} <a href="/sign-in">{accessCopy.skipCodeLink}</a> {accessCopy.skipCodeEnd}
                  </p>
                </div>
              )}
              <div className="pm-field">
                <label htmlFor="pm-about">{accessCopy.aboutLabel}</label>
                <textarea
                  id="pm-about"
                  className="pm-input"
                  rows={4}
                  value={about}
                  onChange={(event) => setAbout(event.target.value)}
                  placeholder={accessCopy.aboutPlaceholder}
                />
              </div>
              {error ? (
                <div className="pm-error" role="alert">
                  {error}
                </div>
              ) : null}
              {TURNSTILE_SITE_KEY ? null : (
                <div className="pm-human">
                  <span className="pm-tick" aria-hidden="true" />
                  {accessCopy.humanCheck}
                  <span>{accessCopy.humanCheckBy}</span>
                </div>
              )}
              <button type="button" className="ss-btn ss-btn-red pm-submit" disabled={busy} onClick={sendForm}>
                {viewer ? accessCopy.send : accessCopy.sendCode}
              </button>
              <p className="pm-fine">
                {accessCopy.privacy} <a href="#privacy">{accessCopy.privacyLink}</a>
              </p>
            </div>
          ) : null}

          {step === "code" ? (
            <div className="pm-step">
              <h2 id={titleId}>{accessCopy.inboxTitle}</h2>
              <p className="pm-lead">
                {accessCopy.codeSentTo} <b>{email}</b>.
              </p>
              <div className="pm-field">
                <label htmlFor="pm-code">{accessCopy.codeLabel}</label>
                <input
                  id="pm-code"
                  className={error ? "pm-input pm-code pm-shake" : "pm-input pm-code"}
                  inputMode="numeric"
                  autoComplete="one-time-code"
                  maxLength={CODE_LENGTH}
                  value={code}
                  onChange={(event) => {
                    setCode(codeDigits(event.target.value));
                    setError(null);
                  }}
                  placeholder={accessCopy.codePlaceholder}
                />
              </div>
              {error ? (
                <div className="pm-error" role="alert">
                  {error}
                </div>
              ) : null}
              <button type="button" className="ss-btn ss-btn-red pm-submit" disabled={busy} onClick={verify}>
                {accessCopy.verify}
              </button>
              <div className="pm-row">
                {secondsLeft > 0 ? (
                  <span>{accessCopy.resendIn(formatCountdown(secondsLeft))}</span>
                ) : (
                  <button type="button" onClick={resend} disabled={busy}>
                    {accessCopy.resend}
                  </button>
                )}
                <button type="button" onClick={() => { setError(null); setStep("form"); }}>
                  {accessCopy.wrongEmail}
                </button>
              </div>
            </div>
          ) : null}

          {step === "sent" ? (
            <div className="pm-step pm-sent">
              <div className="pm-pop" aria-hidden="true">
                <svg width="42" height="42" viewBox="0 0 48 48" fill="none" stroke="#fff" strokeWidth="5" strokeLinecap="round" strokeLinejoin="round">
                  <path d="M12 25l8 8 16-17" />
                </svg>
              </div>
              <h2 id={titleId}>{accessCopy.sentTitle}</h2>
              <p className="pm-lead">
                {accessCopy.sentTo} <b>{email}</b>.
              </p>
              <ul className="pm-checks">
                <li className="is-done">
                  <span className="pm-tick" aria-hidden="true" />
                  {accessCopy.emailVerified}
                </li>
                <li>
                  <span className="pm-dot" aria-hidden="true" />
                  {accessCopy.reviewing}
                </li>
                <li>
                  <span className="pm-dot" aria-hidden="true" />
                  {accessCopy.linkArrives}
                </li>
              </ul>
              <Link href="/" className="pm-back">
                {accessCopy.backToSearch}
              </Link>
            </div>
          ) : null}
          {viewer ? null : <TurnstileWidget key={captchaRun} siteKey={TURNSTILE_SITE_KEY} onToken={setCaptcha} />}
        </div>
      </div>
    </>
  );
}
