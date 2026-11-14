"use client";

import { useRouter } from "next/navigation";
import { useState, type FormEvent } from "react";
import { adminCall } from "@/lib/admin/client";
import { LINK_EXPIRY_CHOICES, validateSettings, type SettingsErrors, type SettingsValues } from "@/lib/admin/settings-form";
import { adminCopy } from "../copy";

/** Props of {@link SettingsForm}. */
type SettingsFormProps = {
  /** The stored settings. */
  initial: SettingsValues;
};

/**
 * The editable settings (A-10, canvas `AdminSettings`): the contact email, the default link expiry (7, 30 or 90 days,
 * never "Never"), the daily digest time and the quiet hours. It checks the fields, saves through the console's route and
 * shows the API's answer; the read-only rows of the canvas are on the page around it.
 *
 * @param props - See {@link SettingsFormProps}.
 * @returns The form.
 */
export function SettingsForm({ initial }: SettingsFormProps) {
  const router = useRouter();
  const [values, setValues] = useState<SettingsValues>(initial);
  const [errors, setErrors] = useState<SettingsErrors>({});
  const [state, setState] = useState<"idle" | "saving" | "saved" | "failed">("idle");
  const copy = adminCopy.settings;

  /**
   * Changes one field.
   *
   * @param patch - The new value.
   */
  const change = (patch: Partial<SettingsValues>) => {
    setValues((current) => ({ ...current, ...patch }));
    setState("idle");
  };

  /**
   * Saves the settings.
   *
   * @param event - The form submit.
   */
  const save = async (event: FormEvent) => {
    event.preventDefault();
    const found = validateSettings(values);
    setErrors(found);
    if (Object.keys(found).length > 0) {
      return;
    }

    setState("saving");
    try {
      await adminCall("PUT", "settings", { ...values, contactEmail: values.contactEmail.trim() });
      setState("saved");
      router.refresh();
    } catch {
      setState("failed");
    }
  };

  return (
    <form onSubmit={(event) => void save(event)} className="adm-card" aria-label={copy.title}>
      <h2 className="adm-label">{copy.contact}</h2>
      <label htmlFor="s-mail">{copy.contactEmail}</label>
      <input id="s-mail" type="email" className="adm-input" value={values.contactEmail} onChange={(event) => change({ contactEmail: event.target.value })} aria-invalid={Boolean(errors.contactEmail)} />
      {errors.contactEmail ? <p className="adm-error">{errors.contactEmail}</p> : <span className="adm-hint">{copy.contactHint}</span>}

      <h2 className="adm-label" style={{ marginTop: 22 }}>
        {copy.links}
      </h2>
      <div className="adm-label" style={{ marginTop: 10 }}>
        {copy.defaultExpiry}
      </div>
      <div className="adm-chips">
        {LINK_EXPIRY_CHOICES.map((days) => (
          <button key={days} type="button" className="adm-chip" aria-pressed={values.defaultLinkExpiryDays === days} onClick={() => change({ defaultLinkExpiryDays: days })}>
            {adminCopy.linkModal.days(days)}
          </button>
        ))}
      </div>

      <h2 className="adm-label" style={{ marginTop: 22 }}>
        {copy.notifications}
      </h2>
      <div className="adm-row2">
        <div>
          <label htmlFor="s-digest">{copy.digestAt}</label>
          <input id="s-digest" type="time" className="adm-input" value={values.digestAt} onChange={(event) => change({ digestAt: event.target.value })} aria-invalid={Boolean(errors.digestAt)} />
        </div>
        <div />
        <div>
          <label htmlFor="s-quiet-start">{copy.quietStart}</label>
          <input id="s-quiet-start" type="time" className="adm-input" value={values.quietHoursStart} onChange={(event) => change({ quietHoursStart: event.target.value })} aria-invalid={Boolean(errors.quietHoursStart)} />
        </div>
        <div>
          <label htmlFor="s-quiet-end">{copy.quietEnd}</label>
          <input id="s-quiet-end" type="time" className="adm-input" value={values.quietHoursEnd} onChange={(event) => change({ quietHoursEnd: event.target.value })} aria-invalid={Boolean(errors.quietHoursEnd)} />
        </div>
      </div>
      {errors.digestAt || errors.quietHoursStart || errors.quietHoursEnd ? <p className="adm-error">{errors.digestAt ?? errors.quietHoursStart ?? errors.quietHoursEnd}</p> : null}

      {state === "failed" ? (
        <p role="alert" className="adm-error">
          {copy.failed}
        </p>
      ) : null}
      <div className="adm-actions">
        <button type="submit" className="adm-btn adm-btn-red" disabled={state === "saving"}>
          {state === "saving" ? copy.saving : state === "saved" ? copy.saved : copy.save}
        </button>
      </div>
    </form>
  );
}
