"use client";

import { useEffect, useMemo, useState } from "react";
import {
  POSITIONS,
  checkDriveFolder,
  checkVolleybox,
  createPlayer,
  fetchCountries,
  formatBytes,
  isDriveFolderLink,
  togglePosition,
  type Country,
  type DriveFolderCheck,
  type NewPlayerForm as NewPlayerFormValues,
  type PositionCode,
  type VolleyboxCheck,
} from "@/lib/miniapp";
import { newPlayerCopy as copy, ruleMessages } from "../copy";
import { PhotoStep } from "./photo-step";

/** Where the draft is kept, so closing the Mini App never loses what was typed. */
const DRAFT_KEY = "ssd.miniapp.newPlayerDraft";

/** How many countries the picker lists while searching. */
const MAX_MATCHES = 8;

/** Latin letters (accents allowed), spaces, hyphens and apostrophes: the API's name rule. */
const LATIN_NAME = /^[\p{Script=Latin}\s'-]+$/u;

/** What the analyst has typed so far. */
interface Draft {
  category: NewPlayerFormValues["category"] | null;
  name: string;
  nationalityCode: string;
  positions: PositionCode[];
  volleyboxUrl: string;
  driveFolderUrl: string;
}

/** The empty form. */
const EMPTY_DRAFT: Draft = { category: null, name: "", nationalityCode: "", positions: [], volleyboxUrl: "", driveFolderUrl: "" };

/** Where sending the form stands. */
type SubmitState = { kind: "idle" } | { kind: "sending" } | { kind: "created"; id: string; name: string } | { kind: "rejected"; code: string | null };

/**
 * Reads the saved draft, ignoring anything unreadable.
 *
 * @returns The draft, or the empty form.
 */
function loadDraft(): Draft {
  try {
    const saved = JSON.parse(window.localStorage.getItem(DRAFT_KEY) ?? "{}") as Partial<Draft>;
    return { ...EMPTY_DRAFT, ...saved, positions: (saved.positions ?? []).filter((code) => POSITIONS.includes(code)) };
  } catch {
    return EMPTY_DRAFT;
  }
}

/**
 * The new-player form (T-4a, T-4c): category, name, nationality, positions, Volleybox link and Drive folder link, both
 * checked live (the folder with the service account). **Create player** stays disabled until every check is green.
 * The draft is saved as the analyst types.
 *
 * @param props - `initData`: the signed initData every API call carries.
 * @returns The form, or the confirmation once the player is saved.
 */
export function NewPlayerForm({ initData }: { initData: string }) {
  const [draft, setDraft] = useState<Draft>(loadDraft);
  const [countries, setCountries] = useState<Country[]>([]);
  const [countryQuery, setCountryQuery] = useState("");
  const [volleybox, setVolleybox] = useState<VolleyboxCheck>({ kind: "empty" });
  const [driveFolder, setDriveFolder] = useState<DriveFolderCheck | { kind: "checking" }>({ kind: "empty" });
  const [submit, setSubmit] = useState<SubmitState>({ kind: "idle" });

  useEffect(() => {
    let cancelled = false;
    void fetchCountries(initData).then((list) => {
      if (!cancelled) {
        setCountries(list);
      }
    });
    return () => {
      cancelled = true;
    };
  }, [initData]);

  // A restored draft's Volleybox link is checked again, since another analyst may have used it meanwhile.
  const [restoredVolleybox] = useState(draft.volleyboxUrl);
  useEffect(() => {
    let cancelled = false;
    void checkVolleybox(restoredVolleybox, initData).then((result) => {
      if (!cancelled) {
        setVolleybox(result);
      }
    });
    return () => {
      cancelled = true;
    };
  }, [restoredVolleybox, initData]);

  // The same for a restored Drive link: access or the folder itself may have changed.
  const [restoredDriveFolder] = useState(draft.driveFolderUrl);
  useEffect(() => {
    if (!isDriveFolderLink(restoredDriveFolder)) {
      return;
    }

    let cancelled = false;
    void checkDriveFolder(restoredDriveFolder, initData).then((result) => {
      if (!cancelled) {
        setDriveFolder(result);
      }
    });
    return () => {
      cancelled = true;
    };
  }, [restoredDriveFolder, initData]);

  useEffect(() => {
    try {
      window.localStorage.setItem(DRAFT_KEY, JSON.stringify(draft));
    } catch {
      // Storage can be unavailable (private mode); the form still works without a draft.
    }
  }, [draft]);

  const chosenCountry = countries.find((country) => country.code === draft.nationalityCode);
  const matches = useMemo(() => {
    const query = countryQuery.trim().toLowerCase();
    return query ? countries.filter((country) => country.name.toLowerCase().includes(query) || country.code.toLowerCase() === query).slice(0, MAX_MATCHES) : [];
  }, [countries, countryQuery]);

  const nameValid = LATIN_NAME.test(draft.name.trim());
  const driveValid = isDriveFolderLink(draft.driveFolderUrl);
  const canCreate =
    draft.category !== null &&
    nameValid &&
    draft.nationalityCode !== "" &&
    draft.positions.length > 0 &&
    volleybox.kind === "new" &&
    driveFolder.kind === "found" &&
    submit.kind !== "sending";

  /**
   * Changes one field of the draft.
   *
   * @param changes - The fields to change.
   */
  function update(changes: Partial<Draft>) {
    setDraft((current) => ({ ...current, ...changes }));
    setSubmit({ kind: "idle" });
  }

  /** Checks the Volleybox link once the analyst leaves the field. */
  async function onVolleyboxBlur() {
    setVolleybox(await checkVolleybox(draft.volleyboxUrl, initData));
  }

  /** Checks the Drive folder with the service account once the analyst leaves the field (a well-formed link only). */
  async function onDriveBlur() {
    if (!isDriveFolderLink(draft.driveFolderUrl)) {
      return;
    }

    setDriveFolder({ kind: "checking" });
    setDriveFolder(await checkDriveFolder(draft.driveFolderUrl, initData));
  }

  /** Sends the form. */
  async function onCreate() {
    if (!canCreate || draft.category === null) {
      return;
    }

    setSubmit({ kind: "sending" });
    const result = await createPlayer({ ...draft, category: draft.category, name: draft.name.trim() }, initData);
    if (result.kind === "created") {
      try {
        window.localStorage.removeItem(DRAFT_KEY);
      } catch {
        // Nothing to clean up when storage is unavailable.
      }

      setSubmit({ kind: "created", id: result.player.id, name: result.player.name });
    } else {
      setSubmit({ kind: "rejected", code: result.code });
    }
  }

  if (submit.kind === "created") {
    return (
      <main>
        <h1>{copy.title}</h1>
        <p role="status">{copy.created(submit.name)}</p>
        <PhotoStep playerId={submit.id} playerName={submit.name} initData={initData} />
      </main>
    );
  }

  return (
    <main>
      <h1>{copy.title}</h1>

      <fieldset>
        <legend>{copy.category}</legend>
        {(["Men", "Women"] as const).map((category) => (
          <button key={category} type="button" aria-pressed={draft.category === category} onClick={() => update({ category })}>
            {category === "Men" ? copy.men : copy.women}
          </button>
        ))}
      </fieldset>

      <label htmlFor="player-name">{copy.name}</label>
      <input id="player-name" value={draft.name} autoComplete="off" onChange={(event) => update({ name: event.target.value })} />
      {draft.name.trim() !== "" && !nameValid && <p role="alert">{copy.nameHint}</p>}

      <label htmlFor="player-nationality">{copy.nationality}</label>
      <input
        id="player-nationality"
        role="combobox"
        aria-expanded={matches.length > 0}
        aria-controls="player-nationality-options"
        placeholder={copy.nationalityPlaceholder}
        value={countryQuery || chosenCountry?.name || ""}
        onChange={(event) => {
          setCountryQuery(event.target.value);
          update({ nationalityCode: "" });
        }}
      />
      {matches.length > 0 && (
        <div id="player-nationality-options" role="listbox">
          {matches.map((country) => (
            <button
              key={country.code}
              type="button"
              role="option"
              aria-selected={country.code === draft.nationalityCode}
              onClick={() => {
                update({ nationalityCode: country.code });
                setCountryQuery("");
              }}
            >
              {country.name} ({country.code})
            </button>
          ))}
        </div>
      )}

      <fieldset>
        <legend>{copy.positions}</legend>
        <p>{copy.positionsHint}</p>
        {POSITIONS.map((position) => (
          <button
            key={position}
            type="button"
            aria-pressed={draft.positions.includes(position)}
            data-primary={draft.positions[0] === position || undefined}
            onClick={() => update({ positions: togglePosition(draft.positions, position) })}
          >
            {position}
          </button>
        ))}
      </fieldset>

      <label htmlFor="player-volleybox">{copy.volleybox}</label>
      <input
        id="player-volleybox"
        inputMode="url"
        value={draft.volleyboxUrl}
        onChange={(event) => {
          update({ volleyboxUrl: event.target.value });
          setVolleybox({ kind: "empty" });
        }}
        onBlur={() => void onVolleyboxBlur()}
      />
      {volleybox.kind === "new" && <p role="status">{copy.volleyboxNew(volleybox.volleyboxId)}</p>}
      {volleybox.kind === "existing" && <p role="alert">{copy.volleyboxExisting(volleybox.playerName)}</p>}
      {volleybox.kind === "invalid" && <p role="alert">{ruleMessages[volleybox.code ?? ""] ?? ruleMessages["player.volleybox_url_invalid"]}</p>}

      <label htmlFor="player-drive">{copy.drive}</label>
      <input
        id="player-drive"
        inputMode="url"
        value={draft.driveFolderUrl}
        onChange={(event) => {
          update({ driveFolderUrl: event.target.value });
          setDriveFolder({ kind: "empty" });
        }}
        onBlur={() => void onDriveBlur()}
      />
      {draft.driveFolderUrl.trim() !== "" && !driveValid && <p role="alert">{ruleMessages["drive_folder.link_invalid"]}</p>}
      {driveFolder.kind === "checking" && <p role="status">{copy.driveChecking}</p>}
      {driveFolder.kind === "found" && (
        <p role="status">
          {copy.driveFound(
            driveFolder.preview.name,
            driveFolder.preview.videoCount,
            driveFolder.preview.documentCount,
            driveFolder.preview.scoutCount,
            formatBytes(driveFolder.preview.totalBytes),
            driveFolder.preview.isTruncated,
          )}
        </p>
      )}
      {driveFolder.kind === "invalid" && <p role="alert">{ruleMessages[driveFolder.code ?? ""] ?? ruleMessages["drive.unavailable"]}</p>}

      <p>{copy.photoLater}</p>
      {submit.kind === "rejected" && <p role="alert">{ruleMessages[submit.code ?? ""] ?? copy.rejected}</p>}

      <button type="button" disabled={!canCreate} onClick={() => void onCreate()}>
        {submit.kind === "sending" ? copy.creating : copy.create}
      </button>
    </main>
  );
}
