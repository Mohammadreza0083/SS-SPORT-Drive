import { describe, expect, it } from "vitest";
import { LINK_EXPIRY_CHOICES, validateSettings } from "./settings-form";

/**
 * The settings form checks its fields before saving (A-10). The API checks again; this is only for a quick message.
 * Every link expires (decisions §1b): the default is 7, 30 or 90 days, never "Never".
 */

const valid = { contactEmail: "info@ss-sport.co", digestAt: "09:00", quietHoursStart: "23:00", quietHoursEnd: "08:00", defaultLinkExpiryDays: 30 };

describe("validateSettings", () => {
  it("accepts a valid form", () => {
    expect(validateSettings(valid)).toEqual({});
  });

  it("names each bad field", () => {
    expect(validateSettings({ ...valid, contactEmail: "info", digestAt: "9:00", quietHoursStart: "24:00", quietHoursEnd: "", defaultLinkExpiryDays: 60 })).toEqual({
      contactEmail: expect.any(String),
      digestAt: expect.any(String),
      quietHoursStart: expect.any(String),
      quietHoursEnd: expect.any(String),
      defaultLinkExpiryDays: expect.any(String),
    });
  });

  it("offers only 7, 30 and 90 days", () => {
    expect(LINK_EXPIRY_CHOICES).toEqual([7, 30, 90]);
  });
});
