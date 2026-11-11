import { screen, within } from '@testing-library/react';
import { renderApp } from '../../test/renderApp';
import { admin, tenancyRoutes, tokens } from '../../test/fakeRecorder';

const starter = { id: 'p-starter-1', code: 'starter', version: 1, name: 'Starter', cameras: 4, storageGB: 500, monthlyPrice: 29, providerPriceId: 'price_starter', sortOrder: 20, active: true, replacedById: null, retiredAt: null, organizations: 3 };
const old = { ...starter, id: 'p-starter-0', version: 0, monthlyPrice: 19, active: false, replacedById: 'p-starter-1', retiredAt: '2026-10-01T00:00:00Z', organizations: 7 };

const operator = (billingOperator: boolean) => ({
  ...tenancyRoutes,
  'POST /api/auth/refresh': () => ({ body: tokens(1) }),
  'GET /api/auth/me': () => ({ body: { ...admin, role: 'Owner', organization: { ...admin.organization, billingOperator } } }),
  'GET /api/billing': () => ({ body: { enabled: true, currency: 'EUR', plans: [] } }),
});

describe('plans', () => {
  it('lets the operator change a plan, warning that its customers keep the old version', async () => {
    let saved: unknown;
    const { user, client } = renderApp('/settings/plans', {
      ...operator(true),
      'GET /api/billing/plans': () => ({ body: [starter, old] }),
      'PUT /api/billing/plans/p-starter-1': ({ body }) => {
        saved = body;
        return { body: { ...starter, id: 'p-starter-2', version: 2 } };
      },
    });

    const row = await screen.findByRole('row', { name: 'Starter version 1' });
    expect(within(row).getByText('3')).toBeInTheDocument();
    expect(screen.queryByRole('row', { name: 'Starter version 0' })).not.toBeInTheDocument();
    await user.click(screen.getByLabelText('Show retired versions'));
    expect(within(screen.getByRole('row', { name: 'Starter version 0' })).getByText(/retired/)).toBeInTheDocument();

    await user.click(within(row).getByRole('button', { name: 'Change' }));
    const dialog = screen.getByRole('dialog');
    await user.clear(within(dialog).getByLabelText('Price a month'));
    await user.type(within(dialog).getByLabelText('Price a month'), '35');
    expect(within(dialog).getByText(/3 organization\(s\) are on this plan: they keep it as it is, and new customers get version 2/)).toBeInTheDocument();
    await user.click(within(dialog).getByRole('button', { name: 'Save' }));

    await vi.waitFor(() => expect(saved).toEqual({ name: 'Starter', cameras: 4, storageGB: 500, monthlyPrice: 35, providerPriceId: 'price_starter', sortOrder: 20 }));
    client.signOut();
  });

  it('is only for the organization that runs the service', async () => {
    const { client } = renderApp('/settings/plans', operator(false));
    expect(await screen.findByRole('alert')).toHaveTextContent('owners of the organization that runs this service');
    expect(screen.queryByRole('link', { name: 'Plans' })).not.toBeInTheDocument();
    client.signOut();
  });
});
