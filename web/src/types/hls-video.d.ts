import type { DetailedHTMLProps, HTMLAttributes, Ref } from "react";

/** JSX typing for the `<hls-video>` custom element registered by hls-video-element (V-3). */
declare module "react" {
  namespace JSX {
    interface IntrinsicElements {
      "hls-video": DetailedHTMLProps<HTMLAttributes<HTMLElement>, HTMLElement> & {
        /** Where the element is placed inside Media Chrome's controller. */
        slot?: string;
        /** A still image shown before playback starts. */
        poster?: string;
        /** Callback ref to the element. */
        ref?: Ref<never>;
      };
    }
  }
}
