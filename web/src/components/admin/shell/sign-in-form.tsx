"use client";

import { useState, type FormEvent } from "react";
import { adminCopy } from "../copy";

/**
 * The email step of the admin sign-in (canvas `AdminLogin`): asks for a sign-in link, then says to check the inbox. The
 * answer is the same for every address, so nobody can learn who is an admin; only a rate limit shows.
 *
 * @returns The form, or the "check your email" step.
 */
export function SignInForm() {
  const [email, setEmail] = useState("");
  const [step, setStep] = useState<"email" | "sent">("email");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  /**
   * Sends the request.
   *
   * @param event - The form submit.
   */
  const send = async (event: FormEvent) => {
    event.preventDefault();
    setBusy(true);
    setError(null);
    try {
      const answer = await fetch("/api/admin/sign-in-link", { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ email: email.trim() }) });
      if (answer.status === 202) {
        setStep("sent");
      } else {
        setError(answer.status === 429 ? adminCopy.signIn.wait : adminCopy.signIn.failed);
      }
    } catch {
      setError(adminCopy.signIn.failed);
    } finally {
      setBusy(false);
    }
  };

  if (step === "sent") {
    return (
      <>
        <h1>{adminCopy.signIn.sentTitle}</h1>
        <p className="adm-lead">{adminCopy.signIn.sent}</p>
        <button type="button" className="adm-btn adm-btn-wide" onClick={() => setStep("email")}>
          {adminCopy.signIn.back}
        </button>
      </>
    );
  }

  return (
    <form onSubmit={(event) => void send(event)}>
      <h1>{adminCopy.signIn.title}</h1>
      <p className="adm-lead">{adminCopy.signIn.lead}</p>
      <label htmlFor="admin-email">{adminCopy.signIn.email}</label>
      <input id="admin-email" type="email" required autoComplete="email" className="adm-input" value={email} onChange={(event) => setEmail(event.target.value)} />
      {error ? (
        <p role="alert" className="adm-error">
          {error}
        </p>
      ) : null}
      <button type="submit" className="adm-btn adm-btn-red adm-btn-wide" disabled={busy || email.trim() === ""}>
        {busy ? adminCopy.signIn.sending : adminCopy.signIn.send}
      </button>
    </form>
  );
}
