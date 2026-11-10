/** One allowed move, and what makes it happen. */
export interface Transition<S extends string> {
  from: S;
  to: S;
  when: string;
}

/**
 * A state machine written down: its states, where it starts, and every allowed move. Code moves with `move` (which
 * refuses a move that is not in the table), tests check the code against it, and docs/STATE-MACHINES.md shows
 * `toMermaid()` (a test keeps it current). The backend has the same helper (Onvif.Open.Core.StateMachines).
 */
export interface StateMachine<S extends string> {
  name: string;
  initial: S;
  /** In the order they first appear: the initial state first. */
  states: readonly S[];
  transitions: readonly Transition<S>[];
  /** Where it can go from `state`. */
  next(state: S): S[];
  can(from: S, to: S): boolean;
  /** `to`, when the move is allowed; otherwise throws, naming the moves that are. */
  move(from: S, to: S): S;
  toMermaid(): string;
}

export function defineMachine<S extends string>(name: string, initial: S, transitions: readonly Transition<S>[]): StateMachine<S> {
  const states: S[] = [initial];
  for (const { from, to } of transitions) {
    for (const state of [from, to]) if (!states.includes(state)) states.push(state);
  }
  const next = (state: S) => transitions.filter((t) => t.from === state).map((t) => t.to);
  const can = (from: S, to: S) => next(from).includes(to);

  return {
    name,
    initial,
    states,
    transitions,
    next,
    can,
    move(from, to) {
      if (can(from, to)) return to;
      const allowed = next(from);
      throw new Error(allowed.length === 0 ? `${name}: ${from} is final; it cannot become ${to}.` : `${name}: ${from} cannot become ${to}; it can become ${allowed.join(' or ')}.`);
    },
    toMermaid() {
      const lines = ['stateDiagram-v2', `    [*] --> ${initial}`];
      for (const { from, to, when } of transitions) lines.push(`    ${from} --> ${to}${when.trim() ? ` : ${when}` : ''}`);
      for (const state of states) if (next(state).length === 0) lines.push(`    ${state} --> [*]`);
      return lines.join('\n') + '\n';
    },
  };
}
