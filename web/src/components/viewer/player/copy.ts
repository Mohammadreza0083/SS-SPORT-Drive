/** UI copy of the video player (English; kept out of the component). */
export const playerCopy = {
  region: (title: string) => `Video player: ${title}`,
  frameByFrame: "Frame by frame",
  previousFrame: "Previous frame",
  nextFrame: "Next frame",
  protectedStream: "Protected stream · no downloads",
  unsupported: "This browser cannot play the video. Please open the page in a current version of Chrome, Safari, Edge or Firefox.",
} as const;
