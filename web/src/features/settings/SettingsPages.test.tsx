import { screen, within } from '@testing-library/react';
import { renderApp } from '../../test/renderApp';
import { admin, lyonSite, mainSite, organization, policies, tenancyRoutes, tokens } from '../../test/fakeRecorder';

const signedIn = (role = 'Admin') => ({
  ...tenancyRoutes,
  'POST /api/auth/refresh': () => ({ body: tokens(1) }),
  'GET /api/auth/me': () => ({ body: { ...admin, role } }),
});

describe('settings', () => {
  it('opens on sites and manages them', async () => {
    const writes: unknown[] = [];
    const { user, client } = renderApp('/settings', {
      ...signedIn(),
      'POST /api/sites': ({ body }) => {
        writes.push(['POST', body]);
        return { status: 201, body: { id: 'site-new', ...(body as object) } };
      },
      'PUT /api/sites/site-lyon': ({ body }) => {
        writes.push(['PUT', body]);
        return { body: { ...lyonSite, ...(body as object) } };
      },
      'DELETE /api/sites/site-main': () => ({ status: 409, body: { errors: ["Site 'Main St' has 3 camera(s): move or remove them first."] } }),
    });

    expect(await screen.findByRole('heading', { name: 'Sites' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Recording policies' })).toBeInTheDocument();
    expect(await screen.findByText('Europe/Paris')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Add site' }));
    let dialog = screen.getByRole('dialog');
    await user.type(within(dialog).getByLabelText('Name'), 'Airport');
    await user.selectOptions(within(dialog).getByLabelText('Time zone'), 'America/New_York');
    await user.click(within(dialog).getByRole('button', { name: 'Save' }));

    await user.click(within(screen.getByText('Lyon').closest('tr')!).getByRole('button', { name: 'Edit' }));
    dialog = screen.getByRole('dialog');
    await user.type(within(dialog).getByLabelText('Address'), '1 Quai');
    await user.click(within(dialog).getByRole('button', { name: 'Save' }));

    await user.click(within(screen.getByText(mainSite.name).closest('tr')!).getByRole('button', { name: 'Delete' }));
    await user.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Delete' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('has 3 camera(s)');

    expect(writes).toEqual([
      ['POST', { name: 'Airport', timeZone: 'America/New_York', address: null }],
      ['PUT', { name: 'Lyon', timeZone: 'Europe/Paris', address: '1 Quai' }],
    ]);
    client.signOut();
  });

  it('edits recording policies in plain words', async () => {
    let saved: unknown;
    const { user, client } = renderApp('/settings/policies', {
      ...signedIn(),
      'PUT /api/recording-policies/p-events': ({ body }) => {
        saved = body;
        return { body: { ...policies[1], ...(body as object) } };
      },
    });

    const events = (await screen.findByText('Events only')).closest('tr')!;
    expect(within(events).getByText('Records around events (5 s before, 10 s after) · keeps video 30 days')).toBeInTheDocument();
    expect(within(screen.getByText('Always record').closest('tr')!).getByRole('button', { name: 'Delete' })).toBeDisabled();

    await user.click(within(events).getByRole('button', { name: 'Edit' }));
    const dialog = screen.getByRole('dialog');
    expect(within(dialog).queryByLabelText('Keep video (days)')).not.toBeInTheDocument();
    await user.clear(within(dialog).getByLabelText('Keep video around events (days)'));
    await user.type(within(dialog).getByLabelText('Keep video around events (days)'), '60');
    await user.click(within(dialog).getByRole('button', { name: 'Save' }));

    await vi.waitFor(() => expect(saved).toMatchObject({ mode: 'EventsOnly', eventRetentionDays: 60, preRollSeconds: 5, postRollSeconds: 10 }));
    client.signOut();
  });

  it('shows the organization; only owners rename it', async () => {
    const { client } = renderApp('/settings/organization', { ...signedIn('Viewer'), 'GET /api/organization': () => ({ body: organization }) });

    expect(await screen.findByLabelText('Name')).toHaveValue('Riverside Shops');
    expect(screen.getByLabelText('Name')).toBeDisabled();
    expect(screen.getByText('United States')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Save' })).not.toBeInTheDocument();
    // Viewers see only the organization in settings.
    expect(screen.queryByRole('link', { name: 'Sites' })).not.toBeInTheDocument();
    client.signOut();
  });

  it('lets owners rename the organization', async () => {
    let renamed: unknown;
    const { user, client } = renderApp('/settings/organization', {
      ...signedIn('Owner'),
      'GET /api/organization': () => ({ body: organization }),
      'PUT /api/organization': ({ body }) => {
        renamed = body;
        return { body: { ...organization, ...(body as object) } };
      },
    });

    await user.clear(await screen.findByLabelText('Name'));
    await user.type(screen.getByLabelText('Name'), 'Riverside Group');
    await user.click(screen.getByRole('button', { name: 'Save' }));
    expect(await screen.findByRole('status')).toHaveTextContent('Saved.');
    expect(renamed).toEqual({ name: 'Riverside Group' });
    client.signOut();
  });
});
