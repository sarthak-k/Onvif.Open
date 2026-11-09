import { screen, within } from '@testing-library/react';
import type { StoredEvent, VideoExport } from '../../api/types';
import { renderApp } from '../../test/renderApp';
import { admin, tenancyRoutes, tokens } from '../../test/fakeRecorder';
import type { PlaybackEngineFactory } from '../playback/engine/types';
import { formatClock, zoneName, zoneOffsetMs } from '../playback/time';

const t0 = Date.UTC(2026, 10, 9, 21, 0, 0);
const iso = (offsetS: number) => new Date(t0 + offsetS * 1000).toISOString();

const exported = (overrides: Partial<VideoExport>): VideoExport => ({
  id: 'x1', cameraId: 'dock', cameraName: 'Dock', siteId: 'site-lyon', caseId: null, from: iso(0), to: iso(30), status: 'Ready', error: null,
  size: 2_400_000, sha256: 'ab'.repeat(32), requestedBy: 'oscar', createdAt: iso(60), completedAt: iso(90), expiresAt: iso(7 * 86400), downloadable: true,
  ...overrides,
});

const playback: PlaybackEngineFactory = (_video, _token, callbacks) => ({
  load(_url, start) {
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

const signedIn = (role: string) => ({
  ...tenancyRoutes,
  'POST /api/auth/refresh': () => ({ body: tokens(1) }),
  'GET /api/auth/me': () => ({ body: { ...admin, role } }),
});

describe('exports', () => {
  it('shows times in the site time zone', () => {
    // 21:00 UTC is 22:00 in Paris in November.
    expect(formatClock(t0, false, 'Europe/Paris')).toBe(formatClock(t0 + 3_600_000, false, 'UTC'));
    expect(formatClock(t0, false, 'Europe/Paris')).not.toBe(formatClock(t0, false, 'UTC'));
    expect(zoneName(t0, 'Europe/Paris')).toMatch(/CET|GMT\+1/);
    expect(zoneOffsetMs(t0, 'Europe/Paris')).toBe(3_600_000);
    expect(zoneOffsetMs(t0, 'Asia/Kolkata')).toBe(5.5 * 3_600_000);
  });

  it('exports the marked clip of every camera on screen', async () => {
    const event: StoredEvent = {
      id: 'e1', cameraId: 'dock', cameraName: 'Dock', siteId: 'site-lyon', category: 'person', severity: 'Info', source: 'detection',
      title: 'Person', topic: null, startTime: new Date(Date.now() - 3_600_000).toISOString(), endTime: new Date(Date.now() - 3_590_000).toISOString(), data: null,
    };
    const requested: unknown[] = [];
    const { user, client } = renderApp(
      '/investigate',
      {
        ...signedIn('Viewer'),
        'GET /api/cameras': () => ({ body: [{ cameraId: 'dock', name: 'Dock', siteId: 'site-lyon', state: 'Recording', mode: 'Continuous', isTriggered: false, codec: 'H264', recordsAudio: false, framesRecorded: 0, bytesRecorded: 0, segmentsCompleted: 0 }] }),
        'GET /api/events': () => ({ body: [event] }),
        'GET /api/cameras/dock/segments': () => ({ body: [] }),
        'POST /api/exports': ({ body }) => {
          requested.push(body);
          return { status: 202, body: exported({ status: 'Queued', downloadable: false }) };
        },
      },
      'refresh-0',
      undefined,
      playback,
    );

    await user.click(await screen.findByRole('button', { name: /Person/ }));
    // Viewers export, but do not save to cases.
    await user.click(await screen.findByRole('button', { name: 'Mark in' }));
    await user.click(screen.getByRole('button', { name: 'Forward 10 seconds' }));
    await user.click(screen.getByRole('button', { name: 'Mark out' }));
    expect(screen.queryByRole('button', { name: 'Save to case' })).not.toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Export MP4' }));

    expect(await screen.findByRole('status')).toHaveTextContent('Preparing 1 video: see Exports.');
    expect(requested).toHaveLength(1);
    const body = requested[0] as { cameraId: string; from: string; to: string; caseId: null };
    expect(body.cameraId).toBe('dock');
    expect(Date.parse(body.to) - Date.parse(body.from)).toBe(10_000);
    client.signOut();
  });

  it('lists exports to download, and shares them with links that expire', async () => {
    let links: { id: string; path: string | null; createdBy: string; createdAt: string; expiresAt: string; revokedAt: string | null; downloads: number }[] = [];
    const { user, client } = renderApp('/exports', {
      ...signedIn('Operator'),
      'GET /api/exports': () => ({
        body: [exported({}), exported({ id: 'x2', status: 'Failed', error: 'Nothing was recorded in that time.', downloadable: false }), exported({ id: 'x3', status: 'Running', downloadable: false })],
      }),
      'GET /api/exports/x1/links': () => ({ body: links }),
      'POST /api/exports/x1/links': ({ body }) => {
        const link = { id: 'l1', path: '/api/shared/secret-token/video.mp4', createdBy: 'oscar', createdAt: iso(100), expiresAt: iso(100 + 3600 * (body as { hours: number }).hours), revokedAt: null, downloads: 0 };
        links = [{ ...link, path: null }];
        return { status: 201, body: link };
      },
      'DELETE /api/exports/x1/links/l1': () => {
        links = [{ ...links[0], revokedAt: iso(200) }];
        return { status: 204 };
      },
    });

    const ready = (await screen.findByRole('link', { name: 'Download' })).closest('tr')!;
    // Lyon is in Paris time: 21:00 UTC shows as 22:00.
    expect(within(ready).getByText(`${formatClock(t0, true, 'Europe/Paris')} – ${formatClock(t0 + 30_000, true, 'Europe/Paris')}`, { exact: false })).toBeInTheDocument();
    expect(formatClock(t0, true, 'Europe/Paris')).toBe(formatClock(t0 + 3_600_000, true, 'UTC'));
    expect(within(ready).getByRole('link', { name: 'Download' })).toHaveAttribute('href', expect.stringMatching(/\/api\/exports\/x1\/video\.mp4\?access_token=/));
    expect(screen.getByText('Nothing was recorded in that time.')).toBeInTheDocument();
    expect(screen.getByText('Being made…')).toBeInTheDocument();
    expect(screen.getAllByRole('link', { name: 'Download' })).toHaveLength(1);

    await user.click(within(ready).getByRole('button', { name: 'Share' }));
    const dialog = screen.getByRole('dialog');
    await user.selectOptions(within(dialog).getByLabelText('Link works for'), '24');
    await user.click(within(dialog).getByRole('button', { name: 'Create link' }));
    expect(await within(dialog).findByLabelText('New link (shown only now)')).toHaveValue('http://localhost:3000/api/shared/secret-token/video.mp4');
    const list = await within(dialog).findByRole('list', { name: 'Links' });
    await user.click(within(list).getByRole('button', { name: 'Revoke' }));
    expect(await within(dialog).findByText(/Revoked · 0 downloads/)).toBeInTheDocument();
    client.signOut();
  });
});
