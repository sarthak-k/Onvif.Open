import { screen, within } from '@testing-library/react';
import type { CameraStatus, Gateway, StoredEvent } from '../../api/types';
import { renderApp } from '../../test/renderApp';
import { admin, lyonSite, mainSite, tenancyRoutes, tokens } from '../../test/fakeRecorder';
import { describeEvents, findProblems, startOfDay, summarizeSites } from './health';

const status = (cameraId: string, name: string, state: string, siteId = mainSite.id, extra: Partial<CameraStatus> = {}): CameraStatus => ({
  cameraId, name, siteId, state, mode: 'Continuous', isTriggered: false, codec: 'H264', recordsAudio: false, framesRecorded: 0, bytesRecorded: 0, segmentsCompleted: 0, ...extra,
});
const gateway = (extra: Partial<Gateway>): Gateway => ({
  id: 'gw', name: 'Back office', location: 'Local', embedded: true, status: 'Online', version: '1', startedAt: null, lastSeenAt: new Date().toISOString(),
  health: { cameras: 3, recording: 2, failing: 1, freeBytes: 500 * 1024 ** 3, recordedBytes: 0, backupProvider: null, pendingUploads: null, backupError: null },
  restartRequired: false, storage: null, backup: null, discovery: null, hosting: null, ...extra,
});
const event = (id: string, category: StoredEvent['category'], siteId: string, minutesAgo: number): StoredEvent => ({
  id, cameraId: 'front', cameraName: 'Front door', siteId, category, severity: 'Info', source: 'camera', title: category, topic: null,
  startTime: new Date(Date.now() - minutesAgo * 60_000).toISOString(), endTime: null, data: null,
});

describe('home', () => {
  it('puts what needs a person first', () => {
    const problems = findProblems(
      [status('a', 'Gate', 'Recording'), status('b', 'Dock', 'Reconnecting', lyonSite.id, { lastError: 'Connection refused', lastErrorAt: '2026-11-09T10:00:00Z' }), status('c', 'Till', 'Stopped')],
      [
        gateway({ health: { cameras: 3, recording: 2, failing: 1, freeBytes: 2 * 1024 ** 3, recordedBytes: 0, backupProvider: 'FileSystem', pendingUploads: 400, backupError: null } }),
        gateway({ id: 'gw2', name: 'Warehouse', embedded: false, status: 'Offline' }),
      ],
    );
    expect(problems.map((p) => [p.severity, p.title])).toEqual([
      ['critical', 'Dock is not recording'],
      ['critical', 'Gateway Warehouse is offline'],
      ['warning', 'Till is stopped'],
      ['warning', 'Disk almost full on Back office'],
      ['warning', 'Cloud backup of Back office is behind'],
    ]);
    expect(problems[0]).toMatchObject({ detail: 'Connection refused', siteId: lyonSite.id, since: Date.parse('2026-11-09T10:00:00Z') });
    expect(findProblems([status('a', 'Gate', 'Recording')], [gateway({})])).toEqual([]);
  });

  it('counts each site in its own day', () => {
    // 23:30 UTC on 9 Nov is already 10 Nov in Paris.
    const now = Date.UTC(2026, 10, 9, 23, 30);
    expect(startOfDay(now, 'UTC')).toBe(Date.UTC(2026, 10, 9));
    expect(startOfDay(now, 'Europe/Paris')).toBe(Date.UTC(2026, 10, 9, 23));
    expect(startOfDay(now, 'Asia/Kolkata')).toBe(Date.UTC(2026, 10, 9, 18, 30));

    const events = [{ ...event('e1', 'person', lyonSite.id, 0), startTime: new Date(Date.UTC(2026, 10, 9, 22, 0)).toISOString() }, { ...event('e2', 'motion', lyonSite.id, 0), startTime: new Date(Date.UTC(2026, 10, 9, 23, 10)).toISOString() }];
    const [lyon] = summarizeSites([lyonSite], [status('b', 'Dock', 'Recording', lyonSite.id)], [], events, now);
    expect(lyon.eventsToday.map((e) => e.id)).toEqual(['e2']);
    expect(describeEvents([event('1', 'person', 's', 1), event('2', 'person', 's', 1), event('3', 'motion', 's', 1)], { person: 'Person', motion: 'Motion' })).toBe('2 person, 1 motion');
  });

  it('shows problems, sites, today and what is left to set up', async () => {
    const { client } = renderApp('/', {
      ...tenancyRoutes,
      'POST /api/auth/refresh': () => ({ body: tokens(1) }),
      'GET /api/auth/me': () => ({ body: admin }),
      'GET /api/cameras': () => ({ body: [status('a', 'Gate', 'Recording'), status('b', 'Dock', 'Reconnecting', lyonSite.id, { lastError: 'Connection refused' })] }),
      'GET /api/gateways': () => ({ body: [gateway({})] }),
      'GET /api/users': () => ({ body: [admin] }),
      'GET /api/events': () => ({ body: [event('e1', 'person', mainSite.id, 5), event('e2', 'person', mainSite.id, 7), event('e3', 'offline', lyonSite.id, 9)] }),
    });

    expect(await screen.findByRole('heading', { name: 'Home' })).toBeInTheDocument();
    expect(await screen.findByText('1 problem needs attention.')).toBeInTheDocument();
    const problem = within(screen.getByRole('list', { name: 'Problems' })).getByRole('listitem');
    expect(problem).toHaveTextContent('Dock is not recording');
    expect(within(problem).getByRole('link', { name: 'When it went offline' })).toHaveAttribute('href', '/investigate?camera=b&category=offline&range=7d');

    const main = screen.getByRole('listitem', { name: mainSite.name });
    expect(within(main).getByText('1 of 1 camera recording')).toBeInTheDocument();
    expect(await within(main).findByText('2 events today: 2 person.')).toBeInTheDocument();
    expect(within(screen.getByRole('listitem', { name: lyonSite.name })).getByText('0 of 1 camera recording · 1 problem')).toBeInTheDocument();
    expect(within(screen.getByRole('list', { name: 'Today' })).getAllByRole('link')).toHaveLength(3);

    // Cameras are there; inviting people and a backup are left.
    const setup = screen.getByRole('region', { name: 'Set up' });
    expect(within(setup).queryByRole('link', { name: 'Add your cameras' })).not.toBeInTheDocument();
    expect(within(setup).getByRole('link', { name: 'Invite the people who watch them' })).toBeInTheDocument();
    expect(within(setup).getByRole('link', { name: 'Back up recordings to the cloud or a NAS' })).toBeInTheDocument();
    client.signOut();
  });

  it('says so when everything records', async () => {
    const { user, client } = renderApp('/home', {
      ...tenancyRoutes,
      'POST /api/auth/refresh': () => ({ body: tokens(1) }),
      'GET /api/auth/me': () => ({ body: { ...admin, role: 'Viewer' } }),
      'GET /api/cameras': () => ({ body: [status('a', 'Gate', 'Recording')] }),
      'GET /api/gateways': () => ({ body: [gateway({})] }),
      'GET /api/events': () => ({ body: [] }),
    });

    expect(await screen.findByText('Everything is recording: 1 camera.')).toBeInTheDocument();
    expect(screen.queryByRole('region', { name: 'Set up' })).not.toBeInTheDocument();
    expect(await screen.findByText('Nothing happened today.')).toBeInTheDocument();
    await user.click(screen.getByRole('link', { name: 'All events' }));
    expect(await screen.findByRole('heading', { name: 'Investigate' })).toBeInTheDocument();
    client.signOut();
  });
});
