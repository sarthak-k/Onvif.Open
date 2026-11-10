import { act, screen, within } from '@testing-library/react';
import type { PlayerFactory, PlayerState } from './player/types';
import { renderApp } from '../../test/renderApp';
import { admin, tokens } from '../../test/fakeRecorder';

/** A player that records what it was asked to play; tests drive its state. */
function fakePlayers() {
  const players: { url: string; lowLatencyUrl?: string; token: string | null; setState(state: PlayerState): void; destroyed: boolean }[] = [];
  const factory: PlayerFactory = (_video, source, onState) => {
    const player = { url: source.url, lowLatencyUrl: source.lowLatencyUrl, token: source.token(), setState: onState, destroyed: false };
    players.push(player);
    return { destroy: () => (player.destroyed = true) };
  };
  return { factory, players, live: () => players.filter((p) => !p.destroyed) };
}

const status = (cameraId: string) => ({ cameraId, name: cameraId, state: 'Recording', mode: 'Continuous', isTriggered: false, codec: 'H264', recordsAudio: false, framesRecorded: 0, bytesRecorded: 0, segmentsCompleted: 0 });
const saved = { id: 'l1', name: 'Doors', grid: '2x1', cameras: ['back', 'front'], shared: true, editable: false, updatedAt: '2026-11-08T00:00:00Z' };

function routes(role: string, layouts: unknown[] = [saved]) {
  return {
    'POST /api/auth/refresh': () => ({ body: tokens(1) }),
    'GET /api/auth/me': () => ({ body: { ...admin, role } }),
    'GET /api/cameras': () => ({ body: [status('front'), status('back'), status('yard')] }),
    'GET /api/layouts': () => ({ body: layouts }),
  };
}

describe('live view', () => {
  it('shows every camera at first, each in its own player', async () => {
    const { factory, live } = fakePlayers();
    const { client } = renderApp('/live', routes('Viewer', []), 'refresh-0', factory);

    expect(await screen.findByRole('region', { name: 'Tile 1: back' })).toBeInTheDocument();
    expect(screen.getByRole('region', { name: 'Tile 4: empty' })).toBeInTheDocument();
    // Each tile starts its player once it is laid out: wait for all of them.
    await vi.waitFor(() =>
      expect(live().map((p) => p.url)).toEqual(['http://localhost:3000/api/cameras/back/live.m3u8', 'http://localhost:3000/api/cameras/front/live.m3u8', 'http://localhost:3000/api/cameras/yard/live.m3u8']),
    );
    expect(live().every((p) => p.token === 'access-1')).toBe(true);
    // A 2 × 2 grid: each tile plays the camera's smaller stream.
    expect(live().map((p) => p.lowLatencyUrl)).toEqual(['back', 'front', 'yard'].map((id) => `http://localhost:3000/api/cameras/${id}/live.mp4?stream=sub`));

    // The player reports its state; errors are shown on the tile.
    const tile = screen.getByRole('region', { name: 'Tile 2: front' });
    expect(within(tile).getByRole('status')).toHaveTextContent('Connecting');
    act(() => live()[1].setState({ status: 'error', message: 'No video from the camera yet.' }));
    expect(within(tile).getByRole('alert')).toHaveTextContent('No video from the camera yet.');
    act(() => live()[1].setState({ status: 'playing' }));
    expect(within(tile).queryByRole('alert')).not.toBeInTheDocument();
    client.signOut();
  });

  it('opens a saved layout and enlarges a tile', async () => {
    const { factory, live } = fakePlayers();
    const { user, client } = renderApp('/live', routes('Viewer'), 'refresh-0', factory);

    await screen.findByRole('option', { name: 'Doors (shared)' });
    await user.selectOptions(screen.getByLabelText('Layout'), 'l1');
    expect(screen.getByLabelText('Grid')).toHaveValue('2x1');
    expect(screen.getAllByRole('region').map((r) => r.getAttribute('aria-label'))).toEqual(['Tile 1: back', 'Tile 2: front']);
    // A shared layout of the administrators: viewers may not change it.
    expect(screen.queryByRole('button', { name: 'Delete' })).not.toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Enlarge front' }));
    expect(screen.getAllByRole('region')).toHaveLength(1);
    expect(live().map((p) => p.url)).toEqual(['http://localhost:3000/api/cameras/front/live.m3u8']);
    // Enlarged, and the halves of a two-camera layout: the main stream.
    expect(live().map((p) => p.lowLatencyUrl)).toEqual(['http://localhost:3000/api/cameras/front/live.mp4']);

    await user.keyboard('{Escape}');
    expect(screen.getAllByRole('region')).toHaveLength(2);
    client.signOut();
  });

  it('saves an arrangement as a new layout', async () => {
    let posted: unknown;
    const { factory } = fakePlayers();
    const { user, client } = renderApp(
      '/live',
      {
        ...routes('Admin', []),
        'POST /api/layouts': ({ body }) => {
          posted = body;
          return { status: 201, body: { ...(body as object), id: 'l2', editable: true, shared: true, updatedAt: '' } };
        },
      },
      'refresh-0',
      factory,
    );

    await user.selectOptions(await screen.findByLabelText('Grid'), '1+5');
    await user.click(screen.getByRole('button', { name: 'Edit cameras' }));
    await user.selectOptions(screen.getByLabelText('Camera in tile 1'), 'yard');
    await user.selectOptions(screen.getByLabelText('Camera in tile 6'), 'front');
    await user.click(screen.getByRole('button', { name: 'Save as…' }));

    const dialog = screen.getByRole('dialog');
    await user.type(within(dialog).getByLabelText('Name'), 'Yard first');
    await user.click(within(dialog).getByLabelText('Share with all users'));
    await user.click(within(dialog).getByRole('button', { name: 'Save' }));

    await vi.waitFor(() => expect(posted).toEqual({ name: 'Yard first', grid: '1+5', cameras: ['yard', 'front', 'yard', null, null, 'front'], shared: true }));
    client.signOut();
  });

  it('offers sharing to administrators only', async () => {
    const { factory } = fakePlayers();
    const { user, client } = renderApp('/live', routes('Operator', []), 'refresh-0', factory);

    await user.click(await screen.findByRole('button', { name: 'Save as…' }));
    expect(within(screen.getByRole('dialog')).queryByLabelText('Share with all users')).not.toBeInTheDocument();
    client.signOut();
  });

  it('saves changes to an own layout and deletes it', async () => {
    const own = { ...saved, id: 'mine', name: 'Mine', shared: false, editable: true };
    const calls: string[] = [];
    const { factory } = fakePlayers();
    const { user, client } = renderApp(
      '/live',
      {
        ...routes('Viewer', [own]),
        'PUT /api/layouts/mine': ({ body }) => {
          calls.push(`PUT ${JSON.stringify(body)}`);
          return { body: { ...own, ...(body as object) } };
        },
        'DELETE /api/layouts/mine': () => {
          calls.push('DELETE');
          return { status: 204 };
        },
      },
      'refresh-0',
      factory,
    );

    await screen.findByRole('option', { name: 'Mine' });
    await user.selectOptions(screen.getByLabelText('Layout'), 'mine');
    expect(screen.queryByRole('button', { name: 'Save' })).not.toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Edit cameras' }));
    await user.selectOptions(screen.getByLabelText('Camera in tile 2'), 'yard');
    await user.click(screen.getByRole('button', { name: 'Save' }));
    await vi.waitFor(() => expect(calls).toEqual([`PUT ${JSON.stringify({ name: 'Mine', grid: '2x1', cameras: ['back', 'yard'] })}`]));

    await user.click(screen.getByRole('button', { name: 'Delete' }));
    await user.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Delete' }));
    await vi.waitFor(() => expect(calls).toContain('DELETE'));
    client.signOut();
  });
});
