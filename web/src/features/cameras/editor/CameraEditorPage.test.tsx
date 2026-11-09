import { screen, within } from '@testing-library/react';
import { renderApp } from '../../../test/renderApp';
import { admin, lyonSite, mainSite, tenancyRoutes, tokens } from '../../../test/fakeRecorder';

const signedIn = {
  ...tenancyRoutes,
  'POST /api/auth/refresh': () => ({ body: tokens(1) }),
  'GET /api/auth/me': () => ({ body: admin }),
  'GET /api/config/cameras': () => ({ body: [{ id: 'c-old', name: 'Lobby', siteId: mainSite.id }] }),
  'GET /api/cameras': () => ({ body: [] }),
};

describe('camera editor', () => {
  it('adds a discovered camera after testing the connection', async () => {
    let saved: unknown;
    const { user, calls, client } = renderApp('/cameras/new', {
      ...signedIn,
      'GET /api/discovery': () => ({
        body: [
          { name: 'Lobby', deviceServiceUri: 'http://10.0.0.7/onvif/device_service', serviceAddresses: [], configuredCameraId: null },
          { name: 'Old', deviceServiceUri: 'http://10.0.0.8/onvif/device_service', serviceAddresses: [], configuredCameraId: 'c-old', configuredCameraName: 'Lobby' },
        ],
      }),
      'POST /api/config/cameras/test': () => ({
        body: {
          ok: true,
          device: { manufacturer: 'Acme', model: 'X1', firmwareVersion: '2.1' },
          capabilities: { ptz: true, imaging: true, analytics: false, replay: false, deviceIo: false },
          profiles: [
            { token: 'main', name: 'Main', encoding: 'H264', width: 1920, height: 1080, frameRate: 25, ptz: true, analytics: false },
            { token: 'sub', name: 'Sub', encoding: 'H264', width: 640, height: 360, frameRate: 15, ptz: true, analytics: false },
          ],
        },
      }),
      'POST /api/config/cameras': ({ body }) => {
        saved = body;
        return { status: 201, body: { ...(body as object), id: 'c-new' } };
      },
    });

    await user.click(await screen.findByRole('button', { name: 'Search' }));
    expect(await screen.findByText('added as Lobby')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Use' }));

    // A free name from the device, the first site, and the default policy.
    expect(screen.getByLabelText('Name')).toHaveValue('Lobby 2');
    expect(screen.getByLabelText('Site')).toHaveValue(mainSite.id);
    expect(screen.getByLabelText('Recording policy')).toHaveValue('p-always');
    expect(screen.getByText(/Records always · keeps video 7 days, around events 30 days/)).toBeInTheDocument();
    expect(screen.getByLabelText('Device service URL')).toHaveValue('http://10.0.0.7/onvif/device_service');

    await user.selectOptions(screen.getByLabelText('Site'), lyonSite.id);
    await user.type(screen.getByLabelText('User name'), 'admin');
    await user.type(screen.getByLabelText('Password'), 'secret');
    await user.click(screen.getByRole('button', { name: 'Test connection' }));

    const report = await screen.findByRole('status');
    expect(report).toHaveTextContent('Acme X1, firmware 2.1');
    await user.click(within(report).getByLabelText('Record profile Sub'));

    await user.click(screen.getByRole('button', { name: 'Add camera' }));
    expect(await screen.findByRole('heading', { name: 'Cameras' })).toBeInTheDocument();
    expect(saved).toMatchObject({ name: 'Lobby 2', siteId: lyonSite.id, policyId: null, onvifUrl: 'http://10.0.0.7/onvif/device_service', username: 'admin', password: 'secret', profileToken: 'sub' });
    expect(saved).not.toHaveProperty('id');
    expect(calls.find((c) => c.path === '/api/config/cameras/test')?.body).toMatchObject({ password: 'secret' });
    client.signOut();
  });

  it('explains a failed connection test', async () => {
    const { user, client } = renderApp('/cameras/new', {
      ...signedIn,
      'POST /api/config/cameras/test': () => ({ body: { ok: false, kind: 'unauthorized', error: 'The camera refused the user name or password.' } }),
    });

    await user.type(await screen.findByLabelText('Device service URL'), 'http://10.0.0.7/onvif/device_service');
    await user.click(screen.getByRole('button', { name: 'Test connection' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('refused the user name or password');
    client.signOut();
  });

  it('renames a camera, changes its policy and keeps its stored password', async () => {
    let saved: Record<string, unknown> | undefined;
    const { user, calls, client } = renderApp('/cameras/c-gate', {
      ...signedIn,
      'GET /api/config/cameras/c-gate': () => ({
        body: {
          id: 'c-gate',
          name: 'Gate',
          siteId: mainSite.id,
          policyId: 'p-always',
          enabled: true,
          rtspUrl: 'rtsp://10.0.0.9/s1',
          hasPassword: true,
          transport: 'Rtsp',
          mediaVersion: 'Auto',
          mode: 'Continuous',
          events: { enabled: true, triggerTopics: [] },
          analytics: { enabled: false, triggerClasses: [], minLikelihood: 0.5 },
          audio: { enabled: false },
        },
      }),
      'POST /api/config/cameras/test': () => ({ body: { ok: true, stream: { codec: 'H264' } } }),
      'PUT /api/config/cameras/c-gate': ({ body }) => {
        saved = body as Record<string, unknown>;
        return { body };
      },
    });

    expect(await screen.findByRole('heading', { name: 'Gate' })).toBeInTheDocument();
    expect(screen.getByLabelText('RTSP URL')).toHaveValue('rtsp://10.0.0.9/s1');
    await user.clear(screen.getByLabelText('Name'));
    await user.type(screen.getByLabelText('Name'), 'Main gate');
    await user.selectOptions(screen.getByLabelText('Recording policy'), 'p-events');
    expect(screen.getByText(/Records around events \(5 s before, 10 s after\)/)).toBeInTheDocument();

    // The connection test of a saved camera sends its id, so the stored password is used.
    await user.click(screen.getByRole('button', { name: 'Test connection' }));
    await screen.findByRole('status');
    expect(calls.find((c) => c.path === '/api/config/cameras/test')?.body).toMatchObject({ id: 'c-gate', password: null });

    await user.click(screen.getByRole('button', { name: 'Save' }));
    expect(await screen.findByRole('heading', { name: 'Cameras' })).toBeInTheDocument();
    expect(saved).toMatchObject({ name: 'Main gate', policyId: 'p-events', password: null, onvifUrl: null });
    client.signOut();
  });

  it('shows the recorder validation errors', async () => {
    const { user, client } = renderApp('/cameras/new', {
      ...signedIn,
      'POST /api/config/cameras': () => ({ status: 400, body: { errors: ["Camera 'x': OnvifUrl is not a valid URL."] } }),
    });

    await user.type(await screen.findByLabelText('Name'), 'x');
    await user.type(screen.getByLabelText('Device service URL'), 'http://x');
    await user.click(screen.getByRole('button', { name: 'Add camera' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('OnvifUrl is not a valid URL.');
    client.signOut();
  });
});
