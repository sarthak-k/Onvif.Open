/// <reference types="node" />
import { readFileSync, writeFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { afterSharedChange, candidateMachine, changeCandidate, fromAddress, startTest, withProbe } from '../features/cameras/wizard/candidates';
import { wizardSteps } from '../features/cameras/wizard/steps';
import { sessionMachine, sessionOf } from '../features/auth/session';
import { signInSteps } from '../features/auth/signInSteps';
import { twoFactorSetupSteps } from '../features/auth/twoFactorSetup';
import { clearMarks, clipOf, markIn, markMachine, markOut, noMarks, phaseOf } from '../features/investigate/clipMarks';
import { ptzHoldMachine } from '../features/live/ptz/ptzHold';
import { admin } from '../test/fakeRecorder';
import { defineMachine, type StateMachine } from './stateMachine';

/** Every web state machine; a new one is one line here (and a section in docs/STATE-MACHINES.md). */
const machines: StateMachine<string>[] = [sessionMachine, signInSteps, twoFactorSetupSteps, wizardSteps, candidateMachine, markMachine, ptzHoldMachine];

describe('state machines', () => {
  it('refuses moves outside the table and names the allowed ones', () => {
    const door = defineMachine<'shut' | 'open' | 'gone'>('Door', 'shut', [
      { from: 'shut', to: 'open', when: 'push' },
      { from: 'open', to: 'shut', when: 'pull' },
      { from: 'open', to: 'gone', when: 'kick' },
    ]);
    expect(door.move('shut', 'open')).toBe('open');
    expect(() => door.move('shut', 'gone')).toThrow('Door: shut cannot become gone; it can become open.');
    expect(() => door.move('gone', 'shut')).toThrow('Door: gone is final; it cannot become shut.');
    expect(door.toMermaid()).toBe('stateDiagram-v2\n    [*] --> shut\n    shut --> open : push\n    open --> shut : pull\n    open --> gone : kick\n    gone --> [*]\n');
  });

  it('tests a wizard camera again when its sign-in changes', () => {
    const camera = withProbe(startTest(fromAddress('10.0.0.5', [])!), { ok: true, recommended: { mainProfile: 'main' } });
    expect(camera.state).toBe('ok');

    // A new name keeps the test; a new password does not (it would be added with a password nobody tested).
    expect(changeCandidate(camera, { name: 'Gate' }).state).toBe('ok');
    expect(changeCandidate(camera, { ownCredentials: true, username: 'admin', password: 'new' }).state).toBe('untested');
    expect(changeCandidate(camera, { password: camera.password }).state).toBe('ok');

    // The shared sign-in: only cameras that use it.
    const own = { ...camera, key: 'own', ownCredentials: true };
    expect(afterSharedChange([camera, own]).map((c) => c.state)).toEqual(['untested', 'ok']);

    // Testing twice at once is not a move of the table.
    expect(() => startTest(startTest(camera))).toThrow('cannot become testing');
  });

  it('marks a clip in either order, and says what is missing', () => {
    expect(phaseOf(noMarks)).toBe('none');
    const out = markOut(noMarks, 2000);
    expect(phaseOf(out)).toBe('out');
    const clip = markIn(out, 1000);
    expect(clipOf(clip)).toEqual({ from: 1000, to: 2000 });
    // A mark on the wrong side of the other replaces it.
    expect(markIn(clip, 3000)).toEqual({ from: 3000, to: null });
    expect(markOut(clip, 500)).toEqual({ from: null, to: 500 });
    expect(clearMarks(clip)).toEqual(noMarks);
    expect(() => clearMarks(noMarks)).toThrow('Clip marks: none cannot become none');
  });

  it('names the session state', () => {
    expect(sessionOf(undefined)).toBe('resuming');
    expect(sessionOf(null)).toBe('signedOut');
    expect(sessionOf({ ...admin, mustChangePassword: true })).toBe('mustChangePassword');
    expect(sessionOf({ ...admin, mustChangePassword: false })).toBe('signedIn');
    expect(sessionOf({ ...admin, mustChangePassword: false, mustSetUpTwoFactor: true })).toBe('mustSetUpTwoFactor');
    expect(sessionOf({ ...admin, mustChangePassword: true, mustSetUpTwoFactor: true })).toBe('mustChangePassword');
    expect(sessionMachine.can('signedIn', 'mustChangePassword')).toBe(false);
  });

  it('keeps docs/STATE-MACHINES.md as the code has them (UPDATE_STATE_DOCS=1 rewrites it)', () => {
    const path = resolve(__dirname, '../../../docs/STATE-MACHINES.md');
    const doc = readFileSync(path, 'utf8').replace(/\r\n/g, '\n');
    let updated = doc;
    const missing: string[] = [];
    for (const machine of machines) {
      const escaped = machine.name.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
      const block = new RegExp(`(<!-- machine: ${escaped} -->\\n\`\`\`mermaid\\n)([\\s\\S]*?)(\`\`\`\\n<!-- /machine -->)`);
      if (!block.test(updated)) {
        missing.push(machine.name);
        continue;
      }
      updated = updated.replace(block, (_, open: string, _old: string, close: string) => open + machine.toMermaid() + close);
    }
    expect(missing, 'add a `<!-- machine: Name -->` section for each').toEqual([]);
    if (updated !== doc && process.env.UPDATE_STATE_DOCS === '1') {
      writeFileSync(path, updated);
      return;
    }
    expect(updated === doc, 'docs/STATE-MACHINES.md is out of date: run the tests with UPDATE_STATE_DOCS=1').toBe(true);
  });
});
