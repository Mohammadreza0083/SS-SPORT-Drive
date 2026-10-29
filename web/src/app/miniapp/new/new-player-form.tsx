"use client";

import { useEffect, useState } from "react";
import {
  POSITIONS,
  checkDriveFolder,
  checkVolleybox,
  createPlayer,
  fetchCountries,
  isDriveFolderLink,
  type Country,
  type NewPlayerForm as NewPlayerFormValues,
  type PositionCode,
  type VolleyboxCheck,
} from "@/lib/miniapp";
import { newPlayerCopy as copy, ruleMessages } from "../copy";
import { PhotoStep } from "./photo-step";
import { CountryPicker, DriveFolderField, PositionChips, type DriveFolderState } from "./player-fields";
import { UpdatePlayer } from "./update-player";

/** Where the draft is kept, so closing the Mini App never loses what was typed. */
const DRAFT_KEY = "ssd.miniapp.newPlayerDraft";

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
 * The draft is saved as the analyst types. A Volleybox id that already has a player, or opening the player a folder
 * belongs to, switches to update mode (T-5).
 *
 * @param props - `initData`: the signed initData every API call carries.
 * @returns The form, or the confirmation once the player is saved.
 */
export function NewPlayerForm({ initData }: { initData: string }) {
  const [draft, setDraft] = useState<Draft>(loadDraft);
  const [countries, setCountries] = useState<Country[]>([]);
  const [volleybox, setVolleybox] = useState<VolleyboxCheck>({ kind: "empty" });
  const [driveFolder, setDriveFolder] = useState<DriveFolderState>({ kind: "empty" });
  const [openPlayerId, setOpenPlayerId] = useState<string | null>(null);
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

  const nameValid = LATIN_NAME.test(draft.name.trim());
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

  const updatingId = openPlayerId ?? (volleybox.kind === "existing" ? volleybox.playerId : null);
  if (updatingId) {
    return (
      <UpdatePlayer
        playerId={updatingId}
        initData={initData}
        initialFolderUrl={driveFolder.kind === "taken" ? "" : draft.driveFolderUrl}
        onBack={() => {
          setOpenPlayerId(null);
          setVolleybox({ kind: "empty" });
          update({ volleyboxUrl: "" });
        }}
      />
    );
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

      <CountryPicker countries={countries} value={draft.nationalityCode} onChange={(code) => update({ nationalityCode: code })} />

      <PositionChips value={draft.positions} onChange={(positions) => update({ positions })} />

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
      {volleybox.kind === "invalid" && <p role="alert">{ruleMessages[volleybox.code ?? ""] ?? ruleMessages["player.volleybox_url_invalid"]}</p>}

      <DriveFolderField
        value={draft.driveFolderUrl}
        onChange={(driveFolderUrl) => update({ driveFolderUrl })}
        state={driveFolder}
        onStateChange={setDriveFolder}
        initData={initData}
        onOpenPlayer={setOpenPlayerId}
      />

      <p>{copy.photoLater}</p>
      {submit.kind === "rejected" && <p role="alert">{ruleMessages[submit.code ?? ""] ?? copy.rejected}</p>}

      <button type="button" disabled={!canCreate} onClick={() => void onCreate()}>
        {submit.kind === "sending" ? copy.creating : copy.create}
      </button>
    </main>
  );
}
