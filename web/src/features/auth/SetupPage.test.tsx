import { screen } from '@testing-library/react';
import { renderApp } from '../../test/renderApp';
import { admin, tenancyRoutes, tokens } from '../../test/fakeRecorder';

const required = { required: true, tokenRequired: false, regions: [{ id: 'asia', name: 'Asia' }, { id: 'us', name: 'United States' }] };

describe('first-run setup', () => {
  it('sends a new recorder from sign-in to setup, then to its first camera', async () => {
    let request: unknown;
    const { user, client } = renderApp(
      '/login',
      {
        ...tenancyRoutes,
        'GET /api/setup': () => ({ body: required }),
        'POST /api/setup': ({ body }) => {
          request = body;
          return { body: tokens(1) };
        },
        'GET /api/auth/me': () => ({ body: { ...admin, role: 'Owner' } }),
        'GET /api/config/cameras': () => ({ body: [] }),
        'GET /api/cameras': () => ({ body: [] }),
      },
      null,
    );

    expect(await screen.findByRole('heading', { name: 'Set up Onvif.Open' })).toBeInTheDocument();
    await user.type(screen.getByLabelText('Organization name'), 'Riverside Shops');
    await user.click(screen.getByLabelText('Asia'));
    await user.type(screen.getByLabelText('Site name'), 'Head office');
    await user.selectOptions(screen.getByLabelText('Time zone'), 'America/New_York');
    await user.type(screen.getByLabelText('User name'), 'dana');
    await user.type(screen.getByLabelText('Password'), 'Dana-Owner-Pass-1');
    await user.type(screen.getByLabelText('Repeat password'), 'Dana-Owner-Pass-1');
    await user.click(screen.getByRole('button', { name: 'Create organization' }));

    expect(await screen.findByRole('heading', { name: 'Add camera' })).toBeInTheDocument();
    expect(request).toEqual({ organizationName: 'Riverside Shops', region: 'asia', siteName: 'Head office', timeZone: 'America/New_York', userName: 'dana', password: 'Dana-Owner-Pass-1' });
    client.signOut();
  });

  it('asks for the setup token when the recorder requires one, and checks the passwords match', async () => {
    const { user } = renderApp('/setup', { ...tenancyRoutes, 'GET /api/setup': () => ({ body: { ...required, tokenRequired: true } }) }, null);

    expect(await screen.findByLabelText('Setup token')).toBeInTheDocument();
    await user.type(screen.getByLabelText('Organization name'), 'X');
    await user.type(screen.getByLabelText('Site name'), 'Y');
    await user.type(screen.getByLabelText('User name'), 'dana');
    await user.type(screen.getByLabelText('Password'), 'one-password');
    await user.type(screen.getByLabelText('Repeat password'), 'another-password');
    await user.type(screen.getByLabelText('Setup token'), 't');
    await user.click(screen.getByRole('button', { name: 'Create organization' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('The passwords do not match.');
  });

  it('is closed once the recorder is set up', async () => {
    renderApp('/setup', tenancyRoutes, null);
    expect(await screen.findByRole('heading', { name: 'Sign in' })).toBeInTheDocument();
  });
});
