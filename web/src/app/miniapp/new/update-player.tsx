"use client";

import { useEffect, useState } from "react";
import { addFolder, fetchCountries, fetchPlayer, updatePlayer, type Country, type MiniAppPlayer, type PlayerChanges, type PositionCode } from "@/lib/miniapp";
import { newPlayerCopy, ruleMessages, updateCopy as copy } from "../copy";
import { PhotoStep } from "./photo-step";
import { CountryPicker, DriveFolderField, PositionChips, type DriveFolderState } from "./player-fields";

/** The three choices of update mode. */
type Choice = "folder" | "details" | "open";

/**
 * Update mode (T-5): the existing player card with three choices: add a Drive folder (the default), update the photo
 * and details (only for whoever may), or just look at the player.
 *
 * @param props - `playerId`, `initData` for the API, the `initialFolderUrl` already typed in the form, and `onBack`
 * to return to the new-player form.
 * @returns The update screen.
 */
export function UpdatePlayer({
  playerId,
  initData,
  initialFolderUrl = "",
  onBack,
}: {
  playerId: string;
  initData: string;
  initialFolderUrl?: string;
  onBack?: () => void;
}) {
  const [player, setPlayer] = useState<MiniAppPlayer | null | undefined>(undefined);
  const [choice, setChoice] = useState<Choice>("folder");

  useEffect(() => {
    let cancelled = false;
    void fetchPlayer(playerId, initData).then((card) => {
      if (!cancelled) {
        setPlayer(card);
      }
    });
    return () => {
      cancelled = true;
    };
  }, [playerId, initData]);

  if (player === undefined) {
    return <p role="status">{copy.loading}</p>;
  }

  if (player === null) {
    return <p role="alert">{copy.notFound}</p>;
  }

  const tabs: [Choice, string][] = [
    ["folder", copy.addFolderTab],
    ["details", copy.detailsTab],
    ["open", copy.openTab],
  ];

  return (
    <main>
      <h1>{copy.title}</h1>
      <section aria-label={player.name ?? undefined}>
        <h2>{player.name}</h2>
        <p>
          {[player.positions.join(" / "), player.nationalityName, player.category, player.status].filter(Boolean).join(" · ")}
        </p>
        <p>{copy.owner(player.analystName)}</p>
        <p>{copy.folders(player.folderCount)}</p>
      </section>

      <div role="tablist">
        {tabs.map(([key, label]) => (
          <button key={key} type="button" role="tab" aria-selected={choice === key} onClick={() => setChoice(key)}>
            {label}
          </button>
        ))}
      </div>

      <div role="tabpanel">
        {choice === "folder" && <AddFolderPanel playerId={player.id} initData={initData} initialFolderUrl={initialFolderUrl} />}
        {choice === "details" && <DetailsPanel player={player} initData={initData} onSaved={(changes) => setPlayer({ ...player, ...changes })} />}
        {choice === "open" && <p>{copy.pageLater}</p>}
      </div>

      {onBack && (
        <button type="button" onClick={onBack}>
          {copy.back}
        </button>
      )}
    </main>
  );
}

/**
 * Adds a checked Drive folder to the player.
 *
 * @param props - `playerId`, `initData` and the `initialFolderUrl` to start with.
 * @returns The panel.
 */
function AddFolderPanel({ playerId, initData, initialFolderUrl }: { playerId: string; initData: string; initialFolderUrl: string }) {
  const [url, setUrl] = useState(initialFolderUrl);
  const [check, setCheck] = useState<DriveFolderState>({ kind: "empty" });
  const [result, setResult] = useState<{ kind: "idle" | "sending" } | { kind: "added"; name: string } | { kind: "rejected"; code: string | null }>({ kind: "idle" });

  /** Adds the folder. */
  async function onAdd() {
    setResult({ kind: "sending" });
    const added = await addFolder(playerId, url, initData);
    setResult(added.kind === "added" ? { kind: "added", name: added.folder.name } : added);
  }

  if (result.kind === "added") {
    return <p role="status">{copy.folderAdded(result.name)}</p>;
  }

  return (
    <>
      <DriveFolderField value={url} onChange={setUrl} state={check} onStateChange={setCheck} initData={initData} />
      {result.kind === "rejected" && <p role="alert">{ruleMessages[result.code ?? ""] ?? newPlayerCopy.rejected}</p>}
      <button type="button" disabled={check.kind !== "found" || result.kind === "sending"} onClick={() => void onAdd()}>
        {result.kind === "sending" ? copy.adding : copy.addFolder}
      </button>
    </>
  );
}

/**
 * Edits the details (sending only what changed) and the photo, or explains who may.
 *
 * @param props - The `player` card, `initData`, and `onSaved` with the saved changes.
 * @returns The panel.
 */
function DetailsPanel({ player, initData, onSaved }: { player: MiniAppPlayer; initData: string; onSaved: (changes: PlayerChanges) => void }) {
  const [countries, setCountries] = useState<Country[]>([]);
  const [name, setName] = useState(player.name ?? "");
  const [nationalityCode, setNationalityCode] = useState(player.nationalityCode ?? "");
  const [positions, setPositions] = useState<PositionCode[]>(player.positions);
  const [state, setState] = useState<{ kind: "idle" | "sending" | "saved" | "unchanged" } | { kind: "rejected"; code: string | null }>({ kind: "idle" });

  useEffect(() => {
    if (!player.canEditDetails) {
      return;
    }

    let cancelled = false;
    void fetchCountries(initData).then((list) => {
      if (!cancelled) {
        setCountries(list);
      }
    });
    return () => {
      cancelled = true;
    };
  }, [player.canEditDetails, initData]);

  /** Sends the fields that differ from the card. */
  async function onSave() {
    const changes: PlayerChanges = {};
    if (name.trim() !== (player.name ?? "")) {
      changes.name = name.trim();
    }

    if (nationalityCode && nationalityCode !== player.nationalityCode) {
      changes.nationalityCode = nationalityCode;
    }

    if (positions.length > 0 && positions.join() !== player.positions.join()) {
      changes.positions = positions;
    }

    if (Object.keys(changes).length === 0) {
      setState({ kind: "unchanged" });
      return;
    }

    setState({ kind: "sending" });
    const result = await updatePlayer(player.id, changes, initData);
    if (result.kind === "updated") {
      onSaved(changes);
      setState({ kind: "saved" });
    } else {
      setState(result);
    }
  }

  return (
    <>
      {player.canEditDetails ? (
        <>
          <label htmlFor="player-name">{newPlayerCopy.name}</label>
          <input id="player-name" value={name} autoComplete="off" onChange={(event) => setName(event.target.value)} />
          <CountryPicker countries={countries} value={nationalityCode} onChange={setNationalityCode} />
          <PositionChips value={positions} onChange={setPositions} />
          {state.kind === "saved" && <p role="status">{copy.saved}</p>}
          {state.kind === "unchanged" && <p role="status">{copy.nothingChanged}</p>}
          {state.kind === "rejected" && <p role="alert">{ruleMessages[state.code ?? ""] ?? newPlayerCopy.rejected}</p>}
          <button type="button" disabled={state.kind === "sending"} onClick={() => void onSave()}>
            {state.kind === "sending" ? copy.saving : copy.save}
          </button>
        </>
      ) : (
        <p role="status">{player.status === "Published" && player.canChangePhoto ? copy.onlyAdminAfterPublish : copy.onlyOwner(player.analystName)}</p>
      )}

      {player.canChangePhoto && <PhotoStep playerId={player.id} playerName={player.name ?? ""} initData={initData} />}
    </>
  );
}
