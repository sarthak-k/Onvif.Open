import { act, fireEvent, screen, waitFor, within } from '@testing-library/react';
import type { PlayerFactory } from '../player/types';
import { renderApp } from '../../../test/renderApp';
import { admin, tokens } from '../../../test/fakeRecorder';
import { createPtzHold, renewMs, safetyMs } from './ptzHold';

const players: PlayerFactory = () => ({ destroy: () => undefined });
const status = (cameraId: string, onvif: boolean) => ({ cameraId, name: cameraId, onvif, state: 'Recording', mode: 'Continuous', isTriggered: false, codec: 'H264', recordsAudio: false, framesRecorded: 0, bytesRecorded: 0, segmentsCompleted: 0 });

function routes(role: string, calls: { method: string; path: string; body: unknown }[]) {
  const record = (r: { method: string; path: string; body: unknown }) => {
    calls.push({ method: r.method, path: r.path, body: r.body });
    return { status: 204 };
  };
  return {
    'POST /api/auth/refresh': () => ({ body: tokens(1) }),
    'GET /api/auth/me': () => ({ body: { ...admin, role } }),
    'GET /api/cameras': () => ({ body: [status('dome', true), status('door', false), status('fixed', true)] }),
    'GET /api/layouts': () => ({ body: [] }),
    'GET /api/cameras/dome/ptz': () => ({ body: { position: { pan: 0, tilt: 0, zoom: 0 }, isMoving: false, error: null } }),
    'GET /api/cameras/fixed/ptz': () => ({ status: 501, body: { detail: 'Camera has no PTZ.' } }),
    'GET /api/cameras/dome/ptz/presets': () => ({ body: [{ token: 'p1', name: 'Gate', position: null }] }),
    'POST /api/cameras/dome/ptz/move': record,
    'POST /api/cameras/dome/ptz/stop': record,
    'POST /api/cameras/dome/ptz/home': record,
    'POST /api/cameras/dome/ptz/presets/p1/goto': record,
    'PUT /api/cameras/dome/ptz/presets/Loading%20bay': (r: { method: string; path: string; body: unknown }) => {
      calls.push({ method: r.method, path: r.path, body: r.body });
      return { body: { token: 'p2', name: 'Loading bay' } };
    },
  };
}

describe('hold to move', () => {
  it('renews a held move before it stops by itself, and stops on release', async () => {
    vi.useFakeTimers();
    const moves: { speed: unknown; ms: number }[] = [];
    const stop = vi.fn(async () => undefined);
    const hold = createPtzHold({ move: async (speed, ms) => void moves.push({ speed, ms }), stop }, () => undefined);

    hold.press({ pan: -0.5 });
    expect(hold.state).toBe('moving');
    expect(moves).toEqual([{ speed: { pan: -0.5 }, ms: safetyMs }]);
    expect(renewMs).toBeLessThan(safetyMs);
    vi.advanceTimersByTime(renewMs * 2);
    expect(moves).toHaveLength(3);

    hold.release();
    expect(hold.state).toBe('idle');
    expect(stop).toHaveBeenCalledTimes(1);
    vi.advanceTimersByTime(renewMs * 3);
    expect(moves).toHaveLength(3);
    // Releasing again sends nothing.
    hold.release();
    expect(stop).toHaveBeenCalledTimes(1);
    vi.useRealTimers();
  });
});

describe('PTZ panel', () => {
  it('lets an operator steer an enlarged camera, use presets and go home', async () => {
    const calls: { method: string; path: string; body: unknown }[] = [];
    const { user, client } = renderApp('/live', routes('Operator', calls), 'refresh-0', players);

    // Only once enlarged, and only for ONVIF cameras.
    await screen.findByRole('region', { name: 'Tile 1: dome' });
    expect(screen.queryByRole('button', { name: /Pan, tilt and zoom/ })).not.toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Enlarge dome' }));
    await user.click(screen.getByRole('button', { name: 'Pan, tilt and zoom dome' }));
    const panel = await screen.findByRole('group', { name: 'Pan, tilt and zoom of dome' });

    // Hold to move (at the chosen speed), release to stop.
    fireEvent.pointerDown(within(panel).getByRole('button', { name: 'Pan left' }), { pointerId: 1 });
    fireEvent.pointerUp(within(panel).getByRole('button', { name: 'Pan left' }), { pointerId: 1 });
    await waitFor(() => expect(calls.map((c) => c.path)).toEqual(['/api/cameras/dome/ptz/move', '/api/cameras/dome/ptz/stop']));
    expect(calls[0].body).toEqual({ pan: -0.5, tilt: 0, zoom: 0, durationMs: 1500 });

    // Keyboard: arrows and + / - while the panel has focus.
    await user.selectOptions(within(panel).getByLabelText('Speed'), 'Fast');
    act(() => panel.focus());
    fireEvent.keyDown(panel, { key: '+' });
    fireEvent.keyUp(panel, { key: '+' });
    await waitFor(() => expect(calls.slice(2).map((c) => c.body)).toEqual([{ pan: 0, tilt: 0, zoom: 1, durationMs: 1500 }, {}]));

    // Presets and home.
    await user.selectOptions(within(panel).getByLabelText('Preset'), 'p1');
    await user.click(within(panel).getByRole('button', { name: 'Go' }));
    await user.type(within(panel).getByLabelText('New preset name'), 'Loading bay');
    await user.click(within(panel).getByRole('button', { name: 'Save' }));
    await user.click(within(panel).getByRole('button', { name: 'Home position' }));
    await waitFor(() =>
      expect(calls.slice(4).map((c) => `${c.method} ${c.path}`)).toEqual([
        'POST /api/cameras/dome/ptz/presets/p1/goto',
        'PUT /api/cameras/dome/ptz/presets/Loading%20bay',
        'POST /api/cameras/dome/ptz/home',
      ]),
    );

    // Hiding the panel while a move is held stops it.
    fireEvent.pointerDown(within(panel).getByRole('button', { name: 'Tilt up' }), { pointerId: 2 });
    await user.click(screen.getByRole('button', { name: 'Hide pan, tilt and zoom' }));
    await waitFor(() => expect(calls.at(-1)?.path).toBe('/api/cameras/dome/ptz/stop'));
    client.signOut();
  });

  it('says when a camera cannot move, and is not offered to viewers', async () => {
    const { user, client } = renderApp('/live', routes('Operator', []), 'refresh-0', players);
    await user.click(await screen.findByRole('button', { name: 'Enlarge fixed' }));
    await user.click(screen.getByRole('button', { name: 'Pan, tilt and zoom fixed' }));
    expect(await screen.findByText('fixed cannot pan, tilt or zoom.')).toBeInTheDocument();
    // Plain RTSP cameras: no button at all.
    await user.click(screen.getByRole('button', { name: 'Back to the grid' }));
    await user.click(screen.getByRole('button', { name: 'Enlarge door' }));
    expect(screen.queryByRole('button', { name: /Pan, tilt and zoom/ })).not.toBeInTheDocument();
    client.signOut();

    const viewer = renderApp('/live', routes('Viewer', []), 'refresh-0', players);
    await viewer.user.click(await screen.findAllByRole('button', { name: 'Enlarge dome' }).then((b) => b[b.length - 1]));
    expect(screen.queryByRole('button', { name: /Pan, tilt and zoom dome/ })).not.toBeInTheDocument();
    viewer.client.signOut();
  });
});
