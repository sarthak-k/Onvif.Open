import { screen, within } from '@testing-library/react';
import { renderApp } from '../../test/renderApp';
import { admin, tenancyRoutes, tokens } from '../../test/fakeRecorder';
import { browser } from '../../shared/browser';

const noSso = { enabled: false, protocol: 'Oidc', samlEntityId: null, samlSsoUrl: null, samlCertificate: null, issuer: null, clientId: null, hasClientSecret: false, domains: [], autoCreate: false, defaultRole: 'Viewer', required: false };

describe('single sign-on', () => {
  afterEach(() => vi.restoreAllMocks());

  it("sends people of an organization's domain to its identity provider", async () => {
    const leave = vi.spyOn(browser, 'leave').mockImplementation(() => undefined);
    const { user } = renderApp(
      '/login',
      {
        ...tenancyRoutes,
        'GET /api/auth/sso': ({ query }) => ({ body: { available: (query.get('email') ?? '').endsWith('@riverside.test') } }),
      },
      null,
    );

    await user.click(await screen.findByRole('button', { name: 'Use single sign-on' }));
    await user.type(screen.getByLabelText('Work email'), 'ana@elsewhere.test');
    await user.click(screen.getByRole('button', { name: 'Continue' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('does not sign in with single sign-on');
    expect(leave).not.toHaveBeenCalled();

    await user.clear(screen.getByLabelText('Work email'));
    await user.type(screen.getByLabelText('Work email'), 'ana@riverside.test');
    await user.click(screen.getByRole('button', { name: 'Continue' }));
    await vi.waitFor(() => expect(leave).toHaveBeenCalledWith(`/api/auth/sso/start?email=ana%40riverside.test&returnTo=${encodeURIComponent(window.location.origin)}`));

    await user.click(screen.getByRole('button', { name: 'Use a password' }));
    expect(screen.getByLabelText('Password')).toBeInTheDocument();
  });

  it('says what went wrong when the provider sends people back with a problem', async () => {
    renderApp('/login?sso_error=There%20is%20no%20account%20for%20cleo%40riverside.test', tenancyRoutes, null);
    expect(await screen.findByRole('alert')).toHaveTextContent('There is no account for cleo@riverside.test');
    expect(screen.getByLabelText('Work email')).toBeInTheDocument();
  });

  it('signs in with the one-time code the provider sent back, once', async () => {
    const codes: unknown[] = [];
    const { client } = renderApp(
      '/sso#code=one-time',
      {
        ...tenancyRoutes,
        'POST /api/auth/sso/exchange': ({ body }) => {
          codes.push(body);
          return { body: tokens(1) };
        },
        'GET /api/auth/me': () => ({ body: { ...admin, singleSignOn: true } }),
      },
      null,
    );

    expect(await screen.findByRole('heading', { name: 'Home' })).toBeInTheDocument();
    expect(codes).toEqual([{ code: 'one-time' }]);
    client.signOut();
  });

  it('lets owners set up the identity provider', async () => {
    let saved: unknown;
    const { user, client } = renderApp('/settings/organization', {
      ...tenancyRoutes,
      'POST /api/auth/refresh': () => ({ body: tokens(1) }),
      'GET /api/auth/me': () => ({ body: { ...admin, role: 'Owner' } }),
      'GET /api/organization': () => ({ body: admin.organization }),
      'GET /api/organization/sso': () => ({ body: noSso }),
      'PUT /api/organization/sso': ({ body }) => {
        saved = body;
        return { body: { ...(body as object), clientSecret: undefined, hasClientSecret: true } };
      },
    });

    const form = await screen.findByRole('form', { name: 'Single sign-on' });
    expect(within(form).getByText(/\/api\/auth\/sso\/callback/)).toBeInTheDocument();
    await user.click(within(form).getByLabelText('Sign in with the identity provider'));
    await user.type(within(form).getByLabelText('Issuer'), 'https://login.example.com/tenant/v2.0');
    await user.type(within(form).getByLabelText('Client ID'), 'onvif-open');
    await user.type(within(form).getByLabelText('Client secret'), 's3cret');
    await user.type(within(form).getByLabelText('Email domains (one per line)'), 'riverside.test{enter}shops.riverside.test');
    await user.click(within(form).getByLabelText('Make accounts at the first sign-in, for these domains'));
    await user.selectOptions(within(form).getByLabelText('New accounts are'), 'Operator');
    await user.click(within(form).getByRole('button', { name: 'Save single sign-on' }));

    expect(await within(form).findByText('Saved.')).toBeInTheDocument();
    expect(saved).toEqual({
      enabled: true,
      protocol: 'Oidc',
      samlEntityId: null,
      samlSsoUrl: null,
      samlCertificate: null,
      issuer: 'https://login.example.com/tenant/v2.0',
      clientId: 'onvif-open',
      clientSecret: 's3cret',
      domains: ['riverside.test', 'shops.riverside.test'],
      autoCreate: true,
      defaultRole: 'Operator',
      required: false,
    });
    expect(within(form).getByLabelText('Client secret')).toHaveAttribute('placeholder', 'Stored. Leave empty to keep it.');
    client.signOut();
  });

  it('sets up a SAML provider, with what to register there', async () => {
    let saved: unknown;
    const { user, client } = renderApp('/settings/organization', {
      ...tenancyRoutes,
      'POST /api/auth/refresh': () => ({ body: tokens(1) }),
      'GET /api/auth/me': () => ({ body: { ...admin, role: 'Owner' } }),
      'GET /api/organization': () => ({ body: admin.organization }),
      'GET /api/organization/sso': () => ({ body: noSso }),
      'PUT /api/organization/sso': ({ body }) => {
        saved = body;
        return { body: { ...(body as object), hasClientSecret: false, samlCertificateSubject: 'CN=idp.example.com', samlCertificateExpires: '2027-11-01T00:00:00Z' } };
      },
    });

    const form = await screen.findByRole('form', { name: 'Single sign-on' });
    await user.click(within(form).getByLabelText('SAML 2.0'));
    expect(within(form).getByText(/\/api\/auth\/sso\/saml\/metadata/)).toBeInTheDocument();
    expect(within(form).queryByLabelText('Client ID')).not.toBeInTheDocument();
    await user.click(within(form).getByLabelText('Sign in with the identity provider'));
    await user.type(within(form).getByLabelText('Provider entity ID'), 'https://idp.example.com');
    await user.type(within(form).getByLabelText('Provider sign-in address'), 'https://idp.example.com/sso');
    await user.type(within(form).getByLabelText('Provider signing certificate'), 'MIIC');
    await user.type(within(form).getByLabelText('Email domains (one per line)'), 'riverside.test');
    await user.click(within(form).getByRole('button', { name: 'Save single sign-on' }));

    expect(await within(form).findByText(/CN=idp.example.com, valid until/)).toBeInTheDocument();
    expect(saved).toMatchObject({ enabled: true, protocol: 'Saml', samlEntityId: 'https://idp.example.com', samlSsoUrl: 'https://idp.example.com/sso', samlCertificate: 'MIIC', domains: ['riverside.test'] });
    client.signOut();
  });
});
