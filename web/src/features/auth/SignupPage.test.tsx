import { screen } from '@testing-library/react';
import { renderApp } from '../../test/renderApp';
import { admin, tenancyRoutes, tokens } from '../../test/fakeRecorder';

const cloud = {
  enabled: true,
  region: 'us',
  signup: true,
  regions: [
    { id: 'asia', name: 'Asia', webUrl: 'https://asia.cameras.example.com' },
    { id: 'us', name: 'United States', webUrl: 'https://us.cameras.example.com' },
  ],
};

describe('cloud sign-up', () => {
  it('offers an account from sign-in, emails a link and creates nothing until it is opened', async () => {
    let request: unknown;
    const { user } = renderApp(
      '/login',
      {
        ...tenancyRoutes,
        'GET /api/cloud': () => ({ body: cloud }),
        'POST /api/signup': ({ body }) => {
          request = body;
          return { status: 202, body: { message: 'Check your email.' } };
        },
      },
      null,
    );

    expect(await screen.findByLabelText('Email')).toBeInTheDocument();
    await user.click(screen.getByRole('link', { name: 'Create an account' }));
    expect(await screen.findByRole('heading', { name: 'Create an account' })).toBeInTheDocument();

    // Another region is another deployment.
    await user.click(screen.getByLabelText('Asia'));
    expect(screen.getByRole('link', { name: 'continue in Asia' })).toHaveAttribute('href', 'https://asia.cameras.example.com/signup');
    expect(screen.queryByLabelText('Email')).not.toBeInTheDocument();
    await user.click(screen.getByLabelText('United States'));

    await user.type(screen.getByLabelText('Organization name'), 'Riverside Shops');
    await user.type(screen.getByLabelText('Site name'), 'Main St');
    await user.selectOptions(screen.getByLabelText('Time zone'), 'America/New_York');
    await user.type(screen.getByLabelText('Email'), 'ada@example.com');
    await user.type(screen.getByLabelText('Password'), 'Shop-Owner-Pass-1');
    await user.type(screen.getByLabelText('Repeat password'), 'Shop-Owner-Pass-1');
    await user.click(screen.getByRole('button', { name: 'Create account' }));

    expect(await screen.findByRole('heading', { name: 'Check your email' })).toBeInTheDocument();
    expect(screen.getByText('ada@example.com')).toBeInTheDocument();
    expect(request).toEqual({ organizationName: 'Riverside Shops', siteName: 'Main St', timeZone: 'America/New_York', email: 'ada@example.com', password: 'Shop-Owner-Pass-1' });
  });

  it('signs the owner in from the emailed link, on a click, and goes on to the first camera', async () => {
    let token: unknown;
    const { user, client } = renderApp(
      '/signup/verify?token=abc',
      {
        ...tenancyRoutes,
        'GET /api/cloud': () => ({ body: cloud }),
        'POST /api/signup/verify': ({ body }) => {
          token = (body as { token: string }).token;
          return { body: tokens(1) };
        },
        'GET /api/auth/me': () => ({ body: { ...admin, userName: 'ada@example.com', role: 'Owner' } }),
        'GET /api/config/cameras': () => ({ body: [] }),
        'GET /api/cameras': () => ({ body: [] }),
      },
      null,
    );

    // Opening the page spends nothing.
    await user.click(await screen.findByRole('button', { name: 'Open my organization' }));
    expect(await screen.findByRole('heading', { name: 'Add cameras' })).toBeInTheDocument();
    expect(token).toBe('abc');
    client.signOut();
  });

  it('says when a link has expired or was used', async () => {
    const { user } = renderApp(
      '/signup/verify?token=old',
      { ...tenancyRoutes, 'POST /api/signup/verify': () => ({ status: 404, body: { errors: ['This link has expired or was used already: sign up again, or sign in.'] } }) },
      null,
    );

    await user.click(await screen.findByRole('button', { name: 'Open my organization' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('This link has expired');
    expect(screen.getByRole('link', { name: 'Sign up again' })).toBeInTheDocument();
  });

  it('is closed where people cannot sign up', async () => {
    renderApp('/signup', tenancyRoutes, null);
    expect(await screen.findByRole('heading', { name: 'Sign in' })).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Create an account' })).not.toBeInTheDocument();
  });
});
