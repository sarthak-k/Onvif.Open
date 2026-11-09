import { screen } from '@testing-library/react';
import { renderApp } from '../../test/renderApp';
import { admin, tenancyRoutes, tokens } from '../../test/fakeRecorder';

describe('sign in', () => {
  it('signs in and opens the cameras', async () => {
    const { user, calls, client } = renderApp(
      '/cameras',
      {
        ...tenancyRoutes,
        'POST /api/auth/login': ({ body }) => ((body as { password: string }).password === 'right' ? { body: tokens(1) } : { status: 401, body: { detail: 'Wrong user name or password.' } }),
        'GET /api/auth/me': () => ({ body: admin }),
        'GET /api/config/cameras': () => ({ body: [] }),
        'GET /api/cameras': () => ({ body: [] }),
      },
      null,
    );

    await user.type(await screen.findByLabelText('User name'), 'admin');
    await user.type(screen.getByLabelText('Password'), 'wrong');
    await user.click(screen.getByRole('button', { name: 'Sign in' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('Wrong user name or password.');

    await user.clear(screen.getByLabelText('Password'));
    await user.type(screen.getByLabelText('Password'), 'right');
    await user.click(screen.getByRole('button', { name: 'Sign in' }));

    expect(await screen.findByRole('heading', { name: 'Cameras' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Settings' })).toBeInTheDocument();
    expect(calls.some((c) => c.path === '/api/cameras' && c.token === 'access-1')).toBe(true);
    client.signOut();
  });

  it('requires a new password first when the administrator set it', async () => {
    let mustChange = true;
    const { user, client } = renderApp('/cameras', {
      ...tenancyRoutes,
      'POST /api/auth/refresh': () => ({ body: tokens(1) }),
      'GET /api/auth/me': () => ({ body: { ...admin, mustChangePassword: mustChange } }),
      'POST /api/auth/password': ({ body }) => {
        expect(body).toEqual({ currentPassword: 'generated', newPassword: 'a-new-password' });
        mustChange = false;
        return { body: tokens(2) };
      },
      'GET /api/config/cameras': () => ({ body: [] }),
      'GET /api/cameras': () => ({ body: [] }),
    });

    expect(await screen.findByRole('heading', { name: 'Choose a new password' })).toBeInTheDocument();
    await user.type(screen.getByLabelText('Current password'), 'generated');
    await user.type(screen.getByLabelText('New password'), 'a-new-password');
    await user.type(screen.getByLabelText('Repeat new password'), 'a-new-password');
    await user.click(screen.getByRole('button', { name: 'Change password' }));

    expect(await screen.findByRole('heading', { name: 'Home' })).toBeInTheDocument();
    client.signOut();
  });

  it('signs out', async () => {
    const { user } = renderApp('/account', {
      ...tenancyRoutes,
      'POST /api/auth/refresh': () => ({ body: tokens(1) }),
      'GET /api/auth/me': () => ({ body: admin }),
    });

    await user.click(await screen.findByRole('button', { name: 'Sign out' }));
    expect(await screen.findByRole('heading', { name: 'Sign in' })).toBeInTheDocument();
  });
});
