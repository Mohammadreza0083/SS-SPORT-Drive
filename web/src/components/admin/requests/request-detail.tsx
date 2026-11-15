"use client";

import { useState } from "react";
import { AdminActionError } from "@/lib/admin/client";
import { formatDate } from "@/lib/admin/people";
import { buildApproval, MAX_GRANTED_PLAYERS, requesterInitials, type ApprovalBody } from "@/lib/admin/requests";
import { LINK_EXPIRY_CHOICES } from "@/lib/admin/settings-form";
import type { AccessRequestRow } from "@/lib/admin/types";
import { adminCopy } from "../copy";
import { StatusBadge } from "../status-badge";

/** A player the admin can grant. */
export type GrantablePlayer = { id: string; name: string };

/** Props of {@link RequestDetail}. */
type RequestDetailProps = {
  /** The open request. */
  request: AccessRequestRow;
  /** The expiry the settings preselect (7, 30 or 90 days). */
  defaultExpiryDays: number;
  /** Approves the request; resolves with the decided request. */
  onApprove: (body: ApprovalBody) => Promise<AccessRequestRow>;
  /** Declines the request; resolves with the decided request. */
  onDecline: () => Promise<AccessRequestRow>;
  /** Finds published players by name. */
  onSearchPlayers: (text: string) => Promise<GrantablePlayer[]>;
  /** Where "Next request" goes once a decision is made here; omitted for a request that was already decided. */
  nextHref?: string;
};

/**
 * Turns the API's refusal into a sentence.
 *
 * @param error - What the call threw.
 * @returns The message.
 */
function messageOf(error: unknown): string {
  return error instanceof AdminActionError && error.code === "access_request.already_decided" ? adminCopy.requests.alreadyDecided : adminCopy.requests.failed;
}

/**
 * The player's name to show.
 *
 * @param player - The requested player.
 * @param player.name - The name, if it is known.
 * @returns The name, or a dash.
 */
function nameOf(player: { name: string | null }): string {
  return player.name ?? "—";
}

/**
 * One access request (A-12, canvas `AdminRequests`, decisions §7 P-4): who asked, for which player, how their email was
 * proven and what they wrote (shown as plain text). A pending request has the approval form: the players to grant (the
 * requested one by default, more by search, up to twenty), the expiry (7, 30 or 90 days, never "no expiry"), a note,
 * then "Approve & email link" or "Decline", which emails nobody. A decided request shows the outcome instead.
 *
 * @param props - See {@link RequestDetailProps}.
 * @returns The detail panel.
 */
export function RequestDetail({ request, defaultExpiryDays, onApprove, onDecline, onSearchPlayers, nextHref }: RequestDetailProps) {
  const copy = adminCopy.requests;
  const requested: GrantablePlayer = { id: request.player.id, name: nameOf(request.player) };
  const [current, setCurrent] = useState<AccessRequestRow>(request);
  const [options, setOptions] = useState<GrantablePlayer[]>([requested]);
  const [chosen, setChosen] = useState<string[]>([requested.id]);
  const [days, setDays] = useState<number>((LINK_EXPIRY_CHOICES as readonly number[]).includes(defaultExpiryDays) ? defaultExpiryDays : 30);
  const [note, setNote] = useState("");
  const [search, setSearch] = useState("");
  const [found, setFound] = useState<GrantablePlayer[] | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  /**
   * Turns a player on or off.
   *
   * @param id - The player.
   */
  const toggle = (id: string) => setChosen((ids) => (ids.includes(id) ? ids.filter((item) => item !== id) : [...ids, id]));

  /**
   * Adds a found player and grants them.
   *
   * @param player - The player.
   */
  const add = (player: GrantablePlayer) => {
    setOptions((items) => (items.some((item) => item.id === player.id) ? items : [...items, player]));
    setChosen((ids) => (ids.includes(player.id) ? ids : [...ids, player.id]));
  };

  /** Looks for published players by the typed name. */
  const find = async () => {
    const text = search.trim();
    if (!text) {
      return;
    }

    try {
      setFound(await onSearchPlayers(text));
    } catch {
      setFound([]);
    }
  };

  /**
   * Runs the decision and shows its outcome.
   *
   * @param action - Approve or decline.
   */
  const decide = async (action: () => Promise<AccessRequestRow>) => {
    setBusy(true);
    setError(null);
    try {
      setCurrent(await action());
    } catch (caught) {
      setError(messageOf(caught));
    } finally {
      setBusy(false);
    }
  };

  const pending = current.status === "Pending";
  const decidedHere = request.status === "Pending" && !pending;
  const next = decidedHere && nextHref ? (
    <a className="adm-btn adm-btn-small" href={nextHref} style={{ marginLeft: "auto" }}>
      {copy.nextRequest}
    </a>
  ) : null;
  const ids = options.filter((player) => chosen.includes(player.id)).map((player) => player.id);
  const extra = (found ?? []).filter((player) => !options.some((item) => item.id === player.id));
  return (
    <section className="adm-card adm-req-detail" aria-label={copy.detail}>
      <div className="adm-req-who">
        <span className="adm-req-avatar" aria-hidden="true">
          {requesterInitials(current.email)}
        </span>
        <div style={{ flex: "1 1 260px", minWidth: 0 }}>
          <div className="adm-req-email">{current.email}</div>
          <div className="adm-muted" style={{ fontSize: 13, marginTop: 4 }}>
            {copy.proof[current.source] ?? current.source} · {formatDate(current.createdAt)}
          </div>
        </div>
        <StatusBadge status={current.status} />
      </div>

      <div className="adm-req-facts">
        <div className="adm-req-fact">
          <div className="adm-label">{copy.requestedPlayer}</div>
          <div className="adm-name" style={{ fontSize: 26, marginTop: 6 }}>
            {requested.name}
          </div>
          <a href={`/admin/players/${encodeURIComponent(current.player.id)}`} style={{ fontSize: 13 }}>
            {copy.openPlayer}
          </a>
        </div>
      </div>

      <div className="adm-label" style={{ marginTop: 20 }}>
        {copy.message}
      </div>
      <blockquote className="adm-req-message">{current.about}</blockquote>

      {pending ? (
        <div>
          <div className="adm-label" id={`grant-${current.id}`} style={{ marginTop: 22 }}>
            {copy.grant}
          </div>
          <div className="adm-chips" role="group" aria-labelledby={`grant-${current.id}`} style={{ marginTop: 8 }}>
            {options.map((player) => (
              <button key={player.id} type="button" className="adm-chip" aria-pressed={chosen.includes(player.id)} onClick={() => toggle(player.id)}>
                {player.name}
              </button>
            ))}
          </div>
          <label htmlFor={`find-${current.id}`} style={{ marginTop: 12 }}>
            {copy.addPlayer}
          </label>
          <div className="adm-inline">
            <input
              id={`find-${current.id}`}
              className="adm-input"
              value={search}
              maxLength={100}
              autoComplete="off"
              onChange={(event) => setSearch(event.target.value)}
              onKeyDown={(event) => {
                if (event.key === "Enter") {
                  event.preventDefault();
                  void find();
                }
              }}
            />
            <button type="button" className="adm-btn adm-btn-small" onClick={() => void find()}>
              {copy.find}
            </button>
          </div>
          {found ? (
            <div className="adm-chips" style={{ marginTop: 8 }}>
              {extra.map((player) => (
                <button key={player.id} type="button" className="adm-chip" disabled={ids.length >= MAX_GRANTED_PLAYERS} onClick={() => add(player)}>
                  {copy.add(player.name)}
                </button>
              ))}
              {extra.length === 0 ? <span className="adm-muted">{copy.noMatch}</span> : null}
            </div>
          ) : null}

          <div className="adm-label" id={`expires-${current.id}`} style={{ marginTop: 16 }}>
            {copy.expires}
          </div>
          <div className="adm-chips" role="group" aria-labelledby={`expires-${current.id}`} style={{ marginTop: 8 }}>
            {LINK_EXPIRY_CHOICES.map((choice) => (
              <button key={choice} type="button" className="adm-chip" aria-pressed={days === choice} onClick={() => setDays(choice)}>
                {copy.days(choice)}
              </button>
            ))}
          </div>
          <label htmlFor={`note-${current.id}`} style={{ marginTop: 16 }}>
            {copy.note}
          </label>
          <textarea id={`note-${current.id}`} className="adm-input" rows={2} maxLength={500} value={note} onChange={(event) => setNote(event.target.value)} />
          {error ? (
            <p role="alert" className="adm-error">
              {error}
            </p>
          ) : null}
          <div className="adm-inline" style={{ marginTop: 18, flexWrap: "wrap" }}>
            <button
              type="button"
              className="adm-btn adm-btn-red"
              disabled={busy || ids.length === 0}
              onClick={() => void decide(() => onApprove(buildApproval({ playerIds: ids, expiresInDays: days, note })))}
            >
              {copy.approve}
            </button>
            <button type="button" className="adm-btn" disabled={busy} onClick={() => void decide(onDecline)}>
              {copy.decline}
            </button>
            <span className="adm-muted" style={{ fontSize: 12 }}>
              {copy.approveHint}
            </span>
          </div>
        </div>
      ) : current.status === "Approved" ? (
        <div role="status" className="adm-req-outcome adm-req-outcome-ok">
          <span className="adm-done-mark" aria-hidden="true">
            ✓
          </span>
          <span>{copy.approved(current.decidedBy, current.shareLink?.path ?? "—", formatDate(current.shareLink?.expiresAt ?? null))}</span>
          {next}
        </div>
      ) : (
        <div role="status" className="adm-req-outcome adm-req-outcome-no">
          <span>{copy.declined(current.decidedBy)}</span>
          {next}
        </div>
      )}
    </section>
  );
}
