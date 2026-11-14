import { TrackingRequestError } from "./tracking-client";

/**
 * The player's side of watch tracking (backlog W-2, the client half of L-5). The tracker is pure: the player hook feeds
 * it the media element's events and calls {@link WatchTracker.tick} every heartbeat interval; it decides what to send.
 * Everything goes out in order through one promise chain, and tracking never breaks playback: a failed start, a 401 or
 * a 404 turns it off, any other failure is dropped.
 */

/** The longest range one heartbeat may report; the API's cap. */
export const MAX_HEARTBEAT_SECONDS = 30;

/** The heartbeat interval used until the API says another. */
const DEFAULT_HEARTBEAT_SECONDS = 10;

/** What starting a watch answers. */
export type WatchStarted = {
  /** The watch id to send with every heartbeat and event. */
  watchId: string;
  /** How often to send a heartbeat, in seconds. */
  heartbeatSeconds: number;
};

/** What the tracker sends through; the browser implementation is in `tracking-client.ts`. */
export type WatchTransport = {
  /** Starts a watch (one play). */
  start: () => Promise<WatchStarted>;
  /** Sends the range played since the last heartbeat. */
  heartbeat: (watchId: string, from: number, to: number) => Promise<void>;
  /** Reports a player event (`Play`, `Pause`, `Seek` or `Ended`) at a position. */
  event: (watchId: string, type: string, position: number) => Promise<void>;
};

/** Decides what the player reports about one video's watch, in order. */
export class WatchTracker {
  /** Every send is chained here so nothing overtakes the start. */
  private queue: Promise<void> = Promise.resolve();
  /** The watch id once the start has answered. */
  private watchId: string | null = null;
  /** True after the first play has been asked for. */
  private started = false;
  /** True while the video is playing. */
  private playing = false;
  /** Where the open range starts. */
  private segmentStart = 0;
  /** The last position seen on the video's clock. */
  private lastPosition = 0;
  /** The heartbeat interval in seconds. */
  private interval = DEFAULT_HEARTBEAT_SECONDS;
  /** True once tracking has been turned off. */
  private off = false;

  /**
   * @param transport - Where the requests go.
   */
  constructor(private readonly transport: WatchTransport) {}

  /** True once tracking is off (a failed start, a 401 or 404, or the video was left). */
  get stopped(): boolean {
    return this.off;
  }

  /** How often to call {@link WatchTracker.tick}, in seconds. */
  get heartbeatSeconds(): number {
    return this.interval;
  }

  /**
   * The video started or resumed playing. The first time this starts the watch; afterwards it sends `Play`.
   *
   * @param position - Where in the video it is, in seconds.
   * @returns A promise that never rejects and settles when the request has been made.
   */
  play(position: number): Promise<void> {
    const first = !this.started;
    this.started = true;
    this.playing = true;
    this.segmentStart = position;
    this.lastPosition = position;
    // The start goes out at once; everything after it waits in the chain until it has answered.
    const start = first ? this.transport.start() : null;
    return this.enqueue(async () => {
      if (start) {
        const started = await start;
        this.watchId = started.watchId;
        this.interval = started.heartbeatSeconds > 0 ? started.heartbeatSeconds : DEFAULT_HEARTBEAT_SECONDS;
        return;
      }
      await this.sendEvent("Play", position);
    }, first);
  }

  /**
   * The video's clock moved.
   *
   * @param position - The position in seconds.
   */
  timeUpdate(position: number): void {
    if (this.playing) {
      this.lastPosition = position;
    }
  }

  /**
   * A heartbeat is due: sends the range played since the last one.
   *
   * @returns A promise that never rejects.
   */
  tick(): Promise<void> {
    if (!this.playing) {
      return this.queue;
    }
    return this.enqueue(() => this.flush());
  }

  /**
   * The video was paused.
   *
   * @param position - Where it paused, in seconds.
   * @returns A promise that never rejects.
   */
  pause(position: number): Promise<void> {
    if (!this.started) {
      return this.queue;
    }
    const wasPlaying = this.playing;
    this.playing = false;
    return this.enqueue(async () => {
      if (wasPlaying) {
        await this.flush();
      }
      await this.sendEvent("Pause", position);
    });
  }

  /**
   * The viewer jumped to another position.
   *
   * @param position - The new position, in seconds.
   * @returns A promise that never rejects.
   */
  seeked(position: number): Promise<void> {
    if (!this.started) {
      return this.queue;
    }
    const wasPlaying = this.playing;
    const closeAt = this.lastPosition;
    const from = this.segmentStart;
    this.segmentStart = position;
    this.lastPosition = position;
    return this.enqueue(async () => {
      if (wasPlaying) {
        await this.sendRange(from, closeAt);
      }
      await this.sendEvent("Seek", position);
    });
  }

  /**
   * The video played to its end.
   *
   * @param position - The end position, in seconds.
   * @returns A promise that never rejects.
   */
  ended(position: number): Promise<void> {
    if (!this.started) {
      return this.queue;
    }
    const wasPlaying = this.playing;
    this.playing = false;
    return this.enqueue(async () => {
      if (wasPlaying) {
        await this.flush();
      }
      await this.sendEvent("Ended", position);
    });
  }

  /**
   * The video was left: sends the open range, then nothing more.
   *
   * @returns A promise that never rejects.
   */
  dispose(): Promise<void> {
    const wasPlaying = this.playing;
    this.playing = false;
    const from = this.segmentStart;
    const to = this.lastPosition;
    this.segmentStart = to;
    const done = this.enqueue(async () => {
      if (wasPlaying) {
        await this.sendRange(from, to);
      }
    });
    return done.then(() => {
      this.off = true;
    });
  }

  /**
   * Chains one step after the previous ones, never rejecting. A failed start turns tracking off.
   *
   * @param step - The step to run.
   * @param isStart - True for the step that starts the watch.
   * @returns The chain.
   */
  private enqueue(step: () => Promise<void>, isStart = false): Promise<void> {
    this.queue = this.queue.then(async () => {
      if (this.off) {
        return;
      }
      try {
        await step();
      } catch (error) {
        if (isStart || (error instanceof TrackingRequestError && (error.status === 401 || error.status === 404))) {
          this.off = true;
        }
      }
    });
    return this.queue;
  }

  /** Sends the open range up to the last position seen and opens the next one there. */
  private flush(): Promise<void> {
    const from = this.segmentStart;
    const to = this.lastPosition;
    this.segmentStart = to;
    return this.sendRange(from, to);
  }

  /**
   * Sends one range as heartbeats of at most {@link MAX_HEARTBEAT_SECONDS}. A range that fails is dropped.
   *
   * @param from - The range start.
   * @param to - The range end.
   */
  private async sendRange(from: number, to: number): Promise<void> {
    if (this.watchId === null) {
      return;
    }
    for (let at = from; at < to; at += MAX_HEARTBEAT_SECONDS) {
      const end = Math.min(at + MAX_HEARTBEAT_SECONDS, to);
      try {
        await this.transport.heartbeat(this.watchId, at, end);
      } catch (error) {
        if (error instanceof TrackingRequestError && (error.status === 401 || error.status === 404)) {
          this.off = true;
          return;
        }
      }
    }
  }

  /**
   * Sends one player event.
   *
   * @param type - The event type.
   * @param position - Where it happened.
   */
  private async sendEvent(type: string, position: number): Promise<void> {
    if (this.watchId !== null) {
      await this.transport.event(this.watchId, type, position);
    }
  }
}
