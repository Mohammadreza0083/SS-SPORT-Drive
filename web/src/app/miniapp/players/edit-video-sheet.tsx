"use client";

import { useState } from "react";
import { editPlayerFile, FILE_TAGS, type FileChanges, type PlayerFile } from "@/lib/miniapp";
import { editVideoCopy as copy, ruleMessages } from "../copy";

/** Where the sheet is: editing, saving, nothing to save, or refused with a rule code. */
type SheetState = { kind: "idle" | "saving" | "unchanged" } | { kind: "rejected"; code: string | null };

/**
 * The edit-video bottom sheet (T-7b): title, tags and the hero choice for the owning analyst or an admin, and
 * Hide/Show for admins only. Only the fields that changed are sent.
 *
 * @param props - `playerId`, the `file` to edit, `canHide` (admins), `initData`, `onSaved` with the saved file and
 * `onClose`.
 * @returns The sheet.
 */
export function EditVideoSheet({
  playerId,
  file,
  canHide,
  initData,
  onSaved,
  onClose,
}: {
  playerId: string;
  file: PlayerFile;
  canHide: boolean;
  initData: string;
  onSaved: (file: PlayerFile) => void;
  onClose: () => void;
}) {
  const [title, setTitle] = useState(file.title);
  const [tags, setTags] = useState<string[]>(file.tags);
  const [isHero, setIsHero] = useState(file.isHero);
  const [hidden, setHidden] = useState(file.visibility === "Hidden");
  const [state, setState] = useState<SheetState>({ kind: "idle" });

  /**
   * Turns a tag on or off, keeping the API's tag order.
   *
   * @param tag - The tag.
   */
  function toggleTag(tag: string) {
    const chosen = tags.includes(tag) ? tags.filter((name) => name !== tag) : [...tags, tag];
    setTags(FILE_TAGS.filter((name) => chosen.includes(name)));
  }

  /** Sends what changed, or says nothing did. */
  async function onSave() {
    const changes = changedFields();
    if (Object.keys(changes).length === 0) {
      setState({ kind: "unchanged" });
      return;
    }

    setState({ kind: "saving" });
    const result = await editPlayerFile(playerId, file.id, changes, initData);
    if (result.kind === "saved") {
      onSaved(result.file);
    } else {
      setState(result);
    }
  }

  /**
   * Compares the sheet with the file.
   *
   * @returns Only the fields that differ.
   */
  function changedFields(): FileChanges {
    const changes: FileChanges = {};
    if (title.trim() !== file.title) {
      changes.title = title.trim();
    }

    if (tags.join() !== file.tags.join()) {
      changes.tags = tags;
    }

    if (isHero !== file.isHero) {
      changes.isHero = isHero;
    }

    if (canHide && hidden !== (file.visibility === "Hidden")) {
      changes.hidden = hidden;
    }

    return changes;
  }

  // A hidden file or a document cannot be the hero, so the choice is only offered for a visible video.
  const heroAllowed = file.kind === "Video" && !hidden;

  return (
    <div role="dialog" aria-modal="true" aria-label={copy.title}>
      <h2>{copy.title}</h2>
      <label>
        {copy.titleLabel}
        <input value={title} maxLength={200} onChange={(event) => setTitle(event.target.value)} />
      </label>
      <p>{copy.titleHint}</p>

      <fieldset>
        <legend>{copy.tags}</legend>
        {FILE_TAGS.map((tag) => (
          <button key={tag} type="button" aria-pressed={tags.includes(tag)} onClick={() => toggleTag(tag)}>
            {copy.tagNames[tag]}
          </button>
        ))}
      </fieldset>

      {heroAllowed && (
        <label>
          <input type="checkbox" checked={isHero} onChange={(event) => setIsHero(event.target.checked)} />
          {copy.hero}
        </label>
      )}
      {heroAllowed && <p>{copy.heroHint}</p>}

      {canHide && (
        <>
          {hidden && <p>{copy.hiddenNote}</p>}
          <button
            type="button"
            onClick={() => {
              setHidden(!hidden);
              if (!hidden) {
                setIsHero(false);
              }
            }}
          >
            {hidden ? copy.show : copy.hide}
          </button>
        </>
      )}

      {state.kind === "rejected" && <p role="alert">{ruleMessages[state.code ?? ""] ?? copy.rejected}</p>}
      {state.kind === "unchanged" && <p role="status">{copy.nothingChanged}</p>}

      <button type="button" disabled={state.kind === "saving"} onClick={() => void onSave()}>
        {state.kind === "saving" ? copy.saving : copy.save}
      </button>
      <button type="button" onClick={onClose}>
        {copy.cancel}
      </button>
    </div>
  );
}
