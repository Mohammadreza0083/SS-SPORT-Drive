"use client";

import { useRef, useState } from "react";
import { missingDetails, volleyboxId } from "@/lib/admin/waiting";
import { adminCopy } from "../copy";

/** The positions a player can have, in the canvas's order. */
const POSITIONS = ["S", "OH", "OPP", "MB", "L", "DS"] as const;

/** A waiting player as the drawer edits them. */
export type WaitingPlayer = {
  /** The player id. */
  id: string;
  /** The Drive folder the player came from. */
  folderName: string;
  /** The name so far; empty when missing. */
  name: string;
  /** The country code, or null. */
  nationalityCode: string | null;
  /** The position codes so far. */
  positions: string[];
  /** The Volleybox link so far; empty when missing. */
  volleyboxUrl: string;
  /** Whether a photo is stored. */
  hasPhoto: boolean;
};

/** What the drawer saves before it publishes. */
export type WaitingEdit = { name: string; nationalityCode: string | null; positions: string[]; volleyboxUrl: string };

/** Props of {@link CompleteDetails}. */
type CompleteDetailsProps = {
  /** The player to complete. */
  player: WaitingPlayer;
  /** The countries to choose from. */
  countries: { code: string; name: string }[];
  /** Saves the details. */
  onSave: (id: string, edit: WaitingEdit) => Promise<void>;
  /** Publishes the player. */
  onPublish: (id: string) => Promise<void>;
  /** Moves on to the next waiting player. */
  onNext: () => void;
  /** Uploads a photo; the drawer hides the button when this is left out. */
  onPhoto?: (id: string, file: File) => Promise<void>;
};

/**
 * The waiting list's drawer (A-5, canvas `Waiting`): the admin fills the missing details, a checklist ticks each required
 * field, and Publish stays disabled with "N fields left" until every field is valid. Then it saves and publishes, and
 * shows the done state with "Create share link" and "Next waiting player".
 *
 * @param props - See {@link CompleteDetailsProps}.
 * @returns The drawer.
 */
export function CompleteDetails({ player, countries, onSave, onPublish, onNext, onPhoto }: CompleteDetailsProps) {
  const [name, setName] = useState(player.name);
  const [nationality, setNationality] = useState(player.nationalityCode ?? "");
  const [positions, setPositions] = useState(player.positions);
  const [vb, setVb] = useState(player.volleyboxUrl);
  const [hasPhoto, setHasPhoto] = useState(player.hasPhoto);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [done, setDone] = useState(false);
  const fileRef = useRef<HTMLInputElement>(null);

  const missing = missingDetails({ name, nationalityCode: nationality || null, positions, volleyboxUrl: vb, hasPhoto });
  const ready = missing.length === 0;
  const id = volleyboxId(vb);

  /**
   * Picks or drops a position; at most two, the first is the primary one.
   *
   * @param code - The position code.
   */
  const toggle = (code: string) =>
    setPositions((current) => (current.includes(code) ? current.filter((item) => item !== code) : current.length < 2 ? [...current, code] : current));

  /** Saves the details, then publishes the player. */
  const publish = async () => {
    setBusy(true);
    setError(null);
    try {
      await onSave(player.id, { name: name.trim(), nationalityCode: nationality || null, positions, volleyboxUrl: vb.trim() });
      await onPublish(player.id);
      setDone(true);
    } catch (caught) {
      setError((caught as { code?: string }).code === "player.incomplete" ? adminCopy.details.incomplete : adminCopy.details.failed);
    } finally {
      setBusy(false);
    }
  };

  /**
   * Uploads the chosen photo.
   *
   * @param file - The image file.
   */
  const upload = async (file: File | undefined) => {
    if (!file || !onPhoto) {
      return;
    }

    setError(null);
    try {
      await onPhoto(player.id, file);
      setHasPhoto(true);
    } catch {
      setError(adminCopy.details.failed);
    }
  };

  if (done) {
    return (
      <section aria-label={adminCopy.details.region} className="adm-drawer">
        <div className="adm-done">
          <div className="adm-done-mark" aria-hidden="true">
            ✓
          </div>
          <div className="adm-done-name">{name.trim()}</div>
          <p>{adminCopy.details.doneText}</p>
          <div className="adm-actions">
            <a className="adm-btn adm-btn-red" href={`/admin/players/${player.id}?newLink=1`}>
              {adminCopy.details.createLink}
            </a>
            <button type="button" className="adm-btn" onClick={onNext}>
              {adminCopy.details.next}
            </button>
          </div>
        </div>
      </section>
    );
  }

  return (
    <section aria-label={adminCopy.details.region} className="adm-drawer">
      <div className="adm-drawer-body">
        <div className="adm-label">{player.folderName}</div>
        <h2>{adminCopy.details.title}</h2>
        <div className="adm-photo-row">
          <span className={hasPhoto ? "adm-photo adm-photo-ok" : "adm-photo"} aria-hidden="true" />
          <div>
            <button type="button" className="adm-btn adm-btn-small" onClick={() => fileRef.current?.click()}>
              {hasPhoto ? adminCopy.details.replacePhoto : adminCopy.details.uploadPhoto}
            </button>
            <input ref={fileRef} type="file" accept="image/*" hidden onChange={(event) => void upload(event.target.files?.[0])} />
            <span className="adm-hint">{adminCopy.details.photoHint}</span>
          </div>
        </div>

        <label htmlFor="w-name">{adminCopy.details.name}</label>
        <input id="w-name" className="adm-input" value={name} onChange={(event) => setName(event.target.value)} />

        <label htmlFor="w-nat">{adminCopy.details.nationality}</label>
        <select id="w-nat" className="adm-input" value={nationality} onChange={(event) => setNationality(event.target.value)}>
          <option value="">{adminCopy.details.chooseCountry}</option>
          {countries.map((country) => (
            <option key={country.code} value={country.code}>
              {country.name}
            </option>
          ))}
        </select>

        <div className="adm-label">{adminCopy.details.position}</div>
        <div className="adm-chips">
          {POSITIONS.map((code) => {
            const index = positions.indexOf(code);
            return (
              <button key={code} type="button" className="adm-chip" aria-pressed={index >= 0} data-second={index === 1 ? "true" : undefined} onClick={() => toggle(code)}>
                {index === 1 ? `${code} · 2nd` : code}
              </button>
            );
          })}
        </div>

        <label htmlFor="w-vb">{adminCopy.details.volleybox}</label>
        <input
          id="w-vb"
          className="adm-input"
          value={vb}
          onChange={(event) => setVb(event.target.value)}
          placeholder={adminCopy.details.volleyboxPlaceholder}
          aria-invalid={vb !== "" && id === null}
        />
        <div className={id !== null ? "adm-hint adm-hint-ok" : vb ? "adm-hint adm-hint-bad" : "adm-hint"}>
          {id !== null ? adminCopy.details.volleyboxOk(id) : vb ? adminCopy.details.volleyboxBad : adminCopy.details.volleyboxRequired}
        </div>

        <ul className="adm-checks">
          {(["Photo", "Name", "Nationality", "Positions", "Volleybox"] as const).map((key) => (
            <li key={key} className={missing.includes(key) ? "adm-check" : "adm-check adm-check-ok"}>
              <span aria-hidden="true">{missing.includes(key) ? "•" : "✓"}</span>
              {adminCopy.details.checks[key]}
            </li>
          ))}
        </ul>

        {error ? (
          <p role="alert" className="adm-error">
            {error}
          </p>
        ) : null}
        <button type="button" className="adm-btn adm-btn-red adm-btn-wide" disabled={!ready || busy} onClick={() => void publish()}>
          {adminCopy.details.publish}
        </button>
        <p className="adm-hint">{ready ? adminCopy.details.ready : adminCopy.details.fieldsLeft(missing.length)}</p>
      </div>
    </section>
  );
}
