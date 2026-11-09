/** A piece of the recordings playlist: where it sits in the video and on the wall clock. */
export interface Fragment {
  /** Seconds from the start of the playlist. */
  start: number;
  duration: number;
  /** Wall-clock time of its first frame (EXT-X-PROGRAM-DATE-TIME), ms since the epoch. */
  programDateTime: number;
}

/** The wall-clock time shown at video time `time`, or null outside the fragments. */
export function dateAt(fragments: readonly Fragment[], time: number): number | null {
  for (const fragment of fragments) {
    if (time >= fragment.start && time < fragment.start + fragment.duration) {
      return fragment.programDateTime + (time - fragment.start) * 1000;
    }
  }
  const last = fragments[fragments.length - 1];
  // The very end of the last fragment.
  return last && Math.abs(time - (last.start + last.duration)) < 0.05 ? last.programDateTime + last.duration * 1000 : null;
}

/**
 * The video time showing wall-clock time `date`: inside a fragment, or the start of the next one when `date` falls in
 * a gap between recordings; null when it is before the first or after the last fragment.
 */
export function timeAt(fragments: readonly Fragment[], date: number): number | null {
  for (const fragment of fragments) {
    const end = fragment.programDateTime + fragment.duration * 1000;
    if (date < fragment.programDateTime) {
      // In a gap (or before the start): the next recorded moment, unless it is before everything.
      return fragment === fragments[0] ? null : fragment.start;
    }
    if (date < end) return fragment.start + (date - fragment.programDateTime) / 1000;
  }
  return null;
}
