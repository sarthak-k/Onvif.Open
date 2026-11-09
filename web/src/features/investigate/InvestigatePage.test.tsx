import { screen, within } from '@testing-library/react';
import type { StoredEvent } from '../../api/types';
import { renderApp } from '../../test/renderApp';
import { admin, tenancyRoutes, tokens } from '../../test/fakeRecorder';
import type { PlaybackEngineFactory } from '../playback/engine/types';

const t0 = Date.now() - 60 * 60_000;
const iso = (offsetS: number) => new Date(t0 + offsetS * 1000).toISOString();

const person: StoredEvent = {
  id: 'e1', cameraId: 'lobby', cameraName: 'Lobby', siteId: 'site-main', category: 'person', severity: 'Info', source: 'detection',
  title: 'Person', topic: null, startTime: iso(0), endTime: iso(8), data: null,
};
const tamper: StoredEvent = { ...person, id: 'e2', category: 'tamper', severity: 'Warning', title: 'Camera covered', startTime: iso(-600), endTime: iso(-590) };
const status = (cameraId: string, name: string) => ({
  cameraId, name, siteId: 'site-main', state: 'Recording', mode: 'Continuous', isTriggered: false, codec: 'H264', recordsAudio: false, framesRecorded: 0, bytesRecorded: 0, segmentsCompleted: 0,
});
const segment = (cameraId: string) => ({ cameraId, fileName: 'a.ts', startTime: iso(-300), lastWriteTime: iso(300), size: 1, isActive: false, isLocal: true, isArchived: false });

/** Records what the page asks of each camera's player. */
function fakePlayback() {
  const engines: { calls: string[] }[] = [];
  const factory: PlaybackEngineFactory = (_video, _token, callbacks) => {
    const engine = { calls: [] as string[] };
    engines.push(engine);
    return {
      load(url, start) {
        engine.calls.push(`load ${new URL(url).pathname} ${start}`);
        callbacks.onStatus('playing');
        callbacks.onTime(start);
      },
      seek(date) {
        engine.calls.push(`seek ${date}`);
        callbacks.onTime(date);
        return true;
      },
      play: () => engine.calls.push('play'),
      pause: () => {
        engine.calls.push('pause');
        callbacks.onStatus('paused');
      },
      setRate: (rate) => engine.calls.push(`rate ${rate}`),
      destroy: () => engine.calls.push('destroy'),
    };
  };
  return { factory, engines };
}

describe('investigate', () => {
  it('finds events and plays their cameras in sync around them', async () => {
    const searches: string[] = [];
    const { factory, engines } = fakePlayback();
    const { user, client } = renderApp(
      '/investigate',
      {
        ...tenancyRoutes,
        'POST /api/auth/refresh': () => ({ body: tokens(1) }),
        'GET /api/auth/me': () => ({ body: admin }),
        'GET /api/cameras': () => ({ body: [status('lobby', 'Lobby'), status('dock', 'Dock')] }),
        'GET /api/events': ({ query }) => {
          if (query.get('limit') === '500') return { body: [person] };
          searches.push(`${query.get('category') ?? 'all'} ${query.get('from') ? 'from' : ''}`);
          return { body: query.get('category') === 'person' ? [person] : [person, tamper] };
        },
        'GET /api/cameras/lobby/segments': () => ({ body: [segment('lobby')] }),
        'GET /api/cameras/dock/segments': () => ({ body: [segment('dock')] }),
      },
      'refresh-0',
      undefined,
      factory,
    );

    const events = await screen.findByRole('list', { name: 'Events' });
    expect(await within(events).findAllByRole('button')).toHaveLength(2);
    expect(within(events).getByText('Camera covered')).toBeInTheDocument();
    expect(within(events).getByText(/Lobby · .* · 8 s/)).toBeInTheDocument();

    // Only people.
    await user.selectOptions(screen.getByLabelText('What'), 'person');
    await vi.waitFor(() => expect(within(screen.getByRole('list', { name: 'Events' })).getAllByRole('button')).toHaveLength(1));
    expect(searches).toEqual(['all from', 'person from']);

    // Open it: the camera plays from 5 s before the event.
    await user.click(within(screen.getByRole('list', { name: 'Events' })).getByRole('button', { name: /Person/ }));
    expect(await screen.findByRole('heading', { name: 'Person · Lobby' })).toBeInTheDocument();
    await vi.waitFor(() => expect(engines[0].calls).toContain(`load /api/cameras/lobby/recordings.m3u8 ${t0 - 5000}`));
    expect(screen.getByTestId('track-lobby')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /^Person at/ })).toBeInTheDocument();

    // Another camera of the site joins at the same time.
    await user.click(screen.getByLabelText('Dock'));
    await vi.waitFor(() => expect(engines.some((e, i) => i > 0 && e.calls.some((c) => c.startsWith('load /api/cameras/dock/recordings.m3u8')))).toBe(true));
    expect(screen.getByRole('figure', { name: 'Dock' })).toBeInTheDocument();

    // Shared controls reach every camera.
    await user.selectOptions(screen.getByLabelText('Speed'), '2');
    await user.click(screen.getByRole('button', { name: 'Pause' }));
    await vi.waitFor(() => {
      const dock = engines.find((e) => e.calls.some((c) => c.includes('/dock/')))!;
      expect(engines[0].calls).toContain('rate 2');
      expect(dock.calls).toContain('rate 2');
      expect(engines[0].calls).toContain('pause');
      expect(dock.calls).toContain('pause');
    });
    expect(screen.getByRole('button', { name: 'Play' })).toBeInTheDocument();

    // Jumping moves all of them.
    await user.click(screen.getByRole('button', { name: 'Back 10 seconds' }));
    await vi.waitFor(() => expect(engines[0].calls.filter((c) => c.startsWith('seek')).length).toBeGreaterThan(0));
    client.signOut();
  });

  it('explains events without video', async () => {
    const { factory } = fakePlayback();
    const { user, client } = renderApp(
      '/investigate?range=7d',
      {
        ...tenancyRoutes,
        'POST /api/auth/refresh': () => ({ body: tokens(1) }),
        'GET /api/auth/me': () => ({ body: { ...admin, role: 'Viewer' } }),
        'GET /api/cameras': () => ({ body: [] }),
        'GET /api/events': () => ({ body: [{ ...person, id: 'p', cameraId: null, cameraName: null, category: 'plugin', title: 'Exam started' }] }),
      },
      'refresh-0',
      undefined,
      factory,
    );

    expect(await screen.findByRole('link', { name: 'Investigate' })).toBeInTheDocument();
    expect(await screen.findByLabelText('When')).toHaveValue('7d');
    await user.click(await screen.findByRole('button', { name: /Exam started/ }));
    expect(screen.getByText('This event is not about a camera: there is no video for it.')).toBeInTheDocument();
    client.signOut();
  });
});
