import { screen, within } from '@testing-library/react';
import type { AuditEntry } from '../../api/types';
import { auditPageSize } from '../../data/audit';
import { renderApp } from '../../test/renderApp';
import { admin, tenancyRoutes, tokens } from '../../test/fakeRecorder';
import { describeDetails } from './auditActions';

const entry = (index: number, overrides: Partial<AuditEntry> = {}): AuditEntry => ({
  id: `a${index}`,
  time: new Date(Date.UTC(2026, 10, 9, 12, 0, 0) - index * 60_000).toISOString(),
  userId: 'u-admin',
  userName: 'admin',
  action: 'camera.update',
  targetType: 'camera',
  targetId: `cam-${index}`,
  targetName: `Camera ${index}`,
  details: null,
  ipAddress: '10.0.0.5',
  ...overrides,
});

const signedIn = (role = 'Admin', sites: string[] = []) => ({
  ...tenancyRoutes,
  'POST /api/auth/refresh': () => ({ body: tokens(1) }),
  'GET /api/auth/me': () => ({ body: { ...admin, role, sites } }),
  'GET /api/users': () => ({ body: [{ ...admin, id: 'u-admin' }, { ...admin, id: 'u-ana', userName: 'ana' }] }),
});

describe('audit log', () => {
  it('lists what people did in words, filters and pages', async () => {
    const queries: string[] = [];
    const firstPage = [
      entry(0, { action: 'auth.signin_failed', targetType: null, targetId: null, targetName: null }),
      entry(1, { action: 'user.update', targetName: 'ana', details: { role: 'Operator', sites: [] } }),
      ...Array.from({ length: auditPageSize - 2 }, (_, i) => entry(i + 2)),
    ];
    const { user, client } = renderApp('/settings/audit', {
      ...signedIn(),
      'GET /api/audit': ({ query }) => {
        queries.push(query.toString());
        if (query.get('before')) {
          return { body: [entry(99, { action: 'video.export', targetName: 'Dock' })] };
        }
        return { body: query.get('action') === 'video' ? [entry(5, { action: 'video.playback', targetName: 'Lobby', userName: 'ana' })] : firstPage };
      },
    });

    expect(await screen.findByRole('heading', { name: 'Audit log' })).toBeInTheDocument();
    const failed = (await screen.findByText('Failed to sign in')).closest('tr')!;
    expect(within(failed).getByText('10.0.0.5')).toBeInTheDocument();
    const changed = screen.getByText('Changed a user').closest('tr')!;
    expect(within(changed).getByText('role: Operator; sites: none')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Load more' }));
    expect(await screen.findByText('Exported video')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Load more' })).not.toBeInTheDocument();

    await user.selectOptions(screen.getByLabelText('Show'), 'video');
    expect(await screen.findByText('Watched recordings')).toBeInTheDocument();
    expect(screen.queryByText('Failed to sign in')).not.toBeInTheDocument();

    expect(queries).toEqual([`limit=${auditPageSize}`, `before=${encodeURIComponent(firstPage[auditPageSize - 1].time)}&limit=${auditPageSize}`, `action=video&limit=${auditPageSize}`]);
    client.signOut();
  });

  it('is only for administrators who see every site', async () => {
    const { client } = renderApp('/settings/sites', { ...signedIn('Admin', ['site-lyon']) });
    expect(await screen.findByRole('navigation', { name: 'Settings' })).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Audit log' })).not.toBeInTheDocument();
    client.signOut();
  });

  it('shows unknown details as sent', () => {
    expect(describeDetails('from 10:00 to 10:05')).toBe('from 10:00 to 10:05');
    expect(describeDetails(null)).toBe('');
    expect(describeDetails('{"from":"10:00"}')).toBe('from: 10:00');
    expect(describeDetails({ backup: 'none', storage: false, range: { from: 1 } })).toBe('backup: none; storage: false; range: {"from":1}');
  });
});
