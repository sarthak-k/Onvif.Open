import { screen, within } from '@testing-library/react';
import { renderApp } from '../test/renderApp';
import { admin, tokens } from '../test/fakeRecorder';

describe('users', () => {
  it('adds, changes and deletes users', async () => {
    const users = [admin, { ...admin, id: 'u2', userName: 'bob', role: 'Viewer' }];
    const { user, calls, client } = renderApp('/users', {
      'POST /api/auth/refresh': () => ({ body: tokens(1) }),
      'GET /api/auth/me': () => ({ body: admin }),
      'GET /api/users': () => ({ body: users }),
      'POST /api/users': ({ body }) => ({ status: 201, body: { ...admin, id: 'u3', ...(body as object), mustChangePassword: true } }),
      'PUT /api/users/u2': ({ body }) => ({ body: { ...users[1], ...(body as object) } }),
      'DELETE /api/users/u2': () => ({ status: 204 }),
    });

    const bob = (await screen.findByText('bob')).closest('tr')!;
    expect(within(screen.getByText('admin', { selector: 'td' }).closest('tr')!).getByRole('button', { name: 'Delete' })).toBeDisabled();

    await user.selectOptions(within(bob).getByLabelText('Role of bob'), 'Operator');
    await user.click(within(bob).getByLabelText('bob enabled'));
    await user.click(within(bob).getByRole('button', { name: 'Reset password' }));
    await user.type(within(screen.getByRole('dialog')).getByLabelText('New password'), 'temporary-pass');
    await user.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Reset password' }));
    await user.click(within(bob).getByRole('button', { name: 'Delete' }));
    await user.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Delete' }));

    const form = screen.getByRole('form', { name: 'Add user' });
    await user.type(within(form).getByLabelText('User name'), 'carol');
    await user.type(within(form).getByLabelText('Initial password'), 'initial-pass');
    await user.selectOptions(within(form).getByLabelText('Role'), 'Admin');
    await user.click(within(form).getByRole('button', { name: 'Add user' }));

    await vi.waitFor(() => expect(calls.some((c) => c.method === 'POST' && c.path === '/api/users')).toBe(true));
    const writes = calls.filter((c) => c.method !== 'GET' && c.path.startsWith('/api/users')).map((c) => [c.method, c.path, c.body]);
    expect(writes).toEqual([
      ['PUT', '/api/users/u2', { role: 'Operator' }],
      ['PUT', '/api/users/u2', { enabled: false }],
      ['PUT', '/api/users/u2', { password: 'temporary-pass' }],
      ['DELETE', '/api/users/u2', undefined],
      ['POST', '/api/users', { userName: 'carol', password: 'initial-pass', role: 'Admin' }],
    ]);
    client.signOut();
  });

  it('is for administrators only', async () => {
    const { client } = renderApp('/users', {
      'POST /api/auth/refresh': () => ({ body: tokens(1) }),
      'GET /api/auth/me': () => ({ body: { ...admin, role: 'Operator' } }),
    });

    expect(await screen.findByRole('alert')).toHaveTextContent('This page needs the Admin role.');
    client.signOut();
  });
});
