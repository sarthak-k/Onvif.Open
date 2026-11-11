import { screen, within } from '@testing-library/react';
import { renderApp } from '../../test/renderApp';
import { admin, lyonSite, tenancyRoutes, tokens } from '../../test/fakeRecorder';

const owner = { ...admin, id: 'u0', userName: 'olga', role: 'Owner' as const };

describe('users', () => {
  it('adds, changes and deletes users', async () => {
    const users = [admin, { ...admin, id: 'u2', userName: 'bob', role: 'Viewer' }, owner];
    const { user, calls, client } = renderApp('/settings/users', {
      ...tenancyRoutes,
      'POST /api/auth/refresh': () => ({ body: tokens(1) }),
      'GET /api/auth/me': () => ({ body: admin }),
      'GET /api/users': () => ({ body: users }),
      'POST /api/users': ({ body }) => ({ status: 201, body: { ...admin, id: 'u3', ...(body as object), mustChangePassword: true } }),
      'PUT /api/users/u2': ({ body }) => ({ body: { ...users[1], ...(body as object) } }),
      'DELETE /api/users/u2': () => ({ status: 204 }),
    });

    const bob = (await screen.findByText('bob')).closest('tr')!;
    expect(within(bob).getByText('All sites')).toBeInTheDocument();
    expect(within(screen.getByText('admin', { selector: 'td' }).closest('tr')!).getByRole('button', { name: 'Delete' })).toBeDisabled();

    // Administrators cannot change owners, nor make anyone an owner.
    const olga = screen.getByText('olga').closest('tr')!;
    expect(within(olga).getByLabelText('Role of olga')).toBeDisabled();
    expect(within(olga).getByRole('button', { name: 'Delete' })).toBeDisabled();
    expect(within(within(bob).getByLabelText('Role of bob')).queryByRole('option', { name: 'Owner' })).not.toBeInTheDocument();

    await user.selectOptions(within(bob).getByLabelText('Role of bob'), 'Operator');
    await user.click(within(bob).getByLabelText('bob enabled'));

    await user.click(within(bob).getByRole('button', { name: 'Change sites of bob' }));
    const sitesDialog = screen.getByRole('dialog');
    await user.click(within(sitesDialog).getByLabelText('All sites'));
    await user.click(within(sitesDialog).getByLabelText('Lyon'));
    await user.click(within(sitesDialog).getByLabelText('Main St'));
    await user.click(within(sitesDialog).getByRole('button', { name: 'Save' }));

    await user.click(within(bob).getByRole('button', { name: 'Reset password' }));
    await user.type(within(screen.getByRole('dialog')).getByLabelText('New password'), 'temporary-pass');
    await user.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Reset password' }));
    await user.click(within(bob).getByRole('button', { name: 'Delete' }));
    await user.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Delete' }));

    const form = screen.getByRole('form', { name: 'Add user' });
    await user.type(within(form).getByLabelText('User name'), 'carol');
    await user.type(within(form).getByLabelText('Initial password'), 'initial-pass');
    await user.selectOptions(within(form).getByLabelText('Role'), 'Admin');
    await user.click(within(form).getByLabelText('All sites'));
    await user.click(within(form).getByRole('button', { name: 'Add user' }));

    await vi.waitFor(() => expect(calls.some((c) => c.method === 'POST' && c.path === '/api/users')).toBe(true));
    const writes = calls.filter((c) => c.method !== 'GET' && c.path.startsWith('/api/users')).map((c) => [c.method, c.path, c.body]);
    expect(writes).toEqual([
      ['PUT', '/api/users/u2', { role: 'Operator' }],
      ['PUT', '/api/users/u2', { enabled: false }],
      ['PUT', '/api/users/u2', { sites: [lyonSite.id] }],
      ['PUT', '/api/users/u2', { password: 'temporary-pass' }],
      ['DELETE', '/api/users/u2', undefined],
      ['POST', '/api/users', { userName: 'carol', password: 'initial-pass', role: 'Admin', sites: ['site-main'] }],
    ]);
    client.signOut();
  });

  it('resets the two-factor sign-in of a member who lost their phone', async () => {
    let bob = { ...admin, id: 'u2', userName: 'bob', role: 'Viewer' as const, twoFactor: 'On' as const };
    const reset: string[] = [];
    const { user, client } = renderApp('/settings/users', {
      ...tenancyRoutes,
      'POST /api/auth/refresh': () => ({ body: tokens(1) }),
      'GET /api/auth/me': () => ({ body: admin }),
      'GET /api/users': () => ({ body: [admin, bob] }),
      'POST /api/users/u2/two-factor/reset': () => {
        reset.push('u2');
        bob = { ...bob, twoFactor: 'Off' as never };
        return { status: 204 };
      },
    });

    const row = (await screen.findByText('bob')).closest('tr')!;
    expect(within(row).getByText('two-factor')).toBeInTheDocument();
    await user.click(within(row).getByRole('button', { name: 'Reset two-factor' }));
    await user.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Reset' }));

    await vi.waitFor(() => expect(within(screen.getByText('bob').closest('tr')!).queryByText('two-factor')).not.toBeInTheDocument());
    expect(reset).toEqual(['u2']);
    client.signOut();
  });

  it('is for administrators who see every site', async () => {
    const { client } = renderApp('/settings/users', {
      ...tenancyRoutes,
      'POST /api/auth/refresh': () => ({ body: tokens(1) }),
      'GET /api/auth/me': () => ({ body: { ...admin, sites: [lyonSite.id] } }),
    });

    expect(await screen.findByRole('alert')).toHaveTextContent('This page is for administrators who see every site.');
    expect(screen.queryByRole('link', { name: 'Users' })).not.toBeInTheDocument();
    client.signOut();
  });
});
