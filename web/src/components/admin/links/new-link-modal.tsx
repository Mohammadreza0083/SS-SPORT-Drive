"use client";

import { useEffect, useRef, useState } from "react";
import { LINK_EXPIRY_CHOICES } from "@/lib/admin/settings-form";
import { adminCopy } from "../copy";

/** The body of `POST /admin/links` the modal builds. */
export type NewLinkBody = {
  recipient: { name?: string; organisation?: string; emails: string[] };
  playerIds: string[];
  expiresInDays: number;
  note?: string;
};

/** The link the API created, as the modal shows it. */
export type CreatedLink = { code: string; url: string };

/** Props of {@link NewLinkModal}. */
type NewLinkModalProps = {
  /** The player the link is for. */
  player: { id: string; name: string };
  /** The expiry the settings preselect (7, 30 or 90 days). */
  defaultExpiryDays: number;
  /** Creates the link. */
  onCreate: (body: NewLinkBody) => Promise<CreatedLink>;
  /** Closes the modal. */
  onClose: () => void;
};

/** Elements that can take focus inside the dialog. */
const FOCUSABLE = 'button:not([disabled]), input, textarea, [href], [tabindex]:not([tabindex="-1"])';

/** An ordinary email address. */
const EMAIL = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

/**
 * The New share link modal (A-6, canvas `AdminPlayer`): recipient email, expiry and a personal note, then "Create &
 * email". There is no download switch (nothing is ever downloadable, decisions §6) and no "Never" expiry (every link
 * expires, decisions §1b). It closes on Escape, keeps Tab inside and locks the page's scroll while open.
 *
 * @param props - See {@link NewLinkModalProps}.
 * @returns The modal.
 */
export function NewLinkModal({ player, defaultExpiryDays, onCreate, onClose }: NewLinkModalProps) {
  const [email, setEmail] = useState("");
  const [name, setName] = useState("");
  const [organisation, setOrganisation] = useState("");
  const [days, setDays] = useState<number>(defaultExpiryDays);
  const [note, setNote] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState(false);
  const [created, setCreated] = useState<CreatedLink | null>(null);
  const dialogRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    const previous = document.body.style.overflow;
    document.body.style.overflow = "hidden";
    dialogRef.current?.querySelector<HTMLElement>("input")?.focus();
    return () => {
      document.body.style.overflow = previous;
    };
  }, []);

  useEffect(() => {
    /**
     * Escape closes the modal wherever focus is.
     *
     * @param event - The key press.
     */
    const onEscape = (event: KeyboardEvent) => {
      if (event.key === "Escape") {
        event.preventDefault();
        onClose();
      }
    };
    window.addEventListener("keydown", onEscape);
    return () => window.removeEventListener("keydown", onEscape);
  }, [onClose]);

  const address = email.trim().toLowerCase();
  const valid = EMAIL.test(address);

  /** Creates the link. */
  const create = async () => {
    setBusy(true);
    setError(false);
    try {
      setCreated(
        await onCreate({
          recipient: { name: name.trim() || undefined, organisation: organisation.trim() || undefined, emails: [address] },
          playerIds: [player.id],
          expiresInDays: days,
          note: note.trim() || undefined,
        }),
      );
    } catch {
      setError(true);
    } finally {
      setBusy(false);
    }
  };

  /**
   * Keeps Tab inside the dialog.
   *
   * @param event - The key press.
   */
  const trap = (event: React.KeyboardEvent<HTMLDivElement>) => {
    if (event.key !== "Tab" || !dialogRef.current) {
      return;
    }

    const items = [...dialogRef.current.querySelectorAll<HTMLElement>(FOCUSABLE)];
    const first = items[0];
    const last = items[items.length - 1];
    if (event.shiftKey && document.activeElement === first) {
      event.preventDefault();
      last?.focus();
    } else if (!event.shiftKey && document.activeElement === last) {
      event.preventDefault();
      first?.focus();
    }
  };

  return (
    <div className="adm-modal-layer">
      <div className="adm-scrim" onClick={onClose} aria-hidden="true" />
      <div ref={dialogRef} role="dialog" aria-modal="true" aria-labelledby="nl-title" className="adm-modal" onKeyDown={trap}>
        {created ? (
          <div className="adm-done">
            <div className="adm-done-mark" aria-hidden="true">
              ✓
            </div>
            <h2 id="nl-title">{adminCopy.linkModal.doneTitle}</h2>
            <p>
              <span className="adm-mono">{created.url.replace(/^https?:\/\//, "")}</span>
              {adminCopy.linkModal.queued(address)}
            </p>
            <button type="button" className="adm-btn adm-btn-red" onClick={onClose}>
              {adminCopy.linkModal.done}
            </button>
          </div>
        ) : (
          <div className="adm-modal-body">
            <h2 id="nl-title">{adminCopy.linkModal.title(player.name)}</h2>
            <label htmlFor="nl-email">{adminCopy.linkModal.email}</label>
            <input id="nl-email" type="email" className="adm-input" value={email} onChange={(event) => setEmail(event.target.value)} autoComplete="off" />
            <div className="adm-row2">
              <div>
                <label htmlFor="nl-name">{adminCopy.linkModal.name}</label>
                <input id="nl-name" className="adm-input" value={name} onChange={(event) => setName(event.target.value)} maxLength={120} />
              </div>
              <div>
                <label htmlFor="nl-org">{adminCopy.linkModal.organisation}</label>
                <input id="nl-org" className="adm-input" value={organisation} onChange={(event) => setOrganisation(event.target.value)} maxLength={120} />
              </div>
            </div>
            <div className="adm-label">{adminCopy.linkModal.expires}</div>
            <div className="adm-chips">
              {LINK_EXPIRY_CHOICES.map((choice) => (
                <button key={choice} type="button" className="adm-chip" aria-pressed={days === choice} onClick={() => setDays(choice)}>
                  {adminCopy.linkModal.days(choice)}
                </button>
              ))}
            </div>
            <label htmlFor="nl-note">{adminCopy.linkModal.note}</label>
            <textarea id="nl-note" className="adm-input" rows={3} maxLength={500} value={note} onChange={(event) => setNote(event.target.value)} />
            <p className="adm-hint">{adminCopy.linkModal.notice}</p>
            {error ? (
              <p role="alert" className="adm-error">
                {adminCopy.linkModal.failed}
              </p>
            ) : null}
            <div className="adm-actions">
              <button type="button" className="adm-btn" onClick={onClose}>
                {adminCopy.linkModal.cancel}
              </button>
              <button type="button" className="adm-btn adm-btn-red" disabled={!valid || busy} onClick={() => void create()}>
                {adminCopy.linkModal.create}
              </button>
            </div>
          </div>
        )}
      </div>
    </div>
  );
}
