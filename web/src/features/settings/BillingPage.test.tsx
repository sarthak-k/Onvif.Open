import { screen, within } from '@testing-library/react';
import { renderApp } from '../../test/renderApp';
import { admin, tenancyRoutes, tokens } from '../../test/fakeRecorder';
import { browser } from '../../shared/browser';

const plans = [
  { id: 'trial', code: 'trial', active: true, name: 'Trial', cameras: 2, storageGB: 10, monthlyPrice: 0, purchasable: false },
  { id: 'starter', code: 'starter', active: true, name: 'Starter', cameras: 4, storageGB: 500, monthlyPrice: 29, purchasable: true },
  { id: 'business', code: 'business', active: true, name: 'Business', cameras: 0, storageGB: 5000, monthlyPrice: 199, purchasable: true },
];

const trial = {
  enabled: true,
  currency: 'EUR',
  plan: plans[0],
  state: 'Trialing',
  trialEndsAt: '2026-11-25T00:00:00Z',
  currentPeriodEnd: null,
  usage: { cameras: 2, storageBytes: 4 * 1024 ** 3 },
  overCameras: false,
  overStorage: false,
  canPay: true,
  canManage: false,
  salesEmail: null,
  plans,
};

const signedIn = (role: 'Owner' | 'Admin', subscription: object | null = null) => ({
  ...tenancyRoutes,
  'POST /api/auth/refresh': () => ({ body: tokens(1) }),
  'GET /api/auth/me': () => ({ body: { ...admin, role, organization: { ...admin.organization, subscription } } }),
});

describe('billing', () => {
  afterEach(() => vi.restoreAllMocks());

  it('shows the plan and its use, and sends owners to the checkout for another', async () => {
    const leave = vi.spyOn(browser, 'leave').mockImplementation(() => undefined);
    let asked: unknown;
    const { user, client } = renderApp('/settings/billing', {
      ...signedIn('Owner'),
      'GET /api/billing': () => ({ body: trial }),
      'POST /api/billing/checkout': ({ body }) => {
        asked = body;
        return { body: { url: 'https://checkout.stripe.test/cs_1' } };
      },
    });

    const current = await screen.findByRole('article', { name: 'Your plan' });
    expect(within(current).getByRole('heading', { name: 'Trial' })).toBeInTheDocument();
    expect(within(current).getByText('Trial', { selector: '.state' })).toBeInTheDocument();
    expect(within(current).getByText('2 of 2')).toBeInTheDocument();
    expect(within(current).getByText('4.0 GB of 10.0 GB')).toBeInTheDocument();

    const starter = screen.getByRole('article', { name: 'Starter' });
    expect(within(starter).getByText(/29.*a month/)).toBeInTheDocument();
    expect(within(screen.getByRole('article', { name: 'Business' })).getByText('Unlimited cameras')).toBeInTheDocument();
    expect(within(screen.getByRole('article', { name: 'Trial' })).getByText('Your plan')).toBeInTheDocument();

    await user.click(within(starter).getByRole('button', { name: 'Choose Starter' }));
    await vi.waitFor(() => expect(leave).toHaveBeenCalledWith('https://checkout.stripe.test/cs_1'));
    expect(asked).toEqual({ planId: 'starter', returnUrl: `${window.location.origin}${window.location.pathname}` });
    client.signOut();
  });

  it('sends owners with a subscription to manage it at the provider', async () => {
    const leave = vi.spyOn(browser, 'leave').mockImplementation(() => undefined);
    const { user, client } = renderApp('/settings/billing?checkout=done', {
      ...signedIn('Owner'),
      'GET /api/billing': () => ({ body: { ...trial, plan: plans[1], state: 'PastDue', canManage: true, currentPeriodEnd: '2026-12-01T00:00:00Z' } }),
      'POST /api/billing/portal': () => ({ body: { url: 'https://billing.stripe.test/p/1' } }),
    });

    expect(await screen.findByText(/your plan changes as soon as/)).toBeInTheDocument();
    expect(screen.getByText(/A payment failed: update your card/)).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Manage billing' }));
    await vi.waitFor(() => expect(leave).toHaveBeenCalledWith('https://billing.stripe.test/p/1'));
    client.signOut();
  });

  it('lets administrators look, and tells them on every page when something needs doing', async () => {
    const { client } = renderApp('/settings/billing', {
      ...signedIn('Admin', { state: 'Canceled', trialEndsAt: null }),
      'GET /api/billing': () => ({ body: { ...trial, state: 'Canceled' } }),
    });

    expect(await screen.findByText('The organization\'s owners choose its plan.')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Choose/ })).not.toBeInTheDocument();
    expect(screen.getByText(/Your subscription has ended: no new cameras/)).toBeInTheDocument();
    client.signOut();
  });

  it('says when billing is not used', async () => {
    const { client } = renderApp('/settings/billing', { ...signedIn('Owner'), 'GET /api/billing': () => ({ body: { enabled: false } }) });
    expect(await screen.findByText('Billing is not used on this server.')).toBeInTheDocument();
    client.signOut();
  });
});

describe('subscription banner', () => {
  const owner = { ...admin, role: 'Owner' as const };
  const at = (state: string, trialEndsAt: string | null) => ({ ...owner, organization: { ...owner.organization, subscription: { state, trialEndsAt } } }) as never;
  const now = Date.parse('2026-11-20T12:00:00Z');

  it('warns of a trial ending within three days, a failed payment, or an end; nothing else', async () => {
    const { render } = await import('@testing-library/react');
    const { MemoryRouter } = await import('react-router-dom');
    const { SubscriptionBanner } = await import('./SubscriptionBanner');
    const text = (user: never) => render(<MemoryRouter><SubscriptionBanner user={user} now={now} /></MemoryRouter>).container.textContent;

    expect(text(at('Trialing', '2026-11-22T12:00:00Z'))).toContain('Your trial ends in 2 days.');
    expect(text(at('Trialing', '2026-11-30T12:00:00Z'))).toBe('');
    expect(text(at('PastDue', null))).toContain('A payment failed.');
    expect(text(at('Active', null))).toBe('');
    expect(text({ ...admin, role: 'Viewer', organization: { ...admin.organization, subscription: { state: 'PastDue', trialEndsAt: null } } } as never)).toBe('');
  });
});
