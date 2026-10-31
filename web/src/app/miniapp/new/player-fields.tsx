"use client";

import { useMemo, useState } from "react";
import { POSITIONS, checkDriveFolder, formatBytes, isDriveFolderLink, togglePosition, type Country, type DriveFolderCheck, type PositionCode } from "@/lib/miniapp";
import { newPlayerCopy as copy, ruleMessages, updateCopy } from "../copy";

/** How many countries the picker lists while searching. */
const MAX_MATCHES = 8;

/** The live state of a Drive folder field: the check result, or still checking. */
export type DriveFolderState = DriveFolderCheck | { kind: "checking" };

/**
 * The nationality picker: type to search the catalogue, tap a country to choose it.
 *
 * @param props - `countries` (the catalogue), the chosen `value` (a code, or empty) and `onChange`.
 * @returns The picker.
 */
export function CountryPicker({ countries, value, onChange }: { countries: Country[]; value: string; onChange: (code: string) => void }) {
  const [query, setQuery] = useState("");
  const chosen = countries.find((country) => country.code === value);
  const matches = useMemo(() => {
    const text = query.trim().toLowerCase();
    return text ? countries.filter((country) => country.name.toLowerCase().includes(text) || country.code.toLowerCase() === text).slice(0, MAX_MATCHES) : [];
  }, [countries, query]);

  return (
    <>
      <label htmlFor="player-nationality">{copy.nationality}</label>
      <input
        id="player-nationality"
        role="combobox"
        aria-expanded={matches.length > 0}
        aria-controls="player-nationality-options"
        placeholder={copy.nationalityPlaceholder}
        value={query || chosen?.name || ""}
        onChange={(event) => {
          setQuery(event.target.value);
          onChange("");
        }}
      />
      {matches.length > 0 && (
        <div id="player-nationality-options" role="listbox" className="ma-options">
          {matches.map((country) => (
            <button
              key={country.code}
              type="button"
              role="option"
              aria-selected={country.code === value}
              onClick={() => {
                onChange(country.code);
                setQuery("");
              }}
            >
              {country.name} ({country.code})
            </button>
          ))}
        </div>
      )}
    </>
  );
}

/**
 * The position chips: the first tap chooses the primary, the second the secondary.
 *
 * @param props - The chosen `value` (primary first) and `onChange`.
 * @returns The chips.
 */
export function PositionChips({ value, onChange }: { value: PositionCode[]; onChange: (positions: PositionCode[]) => void }) {
  return (
    <fieldset className="ma-positions">
      <legend>{copy.positions}</legend>
      <p className="ma-hint">{copy.positionsHint}</p>
      {POSITIONS.map((position) => (
        <button
          key={position}
          type="button"
          aria-pressed={value.includes(position)}
          data-primary={value[0] === position || undefined}
          onClick={() => onChange(togglePosition(value, position))}
        >
          {position}
        </button>
      ))}
    </fieldset>
  );
}

/**
 * The Drive folder link with its live check (T-4c): a format hint while typing, then the folder preview, the player
 * it already belongs to (with a button to open that player), or why it cannot be used.
 *
 * @param props - The link `value` and `onChange`, the check `state` and `onStateChange`, `initData` for the API, and
 * `onOpenPlayer` to open the player a folder already belongs to.
 * @returns The field.
 */
export function DriveFolderField({
  value,
  onChange,
  state,
  onStateChange,
  initData,
  onOpenPlayer,
}: {
  value: string;
  onChange: (value: string) => void;
  state: DriveFolderState;
  onStateChange: (state: DriveFolderState) => void;
  initData: string;
  onOpenPlayer?: (playerId: string) => void;
}) {
  const wellFormed = isDriveFolderLink(value);

  /** Checks the folder with the service account once the analyst leaves the field (a well-formed link only). */
  async function onBlur() {
    if (!wellFormed) {
      return;
    }

    onStateChange({ kind: "checking" });
    onStateChange(await checkDriveFolder(value, initData));
  }

  return (
    <>
      <label htmlFor="player-drive">{copy.drive}</label>
      <input
        id="player-drive"
        inputMode="url"
        value={value}
        onChange={(event) => {
          onChange(event.target.value);
          onStateChange({ kind: "empty" });
        }}
        onBlur={() => void onBlur()}
      />
      {value.trim() !== "" && !wellFormed && <p role="alert">{ruleMessages["drive_folder.link_invalid"]}</p>}
      {state.kind === "checking" && (
        <p role="status" className="ma-hint">
          {copy.driveChecking}
        </p>
      )}
      {state.kind === "found" && (
        <p role="status" className="ma-folder">
          {copy.driveFound(
            state.preview.name,
            state.preview.videoCount,
            state.preview.documentCount,
            state.preview.scoutCount,
            formatBytes(state.preview.totalBytes),
            state.preview.isTruncated,
          )}
        </p>
      )}
      {state.kind === "taken" && (
        <p role="alert">
          {updateCopy.takenFolder(state.playerName)}{" "}
          {onOpenPlayer && (
            <button type="button" onClick={() => onOpenPlayer(state.playerId)}>
              {updateCopy.openPlayer(state.playerName)}
            </button>
          )}
        </p>
      )}
      {state.kind === "invalid" && <p role="alert">{ruleMessages[state.code ?? ""] ?? ruleMessages["drive.unavailable"]}</p>}
    </>
  );
}
