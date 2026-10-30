"use client";

import Link from "next/link";
import { useEffect, useState } from "react";
import { fetchPhotoUrl, fetchPlayer, fetchPlayerFiles, type MiniAppPlayer, type Page, type PlayerFile } from "@/lib/miniapp";
import { editVideoCopy, myPlayersCopy as copy, updateCopy } from "../copy";
import { UpdatePlayer } from "../new/update-player";
import { EditVideoSheet } from "./edit-video-sheet";

/**
 * The player detail (T-7a): the player card with its photo, the linked Drive folders and the files in page order
 * (hero starred, hidden ones marked). "Edit player" opens the T-4b/T-5 photo, details and folder steps, and each
 * file opens the edit-video sheet (T-7b) for whoever may edit files.
 *
 * @param props - `playerId` and `initData` for the API.
 * @returns The detail screen.
 */
export function PlayerDetail({ playerId, initData }: { playerId: string; initData: string }) {
  const [player, setPlayer] = useState<MiniAppPlayer | null | undefined>(undefined);
  const [editing, setEditing] = useState(false);

  useEffect(() => {
    if (editing) {
      return;
    }

    let cancelled = false;
    void fetchPlayer(playerId, initData).then((card) => {
      if (!cancelled) {
        setPlayer(card);
      }
    });
    return () => {
      cancelled = true;
    };
  }, [playerId, initData, editing]);

  if (editing) {
    return (
      <>
        <UpdatePlayer playerId={playerId} initData={initData} />
        <button type="button" onClick={() => setEditing(false)}>
          {copy.closeEditor}
        </button>
      </>
    );
  }

  if (player === undefined) {
    return <p role="status">{updateCopy.loading}</p>;
  }

  if (player === null) {
    return <p role="alert">{updateCopy.notFound}</p>;
  }

  return (
    <main>
      <Link href="/miniapp/players">{copy.back}</Link>
      {player.hasPhoto && <PlayerPhoto playerId={player.id} name={player.name} initData={initData} />}
      <h1>{player.name}</h1>
      <p>{[player.positions.join(" / "), player.nationalityName, player.category, player.status].filter(Boolean).join(" · ")}</p>
      <p>{updateCopy.owner(player.analystName)}</p>
      <p>{copy.pageLater}</p>
      <button type="button" onClick={() => setEditing(true)}>
        {copy.editPlayer}
      </button>

      <section aria-label={copy.foldersTitle}>
        <h2>{copy.foldersTitle}</h2>
        <p>{updateCopy.folders(player.folderCount)}</p>
        <ul>
          {player.folders.map((folder) => (
            <li key={folder.id}>
              {folder.name}
              {folder.access === "Lost" && ` · ${copy.accessLost}`}
            </li>
          ))}
        </ul>
      </section>

      <FileList playerId={player.id} initData={initData} canEdit={player.canEditFiles === true} canHide={player.canHideFiles === true} />
    </main>
  );
}

/**
 * Shows the player's photo, read with the `tma` header into a local URL.
 *
 * @param props - `playerId`, the `name` for the alt text and `initData`.
 * @returns The photo, once loaded.
 */
function PlayerPhoto({ playerId, name, initData }: { playerId: string; name: string | null; initData: string }) {
  const [url, setUrl] = useState<string | null>(null);

  useEffect(() => {
    let created: string | null = null;
    let cancelled = false;
    void fetchPhotoUrl(playerId, initData).then((objectUrl) => {
      created = objectUrl;
      if (!cancelled) {
        setUrl(objectUrl);
      }
    });
    return () => {
      cancelled = true;
      if (created) {
        URL.revokeObjectURL(created);
      }
    };
  }, [playerId, initData]);

  // eslint-disable-next-line @next/next/no-img-element -- an object URL from an authorised fetch; next/image cannot load it.
  return url ? <img src={url} alt={name ?? ""} width={160} /> : null;
}

/**
 * The player's files in page order, one page at a time. For whoever may edit files, each file opens the edit-video
 * sheet; after a save the page is read again, since choosing a hero changes the other files too.
 *
 * @param props - `playerId`, `initData`, `canEdit` (owner or admin) and `canHide` (admins).
 * @returns The file list.
 */
function FileList({ playerId, initData, canEdit, canHide }: { playerId: string; initData: string; canEdit: boolean; canHide: boolean }) {
  const [page, setPage] = useState(1);
  const [reload, setReload] = useState(0);
  const [editing, setEditing] = useState<PlayerFile | null>(null);
  const [files, setFiles] = useState<Page<PlayerFile> | null | undefined>(undefined);

  useEffect(() => {
    let cancelled = false;
    void fetchPlayerFiles(playerId, page, initData).then((result) => {
      if (!cancelled) {
        setFiles(result);
      }
    });
    return () => {
      cancelled = true;
    };
  }, [playerId, page, initData, reload]);

  return (
    <section aria-label={copy.filesTitle}>
      <h2>{copy.filesTitle}</h2>
      {files === undefined && <p role="status">{copy.loadingFiles}</p>}
      {files === null && <p role="alert">{copy.failed}</p>}
      {files && files.items.length === 0 && <p>{copy.noFiles}</p>}
      {files && files.items.length > 0 && (
        <>
          <ul>
            {files.items.map((file) => (
              <li key={file.id} aria-label={`file ${file.title}`}>
                <strong>{file.isHero ? `${copy.hero} ${file.title}` : file.title}</strong>{" "}
                <span>{[file.kind, ...file.tags, file.state, file.visibility === "Hidden" ? copy.hidden : null].filter(Boolean).join(" · ")}</span>
                {canEdit && (
                  <button type="button" onClick={() => setEditing(file)}>
                    {editVideoCopy.editFile(file.title)}
                  </button>
                )}
              </li>
            ))}
          </ul>
          {files.totalPages > 1 && (
            <nav>
              <button type="button" disabled={page <= 1} onClick={() => setPage(page - 1)}>
                {copy.previous}
              </button>
              <span>{copy.pageOf(files.page, files.totalPages)}</span>
              <button type="button" disabled={page >= files.totalPages} onClick={() => setPage(page + 1)}>
                {copy.next}
              </button>
            </nav>
          )}
        </>
      )}
      {editing && (
        <EditVideoSheet
          playerId={playerId}
          file={editing}
          canHide={canHide}
          initData={initData}
          onSaved={() => {
            setEditing(null);
            setReload(reload + 1);
          }}
          onClose={() => setEditing(null)}
        />
      )}
    </section>
  );
}
