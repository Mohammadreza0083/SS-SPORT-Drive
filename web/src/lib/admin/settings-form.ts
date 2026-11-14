/**
 * The settings form checks its fields before saving (A-10). The API checks again; this is only for a quick message.
 * Every link expires (decisions §1b): the default is 7, 30 or 90 days, never "Never".
 */

/** The expiry choices, in days. */
export const LINK_EXPIRY_CHOICES = [7, 30, 90] as const;

/** The settings as the form holds them. */
export type SettingsValues = {
  contactEmail: string;
  digestAt: string;
  quietHoursStart: string;
  quietHoursEnd: string;
  defaultLinkExpiryDays: number;
};

/** Field name → message, for the fields that are not valid. */
export type SettingsErrors = Partial<Record<keyof SettingsValues, string>>;

/** A time as `HH:mm`. */
const TIME = /^([01]\d|2[0-3]):[0-5]\d$/;

/** An ordinary email address. */
const EMAIL = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

/**
 * Checks the settings form.
 *
 * @param values - The form's values.
 * @returns The messages of the fields that are not valid; empty when all are.
 */
export function validateSettings(values: SettingsValues): SettingsErrors {
  const errors: SettingsErrors = {};
  if (!EMAIL.test(values.contactEmail.trim()) || values.contactEmail.length > 254) {
    errors.contactEmail = "Use a valid email address.";
  }

  for (const field of ["digestAt", "quietHoursStart", "quietHoursEnd"] as const) {
    if (!TIME.test(values[field])) {
      errors[field] = "Use a time as HH:mm.";
    }
  }

  if (!(LINK_EXPIRY_CHOICES as readonly number[]).includes(values.defaultLinkExpiryDays)) {
    errors.defaultLinkExpiryDays = "Use 7, 30 or 90 days.";
  }

  return errors;
}
