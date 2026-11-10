import { screen, waitFor, within } from '@testing-library/react';
import type { Alert, AlertRule } from '../../api/types';
import { renderApp } from '../../test/renderApp';
import { admin, lyonSite, tenancyRoutes, tokens } from '../../test/fakeRecorder';
import { describeDelivery, describeRule, emailsOf, formOf, requestOf } from '../settings/alertRuleForm';
import { alertToAnnounce, deliveryProblems, investigateLink } from './alerts';

const now = Date.now();
const alert = (id: string, extra: Partial<Alert> = {}): Alert => ({
  id, ruleId: 'r1', ruleName: 'Person at night', siteId: lyonSite.id, cameraId: 'dock', cameraName: 'Dock', eventId: 'e1', category: 'person', severity: 'Warning',
  title: 'Person', status: 'Open', triggeredAt: new Date(now - 5 * 60_000).toISOString(), acknowledgedAt: null, acknowledgedBy: null, delivery: {}, ...extra,
});
const rule: AlertRule = {
  id: 'r1', name: 'Person at night', enabled: true, kind: 'Event', categories: ['person', 'vehicle'], minSeverity: 'Warning', offlineMinutes: 5, siteIds: [lyonSite.id], cameraIds: [],
  hours: [{ days: ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday'], start: '22:00', end: '06:00' }], cooldownMinutes: 5, emails: ['guard@example.com'],
  webhookUrl: 'https://hooks.example.com/a', hasWebhookSecret: true, createdAt: '', updatedAt: '',
};

const signedIn = (role: string, routes: Parameters<typeof renderApp>[1] = {}) => ({
  ...tenancyRoutes,
  'POST /api/auth/refresh': () => ({ body: tokens(1) }),
  'GET /api/auth/me': () => ({ body: { ...admin, role } }),
  'GET /api/cameras': () => ({ body: [] }),
  ...routes,
});

describe('alert helpers', () => {
  it('links to Investigate around the alert and names failed notifications', () => {
    expect(investigateLink(alert('a'), now)).toBe(`/investigate?range=1h&site=${lyonSite.id}&camera=dock&category=person`);
    expect(investigateLink(alert('a', { category: 'test', cameraId: null, triggeredAt: new Date(now - 3 * 86_400_000).toISOString() }), now)).toBe(`/investigate?range=7d&site=${lyonSite.id}`);
    expect(deliveryProblems(alert('a', { delivery: { email: { ok: true, error: null }, webhook: { ok: false, error: 'The webhook answered 503.' } } }))).toEqual(['Webhook failed: The webhook answered 503.']);
  });

  it('announces only alerts that are new since the last check', () => {
    expect(alertToAnnounce(undefined, { open: 1, latest: alert('a') })).toBeNull();
    expect(alertToAnnounce('a', { open: 1, latest: alert('a') })).toBeNull();
    expect(alertToAnnounce('a', { open: 2, latest: alert('b') })?.id).toBe('b');
    expect(alertToAnnounce(null, { open: 1, latest: alert('b') })?.id).toBe('b');
    expect(alertToAnnounce('a', { open: 0, latest: null })).toBeNull();
  });

  it('turns the editor into a rule and back', () => {
    expect(describeRule(rule)).toBe('Person or Vehicle, Warning or worse, Mon–Fri 22:00–06:00 (next day)');
    expect(describeRule({ ...rule, kind: 'Offline', offlineMinutes: 10, hours: [] })).toBe('Camera offline for 10 min');
    expect(describeDelivery(rule)).toBe('Email (1), webhook');
    expect(describeDelivery({ ...rule, emails: [], webhookUrl: null })).toBe('Inbox only');
    expect(emailsOf('a@x.com, b@y.com\nc@z.com;')).toEqual(['a@x.com', 'b@y.com', 'c@z.com']);

    const form = formOf(rule);
    expect(requestOf(form)).toMatchObject({ categories: ['person', 'vehicle'], minSeverity: 'Warning', hours: rule.hours, emails: ['guard@example.com'], webhookSecret: null });
    expect(requestOf({ ...form, limitHours: false, removeSecret: true })).toMatchObject({ hours: [], webhookSecret: '' });
    expect(requestOf({ ...form, kind: 'Offline' })).toMatchObject({ categories: [], minSeverity: null, offlineMinutes: 5 });
  });
});

describe('alerts', () => {
  it('shows open alerts with a count in the navigation, and operators acknowledge them', async () => {
    let open = [alert('a2', { title: 'Camera offline for 6 min', category: 'offline' }), alert('a1', { delivery: { webhook: { ok: false, error: 'Timed out.' } } })];
    const { user } = renderApp(
      '/alerts',
      signedIn('Operator', {
        'GET /api/alerts': ({ query }) => ({ body: query.get('status') === 'open' ? open : [...open, alert('a0', { acknowledgedAt: new Date().toISOString(), status: 'Acknowledged' })] }),
        'GET /api/alerts/summary': () => ({ body: { open: open.length, latest: open[0] ?? null } }),
        'POST /api/alerts/a1/acknowledge': () => {
          open = open.filter((a) => a.id !== 'a1');
          return { body: { ...alert('a1'), acknowledgedAt: new Date().toISOString(), acknowledgedBy: 'oscar', status: 'Acknowledged' } };
        },
      }),
    );

    const list = await screen.findByRole('list', { name: 'Alerts' });
    const person = await within(list).findByRole('listitem', { name: 'Person at Dock' });
    expect(within(list).getByRole('listitem', { name: 'Camera offline for 6 min at Dock' })).toBeInTheDocument();
    expect(within(person).getByText('Webhook failed: Timed out.')).toBeInTheDocument();
    expect(within(person).getByRole('link', { name: 'Investigate' })).toHaveAttribute('href', expect.stringContaining('camera=dock&category=person'));
    expect(within(screen.getByRole('navigation', { name: 'Main' })).getByLabelText('2 open')).toHaveTextContent('2');

    await user.click(within(person).getByRole('button', { name: 'Acknowledge' }));
    await waitFor(() => expect(within(screen.getByRole('navigation', { name: 'Main' })).getByLabelText('1 open')).toBeInTheDocument());
  });

  it('lets viewers read alerts but not acknowledge them', async () => {
    renderApp('/alerts', signedIn('Viewer', { 'GET /api/alerts': () => ({ body: [alert('a1')] }), 'GET /api/alerts/summary': () => ({ body: { open: 1, latest: alert('a1') } }) }));
    const person = await screen.findByRole('listitem', { name: 'Person at Dock' });
    expect(within(person).queryByRole('button', { name: 'Acknowledge' })).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Alert rules' })).not.toBeInTheDocument();
  });

  it('sets up, tests and deletes alert rules', async () => {
    const saved: unknown[] = [];
    let rules: AlertRule[] = [];
    const { user } = renderApp(
      '/settings/alerts',
      signedIn('Admin', {
        'GET /api/alert-rules': () => ({ body: rules }),
        'POST /api/alert-rules': ({ body }) => {
          saved.push(body);
          rules = [{ ...rule, ...(body as object), id: 'r1', hasWebhookSecret: false }];
          return { status: 201, body: rules[0] };
        },
        'POST /api/alert-rules/r1/test': () => ({ body: { delivery: { email: { ok: true, error: null } } } }),
        'DELETE /api/alert-rules/r1': () => {
          rules = [];
          return { status: 204 };
        },
      }),
    );

    expect(await screen.findByText(/No rules yet/)).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Add rule' }));
    const form = screen.getByRole('form', { name: 'Alert rule' });
    await user.type(within(form).getByLabelText('Name'), 'Vehicles at Lyon');
    await user.click(within(form).getByLabelText('Person'));
    await user.click(within(form).getByLabelText('Vehicle'));
    await user.click(within(form).getByLabelText('Lyon'));
    await user.click(within(form).getByLabelText('Only during set hours'));
    await user.type(within(form).getByLabelText('Email to (comma-separated)'), 'guard@example.com, boss@example.com');
    await user.click(screen.getByRole('button', { name: 'Save' }));

    await waitFor(() => expect(saved).toHaveLength(1));
    expect(saved[0]).toMatchObject({
      name: 'Vehicles at Lyon',
      kind: 'Event',
      categories: ['vehicle'],
      siteIds: [lyonSite.id],
      hours: [{ start: '22:00', end: '06:00' }],
      emails: ['guard@example.com', 'boss@example.com'],
      webhookUrl: null,
    });

    const row = (await screen.findByRole('cell', { name: 'Vehicles at Lyon' })).closest('tr')!;
    expect(within(row).getByText('Email (2)')).toBeInTheDocument();
    await user.click(within(row).getByRole('button', { name: 'Test' }));
    expect(await screen.findByRole('status')).toHaveTextContent('Test of Vehicles at Lyon: email: sent');

    await user.click(within(row).getByRole('button', { name: 'Delete' }));
    await user.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Delete' }));
    expect(await screen.findByText(/No rules yet/)).toBeInTheDocument();
  });
});
