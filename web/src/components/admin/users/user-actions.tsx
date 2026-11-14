"use client";

import { useRouter } from "next/navigation";
import { useEffect, useState } from "react";
import { adminCall, AdminActionError } from "@/lib/admin/client";
import type { UserRow } from "@/lib/admin/types";
import { adminCopy } from "../copy";

/** Props of {@link UserActions}. */
type UserActionsProps = {
  /** The member. */
  user: UserRow;
  /** The active admins who can take over a removed member's players. */
  admins: { id: string; name: string }[];
};

/**
 * Turns the API's refusal into a sentence.
 *
 * @param error - What the call threw.
 * @returns The message.
 */
function messageOf(error: unknown): string {
  const code = error instanceof AdminActionError ? error.code : null;
  if (code === "user.last_admin") {
    return adminCopy.users.lastAdmin;
  }

  return code === "user.admin_needs_email" ? adminCopy.users.needsEmail : adminCopy.users.failed;
}

/**
 * The buttons of one team member (A-8, canvas `AdminUsers`): Approve as Analyst and Decline for a pending request; Edit
 * (email and role, which promotes an analyst once they have an email) and Remove (handing their players to an admin)
 * for an active member. Every change is audited by the API.
 *
 * @param props - See {@link UserActionsProps}.
 * @returns The buttons, and a dialog while one is open.
 */
export function UserActions({ user, admins }: UserActionsProps) {
  const router = useRouter();
  const [mode, setMode] = useState<"edit" | "remove" | null>(null);
  const [email, setEmail] = useState("");
  const [role, setRole] = useState<string>(user.role);
  const [reassign, setReassign] = useState(admins.find((admin) => admin.id !== user.id)?.id ?? "");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const copy = adminCopy.users;

  useEffect(() => {
    if (!mode) {
      return;
    }

    /**
     * Escape closes the dialog.
     *
     * @param event - The key press.
     */
    const onEscape = (event: KeyboardEvent) => {
      if (event.key === "Escape") {
        setMode(null);
      }
    };
    window.addEventListener("keydown", onEscape);
    return () => window.removeEventListener("keydown", onEscape);
  }, [mode]);

  /**
   * Runs one change, then refreshes the page.
   *
   * @param action - The API call.
   */
  const run = async (action: () => Promise<unknown>) => {
    setBusy(true);
    setError(null);
    try {
      await action();
      setMode(null);
      router.refresh();
    } catch (caught) {
      setError(messageOf(caught));
    } finally {
      setBusy(false);
    }
  };

  if (user.status === "Pending") {
    return (
      <span className="adm-inline">
        <button type="button" className="adm-btn adm-btn-small" disabled={busy} onClick={() => void run(() => adminCall("POST", `users/${user.id}/decline`, {}))}>
          {copy.decline}
        </button>
        <button type="button" className="adm-btn adm-btn-small adm-btn-red" disabled={busy} onClick={() => void run(() => adminCall("POST", `users/${user.id}/approve`, { role: "Analyst" }))}>
          {copy.approveAnalyst}
        </button>
        {error ? <span role="alert" className="adm-error">{error}</span> : null}
      </span>
    );
  }

  if (user.status !== "Active") {
    return null;
  }

  return (
    <>
      <span className="adm-inline">
        <button type="button" className="adm-btn adm-btn-small" onClick={() => setMode("edit")}>
          {copy.edit}
        </button>
        <button type="button" className="adm-btn adm-btn-small adm-btn-danger" onClick={() => setMode("remove")}>
          {copy.remove}
        </button>
      </span>
      {mode ? (
        <div className="adm-modal-layer">
          <div className="adm-scrim" onClick={() => setMode(null)} aria-hidden="true" />
          <div role="dialog" aria-modal="true" aria-labelledby={`user-${user.id}`} className="adm-modal">
            <div className="adm-modal-body">
              <h2 id={`user-${user.id}`}>{mode === "edit" ? user.displayName : copy.removeTitle(user.displayName)}</h2>
              {mode === "edit" ? (
                <>
                  <label htmlFor={`email-${user.id}`}>{copy.email}</label>
                  <input id={`email-${user.id}`} type="email" className="adm-input" value={email} placeholder={user.email ?? ""} onChange={(event) => setEmail(event.target.value)} autoComplete="off" />
                  <label htmlFor={`role-${user.id}`}>{copy.role}</label>
                  <select id={`role-${user.id}`} className="adm-input" value={role} onChange={(event) => setRole(event.target.value)}>
                    <option value="Analyst">Analyst</option>
                    <option value="Admin">Admin</option>
                  </select>
                </>
              ) : user.playerCount > 0 ? (
                <>
                  <label htmlFor={`reassign-${user.id}`}>{copy.reassign}</label>
                  <select id={`reassign-${user.id}`} className="adm-input" value={reassign} onChange={(event) => setReassign(event.target.value)}>
                    {admins
                      .filter((admin) => admin.id !== user.id)
                      .map((admin) => (
                        <option key={admin.id} value={admin.id}>
                          {admin.name}
                        </option>
                      ))}
                  </select>
                </>
              ) : null}
              {error ? (
                <p role="alert" className="adm-error">
                  {error}
                </p>
              ) : null}
              <div className="adm-actions">
                <button type="button" className="adm-btn" onClick={() => setMode(null)}>
                  {copy.cancel}
                </button>
                <button
                  type="button"
                  className="adm-btn adm-btn-red"
                  disabled={busy}
                  onClick={() =>
                    void run(() =>
                      mode === "edit"
                        ? adminCall("PATCH", `users/${user.id}`, { email: email.trim() || undefined, role: role === user.role ? undefined : role })
                        : adminCall("POST", `users/${user.id}/remove`, user.playerCount > 0 ? { reassignTo: reassign } : {}),
                    )
                  }
                >
                  {mode === "edit" ? copy.save : copy.remove}
                </button>
              </div>
            </div>
          </div>
        </div>
      ) : null}
    </>
  );
}
