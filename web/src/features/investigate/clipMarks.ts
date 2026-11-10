import { defineMachine } from '../../shared/stateMachine';

/** The marked stretch: in and out points (ms since the epoch). */
export interface ClipMarks {
  from: number | null;
  to: number | null;
}

/** Where marking a clip stands. */
export type MarkPhase = 'none' | 'in' | 'out' | 'clip';

export const noMarks: ClipMarks = { from: null, to: null };

/** Marking a clip (docs/STATE-MACHINES.md): a mark on the wrong side of the other one replaces it. */
export const markMachine = defineMachine<MarkPhase>('Clip marks', 'none', [
  { from: 'none', to: 'in', when: 'Mark in' },
  { from: 'none', to: 'out', when: 'Mark out' },
  { from: 'in', to: 'in', when: 'Mark in again' },
  { from: 'in', to: 'clip', when: 'Mark out after the in point' },
  { from: 'in', to: 'out', when: 'Mark out before the in point' },
  { from: 'out', to: 'out', when: 'Mark out again' },
  { from: 'out', to: 'clip', when: 'Mark in before the out point' },
  { from: 'out', to: 'in', when: 'Mark in after the out point' },
  { from: 'clip', to: 'clip', when: 'move a mark, still in order' },
  { from: 'clip', to: 'in', when: 'Mark in after the out point' },
  { from: 'clip', to: 'out', when: 'Mark out before the in point' },
  { from: 'in', to: 'none', when: 'Clear' },
  { from: 'out', to: 'none', when: 'Clear' },
  { from: 'clip', to: 'none', when: 'Clear' },
]);

export function phaseOf(marks: ClipMarks): MarkPhase {
  if (marks.from !== null && marks.to !== null) return marks.to > marks.from ? 'clip' : 'out';
  return marks.from !== null ? 'in' : marks.to !== null ? 'out' : 'none';
}

const moved = (marks: ClipMarks, next: ClipMarks): ClipMarks => {
  markMachine.move(phaseOf(marks), phaseOf(next));
  return next;
};

/** In at `now`; an out point before it is dropped. */
export const markIn = (marks: ClipMarks, now: number): ClipMarks => moved(marks, { from: now, to: marks.to !== null && marks.to > now ? marks.to : null });

/** Out at `now`; an in point after it is dropped. */
export const markOut = (marks: ClipMarks, now: number): ClipMarks => moved(marks, { from: marks.from !== null && marks.from < now ? marks.from : null, to: now });

export const clearMarks = (marks: ClipMarks): ClipMarks => moved(marks, noMarks);

/** The marked clip, once both ends are set in order. */
export function clipOf(marks: ClipMarks): { from: number; to: number } | null {
  return phaseOf(marks) === 'clip' ? { from: marks.from!, to: marks.to! } : null;
}
