"use client";

import { useRouter } from "next/navigation";
import { adminCall, uploadPlayerPhoto } from "@/lib/admin/client";
import { formatDate } from "@/lib/admin/people";
import type { WaitingRow } from "@/lib/admin/types";
import { adminCopy } from "../copy";
import { CompleteDetails, type WaitingEdit, type WaitingPlayer } from "./complete-details";

/** Props of {@link WaitingBoard}. */
type WaitingBoardProps = {
  /** The page of waiting players, oldest first. */
  rows: WaitingRow[];
  /** The player whose drawer is open, with their details, or null. */
  selected: WaitingPlayer | null;
  /** The countries of the nationality picker. */
  countries: { code: string; name: string }[];
  /** The active `?missing=` filter, or null. */
  filter: string | null;
};

/** The filter chips: the label key and the API's value. */
const FILTERS: [string, string | null][] = [
  ["all", null],
  ["volleybox", "volleybox"],
  ["photo", "photo"],
  ["positions", "positions"],
];

/**
 * The waiting list's table and its drawer (A-5, canvas `Waiting`). Choosing a row opens the drawer through `?player=`;
 * saving and publishing go to the API through the console's route; "Next waiting player" opens the next row.
 *
 * @param props - See {@link WaitingBoardProps}.
 * @returns The table and the drawer.
 */
export function WaitingBoard({ rows, selected, countries, filter }: WaitingBoardProps) {
  const router = useRouter();
  const copy = adminCopy.waiting;

  /**
   * Builds an address that keeps the filter.
   *
   * @param player - The player to open, or null.
   * @param missing - The filter, or null.
   * @returns The path.
   */
  const href = (player: string | null, missing: string | null) => {
    const params = new URLSearchParams();
    if (missing) {
      params.set("missing", missing);
    }

    if (player) {
      params.set("player", player);
    }

    const text = params.toString();
    return text ? `/admin/waiting?${text}` : "/admin/waiting";
  };

  /**
   * Saves the drawer's details.
   *
   * @param id - The player.
   * @param edit - The details.
   * @returns A task that completes when they are saved.
   */
  const save = async (id: string, edit: WaitingEdit) => {
    await adminCall("PATCH", `players/${id}`, {
      name: edit.name,
      nationalityCode: edit.nationalityCode ?? undefined,
      positions: edit.positions,
      volleyboxUrl: edit.volleyboxUrl,
    });
  };

  /**
   * Publishes the player.
   *
   * @param id - The player.
   * @returns A task that completes when the player is published.
   */
  const publish = async (id: string) => {
    await adminCall("POST", `players/${id}/publish`, {});
  };

  /** Opens the next waiting player after this one is done. */
  const next = () => {
    const others = rows.filter((row) => row.id !== selected?.id);
    router.push(href(others[0]?.id ?? null, filter));
    router.refresh();
  };

  return (
    <div className="adm-split">
      <section aria-label="Players waiting for details">
        <div className="adm-chips" style={{ marginTop: 0 }}>
          {FILTERS.map(([key, value]) => (
            <a key={key} className="adm-chip" href={href(null, value)} aria-current={filter === value ? "true" : undefined}>
              {copy.filters[key]}
            </a>
          ))}
        </div>
        <div className="adm-table">
          <div className="adm-scroll">
            <div style={{ minWidth: 640 }}>
              <div className="adm-grid adm-grid-head" style={{ gridTemplateColumns: "2fr 1fr 2fr 1fr" }}>
                {copy.columns.map((column) => (
                  <span key={column}>{column}</span>
                ))}
              </div>
              {rows.map((row) => (
                <a
                  key={row.id}
                  href={href(row.id, filter)}
                  className="adm-grid adm-grid-row"
                  aria-current={selected?.id === row.id ? "true" : undefined}
                  style={{ gridTemplateColumns: "2fr 1fr 2fr 1fr", background: selected?.id === row.id ? "rgba(204,0,0,.12)" : undefined }}
                >
                  <span>
                    <span className="adm-name">{row.name ?? copy.noName}</span>
                    <span className="adm-sub">{row.folderName}</span>
                  </span>
                  <span className="adm-mono">
                    {row.videoCount} · {row.documentCount}
                  </span>
                  <span className="adm-chips" style={{ marginTop: 0 }}>
                    {row.missing.map((detail) => (
                      <span key={detail} className="adm-pill adm-pill-amber">
                        {detail}
                      </span>
                    ))}
                  </span>
                  <span className="adm-muted">{formatDate(row.waitingSince)}</span>
                </a>
              ))}
              {rows.length === 0 ? <div className="adm-empty">{copy.empty}</div> : null}
            </div>
          </div>
        </div>
      </section>
      {selected ? <CompleteDetails key={selected.id} player={selected} countries={countries} onSave={save} onPublish={publish} onNext={next} onPhoto={uploadPlayerPhoto} /> : null}
    </div>
  );
}
