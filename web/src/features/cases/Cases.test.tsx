import { screen, within } from '@testing-library/react';
import type { Case, StoredEvent } from '../../api/types';
import { renderApp } from '../../test/renderApp';
import { admin, tenancyRoutes, tokens } from '../../test/fakeRecorder';
import type { PlaybackEngineFactory } from '../playback/engine/types';

const t0 = Date.now() - 60 * 60_000;
const iso = (offsetS: number) => new Date(t0 + offsetS * 1000).toISOString();

const person: StoredEvent = {
  id: 'e1', cameraId: 'lobby', cameraName: 'Lobby', siteId: 'site-main', category: 'person', severity: 'Info', source: 'detection',
  title: 'Person', topic: null, startTime: iso(0), endTime: iso(8), data: null,
};
const status = (cameraId: string, name: string) => ({
  cameraId, name, siteId: 'site-main', state: 'Recording', mode: 'Continuous', isTriggered: false, codec: 'H264', recordsAudio: false, framesRecorded: 0, bytesRecorded: 0, segmentsCompleted: 0,
});
const segment = { cameraId: 'lobby', fileName: 'a.ts', startTime: iso(-300), lastWriteTime: iso(300), size: 1, isActive: false, isLocal: true, isArchived: false };
const theft: Case = {
  id: 'c1', name: 'Theft at the till', description: 'Reported by the cashier.', status: 'Open', createdBy: 'oscar', createdAt: iso(0), updatedAt: iso(60), closedAt: null, clipCount: 2,
  clips: [{ id: 'k1', siteId: 'site-main', cameraIds: ['lobby'], cameras: [{ id: 'lobby', name: 'Lobby' }], from: iso(0), to: iso(30), note: 'Hand in the till', eventId: 'e1', createdBy: 'oscar', createdAt: iso(60) }],
};

function fakePlayback() {
  const loads: string[] = [];
  const factory: PlaybackEngineFactory = (_video, _token, callbacks) => ({
    load(url, start) {
      loads.push(`${new URL(url).pathname} ${start}`);
      callbacks.onStatus('playing');
      callbacks.onTime(start);
    },
    seek(date) {
      callbacks.onTime(date);
      return true;
    },
    play: () => undefined,
    pause: () => callbacks.onStatus('paused'),
    setRate: () => undefined,
    destroy: () => undefined,
  });
  return { factory, loads };
}

const signedIn = (role: string) => ({
  ...tenancyRoutes,
  'POST /api/auth/refresh': () => ({ body: tokens(1) }),
  'GET /api/auth/me': () => ({ body: { ...admin, role } }),
  'GET /api/cameras': () => ({ body: [status('lobby', 'Lobby')] }),
  'GET /api/cameras/lobby/segments': () => ({ body: [segment] }),
});

describe('cases', () => {
  it('saves a marked clip of an event to a new case', async () => {
    const posted: unknown[] = [];
    const { factory } = fakePlayback();
    const { user, client } = renderApp(
      '/investigate',
      {
        ...signedIn('Operator'),
        'GET /api/events': () => ({ body: [person] }),
        'GET /api/cases': () => ({ body: [] }),
        'POST /api/cases': ({ body }) => {
          posted.push(['case', body]);
          return { status: 201, body: { ...theft, id: 'c9', name: (body as { name: string }).name, clips: null, clipCount: 0 } };
        },
        'POST /api/cases/c9/clips': ({ body }) => {
          posted.push(['clip', body]);
          return { status: 201, body: { ...theft.clips![0], id: 'k9' } };
        },
      },
      'refresh-0',
      undefined,
      factory,
    );

    await user.click(await screen.findByRole('button', { name: /Person/ }));
    expect(await screen.findByText('Mark where the clip starts and ends.')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Save to case' })).not.toBeInTheDocument();

    // In at the start, out 10 s later.
    await vi.waitFor(() => expect(screen.getByRole('slider', { name: 'Time' })).toHaveAttribute('aria-valuenow', String(t0 - 5000)));
    await user.click(screen.getByRole('button', { name: 'Mark in' }));
    await user.click(screen.getByRole('button', { name: 'Forward 10 seconds' }));
    await vi.waitFor(() => expect(screen.getByRole('slider', { name: 'Time' })).toHaveAttribute('aria-valuenow', String(t0 + 5000)));
    await user.click(screen.getByRole('button', { name: 'Mark out' }));
    expect(screen.getByText(/\(10 s\)/)).toBeInTheDocument();
    expect(screen.getByTestId('clip-lobby')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Save to case' }));
    const dialog = screen.getByRole('dialog');
    expect(within(dialog).getByLabelText('Case')).toHaveValue('new');
    await user.type(within(dialog).getByLabelText('Name of the new case'), 'Theft at the till');
    await user.type(within(dialog).getByLabelText('Note'), 'Hand in the till');
    await user.click(within(dialog).getByRole('button', { name: 'Save' }));

    expect(await screen.findByRole('status')).toHaveTextContent('Saved to Theft at the till.');
    expect(screen.getByRole('link', { name: 'Theft at the till' })).toHaveAttribute('href', '/cases/c9');
    expect(posted).toEqual([
      ['case', { name: 'Theft at the till' }],
      ['clip', { cameraIds: ['lobby'], from: new Date(t0 - 5000).toISOString(), to: new Date(t0 + 5000).toISOString(), note: 'Hand in the till', eventId: 'e1' }],
    ]);
    client.signOut();
  });

  it('plays the clips of a case, edits notes and closes it', async () => {
    const changes: unknown[] = [];
    const { factory, loads } = fakePlayback();
    let current = theft;
    const { user, client } = renderApp(
      '/cases/c1',
      {
        ...signedIn('Operator'),
        'GET /api/events': () => ({ body: [person] }),
        'GET /api/cases/c1': () => ({ body: current }),
        'PUT /api/cases/c1': ({ body }) => {
          changes.push(['case', body]);
          current = { ...current, ...(body as object), closedAt: iso(120) };
          return { body: current };
        },
        'PUT /api/cases/c1/clips/k1': ({ body }) => {
          changes.push(['note', body]);
          return { body: { ...theft.clips![0], note: (body as { note: string }).note } };
        },
      },
      'refresh-0',
      undefined,
      factory,
    );

    expect(await screen.findByRole('heading', { name: /Theft at the till/ })).toBeInTheDocument();
    expect(screen.getByText('Hand in the till')).toBeInTheDocument();
    expect(screen.getByText('1 more clip(s) are of sites you do not see.')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Delete' })).not.toBeInTheDocument();

    // Played from its start, with the clip marked.
    const clip = screen.getByRole('listitem', { name: /\(30 s\)/ });
    await user.click(within(clip).getByRole('button', { name: 'Play' }));
    await vi.waitFor(() => expect(loads).toContain(`/api/cameras/lobby/recordings.m3u8 ${t0}`));
    expect(screen.getByTestId('clip-lobby')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Back to the clip' })).toBeInTheDocument();

    await user.click(within(clip).getByRole('button', { name: 'Edit note' }));
    const dialog = screen.getByRole('dialog');
    await user.clear(within(dialog).getByLabelText('What the clip shows'));
    await user.type(within(dialog).getByLabelText('What the clip shows'), 'Hand in the till, 22:04');
    await user.click(within(dialog).getByRole('button', { name: 'Save' }));

    await user.click(screen.getByRole('button', { name: 'Close case' }));
    expect(await screen.findByRole('button', { name: 'Reopen case' })).toBeInTheDocument();
    expect(screen.getByText(/its video follows the cameras' retention again/)).toBeInTheDocument();
    expect(changes).toEqual([
      ['note', { note: 'Hand in the till, 22:04' }],
      ['case', { status: 'Closed' }],
    ]);
    client.signOut();
  });

  it('lists cases and opens a new one', async () => {
    const asked: string[] = [];
    const { user, client } = renderApp('/cases', {
      ...signedIn('Admin'),
      'GET /api/cases': ({ query }) => {
        asked.push(query.get('status') ?? 'all');
        return { body: query.get('status') === 'Closed' ? [] : [{ ...theft, clips: null }] };
      },
      'POST /api/cases': ({ body }) => ({ status: 201, body: { ...theft, id: 'c2', name: (body as { name: string }).name } }),
      'GET /api/cases/c2': () => ({ body: { ...theft, id: 'c2', name: 'Graffiti', clips: [], clipCount: 0 } }),
    });

    const row = (await screen.findByRole('link', { name: 'Theft at the till' })).closest('tr')!;
    expect(within(row).getByText('2')).toBeInTheDocument();
    await user.selectOptions(screen.getByLabelText('Show'), 'Closed');
    expect(await screen.findByText('No cases here. Save a clip from Investigate to start one.')).toBeInTheDocument();
    expect(asked).toEqual(['Open', 'Closed']);

    await user.click(screen.getByRole('button', { name: 'New case' }));
    await user.type(within(screen.getByRole('dialog')).getByLabelText('Name'), 'Graffiti');
    await user.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Create' }));
    expect(await screen.findByRole('heading', { name: /Graffiti/ })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Delete' })).toBeInTheDocument();
    client.signOut();
  });
});
