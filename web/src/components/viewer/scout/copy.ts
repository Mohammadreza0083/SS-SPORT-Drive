/** UI copy of the Match stats view (V-8; English, kept out of the components). */
export const scoutCopy = {
  section: "Match stats",
  open: "View",
  viewOnly: "View only",
  dialogLabel: (title: string) => `${title}, match stats`,
  close: "Close match stats",
  tableLabel: (name: string) => `${name}'s stats per skill`,
  columns: { skill: "Skill", total: "Total", perfect: "Perfect", errors: "Errors", success: "Success", efficiency: "Efficiency" },
  notInFile: (name: string) => `${name}'s actions are not in this scout file.`,
  playerLine: (number: number, team: string) => `#${number} · ${team}`,
  unreadable: "This scout file could not be read.",
  unavailable: "These match stats cannot be opened right now.",
  protectedShort: "Protected match stats · view only",
} as const;
