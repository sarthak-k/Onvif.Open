import { screen, within } from '@testing-library/react';
import { renderApp } from '../../test/renderApp';
import { admin, tenancyRoutes, tokens } from '../../test/fakeRecorder';

const cameraRoutes = {
  'GET /api/config/cameras': () => ({ body: [] }),
  'GET /api/cameras': () => ({ body: [] }),
};

describe('two-factor sign-in', () => {
  it('asks for the app code after the password, and can start over', async () => {
    const sent: unknown[] = [];
    const { user, calls, client } = renderApp(
      '/cameras',
      {
        ...tenancyRoutes,
        ...cameraRoutes,
        'POST /api/auth/login': () => ({ body: { twoFactorRequired: true, challenge: 'challenge-1', expiresIn: 300 } }),
        'POST /api/auth/login/two-factor': ({ body }) => {
          sent.push(body);
          return (body as { code: string }).code === '123 456' ? { body: tokens(1) } : { status: 401, body: { detail: 'That code is not right, or it took too long.' } };
        },
        'GET /api/auth/me': () => ({ body: { ...admin, twoFactor: 'On' } }),
      },
      null,
    );

    await user.type(await screen.findByLabelText('User name'), 'admin');
    await user.type(screen.getByLabelText('Password'), 'right');
    await user.click(screen.getByRole('button', { name: 'Sign in' }));

    // No session from the password alone.
    expect(await screen.findByRole('heading', { name: 'Enter your code' })).toBeInTheDocument();
    expect(calls.some((c) => c.path === '/api/auth/me')).toBe(false);

    await user.type(screen.getByLabelText('Code'), '000000');
    await user.click(screen.getByRole('button', { name: 'Sign in' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('That code is not right');

    // Starting over asks for the password again.
    await user.click(screen.getByRole('button', { name: 'Start over' }));
    expect(screen.getByLabelText('Password')).toHaveValue('');
    await user.type(screen.getByLabelText('Password'), 'right');
    await user.click(screen.getByRole('button', { name: 'Sign in' }));
    await user.type(await screen.findByLabelText('Code'), '123 456');
    await user.click(screen.getByRole('button', { name: 'Sign in' }));

    expect(await screen.findByRole('heading', { name: 'Cameras' })).toBeInTheDocument();
    expect(sent.at(-1)).toEqual({ challenge: 'challenge-1', code: '123 456' });
    client.signOut();
  });

  it('makes a member set it up first where the organization requires it, and shows the recovery codes once', async () => {
    let state: 'Off' | 'Pending' | 'On' = 'Off';
    let refreshed = 0;
    const { user, client } = renderApp('/cameras', {
      ...tenancyRoutes,
      ...cameraRoutes,
      'POST /api/auth/refresh': () => {
        refreshed++;
        return { body: tokens(refreshed) };
      },
      'GET /api/auth/me': () => ({ body: { ...admin, twoFactor: state, mustSetUpTwoFactor: state !== 'On', recoveryCodesLeft: state === 'On' ? 10 : 0 } }),
      'POST /api/auth/two-factor/setup': () => {
        state = 'Pending';
        return { body: { secret: 'JBSWY3DPEHPK3PXP', uri: 'otpauth://totp/Onvif.Open:admin?secret=JBSWY3DPEHPK3PXP&issuer=Onvif.Open' } };
      },
      'POST /api/auth/two-factor/enable': ({ body }) => {
        if ((body as { code: string }).code !== '654321') return { status: 400, body: { errors: ['That code is not right: enter the one the app shows now.'] } };
        state = 'On';
        return { body: { recoveryCodes: ['aaaa-bbbb-cccc', 'dddd-eeee-ffff'] } };
      },
    });

    expect(await screen.findByRole('heading', { name: 'Set up two-factor sign-in' })).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Set up two-factor sign-in' }));
    expect(await screen.findByRole('img', { name: 'QR code for your authenticator app' })).toBeInTheDocument();
    expect(screen.getByText('JBSW Y3DP EHPK 3PXP')).toBeInTheDocument();

    await user.type(screen.getByLabelText('Code from the app'), '111111');
    await user.click(screen.getByRole('button', { name: 'Turn on' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('That code is not right');
    await user.clear(screen.getByLabelText('Code from the app'));
    await user.type(screen.getByLabelText('Code from the app'), '654321');
    await user.click(screen.getByRole('button', { name: 'Turn on' }));

    // The session is renewed (its tokens said it had to be set up), then the codes, once.
    const codes = await screen.findByLabelText('Recovery codes');
    expect(within(codes).getByText('aaaa-bbbb-cccc')).toBeInTheDocument();
    expect(refreshed).toBeGreaterThanOrEqual(2);
    await user.click(screen.getByRole('button', { name: 'I saved them' }));
    expect(await screen.findByRole('heading', { name: 'Home' })).toBeInTheDocument();
    client.signOut();
  });

  it('turns it off from the account page with the password and a code, unless the organization requires it', async () => {
    let state = 'On';
    let off: unknown;
    const { user, client } = renderApp('/account', {
      ...tenancyRoutes,
      'POST /api/auth/refresh': () => ({ body: tokens(1) }),
      'GET /api/auth/me': () => ({ body: { ...admin, twoFactor: state, recoveryCodesLeft: 7 } }),
      'POST /api/auth/two-factor/disable': ({ body }) => {
        off = body;
        state = 'Off';
        return { status: 204 };
      },
    });

    expect(await screen.findByText(/7 recovery codes left/)).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Turn off' }));
    const form = screen.getByRole('form', { name: 'Turn off two-factor sign-in' });
    await user.type(within(form).getByLabelText('Password'), 'my-password');
    await user.type(within(form).getByLabelText('Code from the app'), '222333');
    await user.click(within(form).getByRole('button', { name: 'Turn off' }));

    expect(await screen.findByRole('button', { name: 'Set up two-factor sign-in' })).toBeInTheDocument();
    expect(off).toEqual({ password: 'my-password', code: '222333' });
    client.signOut();
  });

  it('keeps it on where the organization requires it', async () => {
    const { client } = renderApp('/account', {
      ...tenancyRoutes,
      'POST /api/auth/refresh': () => ({ body: tokens(1) }),
      'GET /api/auth/me': () => ({ body: { ...admin, twoFactor: 'On', recoveryCodesLeft: 10, organization: { ...admin.organization, requireTwoFactor: true } } }),
    });

    expect(await screen.findByText('Your organization requires two-factor sign-in.')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Turn off' })).not.toBeInTheDocument();
    client.signOut();
  });
});
