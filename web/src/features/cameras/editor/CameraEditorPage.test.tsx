import { screen, within } from '@testing-library/react';
import { renderApp } from '../../../test/renderApp';
import { admin, tokens } from '../../../test/fakeRecorder';

const signedIn = {
  'POST /api/auth/refresh': () => ({ body: tokens(1) }),
  'GET /api/auth/me': () => ({ body: admin }),
  'GET /api/config/cameras': () => ({ body: [{ id: 'cam' }] }),
  'GET /api/cameras': () => ({ body: [] }),
};

describe('camera editor', () => {
  it('adds a discovered camera after testing the connection', async () => {
    let saved: unknown;
    const { user, calls, client } = renderApp('/cameras/new', {
      ...signedIn,
      'GET /api/discovery': () => ({
        body: [
          { name: 'Cam', deviceServiceUri: 'http://10.0.0.7/onvif/device_service', serviceAddresses: [], configuredCameraId: null },
          { name: 'Old', deviceServiceUri: 'http://10.0.0.8/onvif/device_service', serviceAddresses: [], configuredCameraId: 'cam' },
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
        return { status: 201, body };
      },
    });

    await user.click(await screen.findByRole('button', { name: 'Search' }));
    expect(await screen.findByText('added as cam')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Use' }));

    expect(screen.getByLabelText('Id')).toHaveValue('cam-2');
    expect(screen.getByLabelText('Device service URL')).toHaveValue('http://10.0.0.7/onvif/device_service');

    await user.type(screen.getByLabelText('User name'), 'admin');
    await user.type(screen.getByLabelText('Password'), 'secret');
    await user.click(screen.getByRole('button', { name: 'Test connection' }));

    const report = await screen.findByRole('status');
    expect(report).toHaveTextContent('Acme X1, firmware 2.1');
    expect(report).toHaveTextContent('Supports: PTZ, Imaging');
    await user.click(within(report).getByLabelText('Record profile Sub'));
    expect(screen.getByLabelText('Media profile token')).toHaveValue('sub');

    await user.click(screen.getByRole('button', { name: 'Add camera' }));
    expect(await screen.findByRole('heading', { name: 'Cameras' })).toBeInTheDocument();
    expect(saved).toMatchObject({ id: 'cam-2', onvifUrl: 'http://10.0.0.7/onvif/device_service', username: 'admin', password: 'secret', profileToken: 'sub' });
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

  it('edits a camera and keeps its stored password', async () => {
    let saved: Record<string, unknown> | undefined;
    const { user, client } = renderApp('/cameras/gate', {
      ...signedIn,
      'GET /api/config/cameras/gate': () => ({
        body: {
          id: 'gate',
          enabled: true,
          rtspUrl: 'rtsp://10.0.0.9/s1',
          hasPassword: true,
          transport: 'Rtsp',
          mediaVersion: 'Auto',
          mode: 'Continuous',
          preRoll: '00:00:05',
          postRoll: '00:00:10',
          events: { enabled: true, triggerTopics: [] },
          analytics: { enabled: false, triggerClasses: [], minLikelihood: 0.5 },
          audio: { enabled: false },
        },
      }),
      'PUT /api/config/cameras/gate': ({ body }) => {
        saved = body as Record<string, unknown>;
        return { body };
      },
    });

    expect(await screen.findByLabelText('RTSP URL')).toHaveValue('rtsp://10.0.0.9/s1');
    expect(screen.getByLabelText('Id')).toBeDisabled();
    await user.click(screen.getByLabelText('On events and detections only'));
    await user.clear(screen.getByLabelText('Post-roll (seconds)'));
    await user.type(screen.getByLabelText('Post-roll (seconds)'), '30');
    await user.click(screen.getByRole('button', { name: 'Save' }));

    expect(await screen.findByRole('heading', { name: 'Cameras' })).toBeInTheDocument();
    expect(saved).toMatchObject({ mode: 'Triggered', postRoll: '00:00:30', password: null, onvifUrl: null });
    client.signOut();
  });

  it('shows the recorder validation errors', async () => {
    const { user, client } = renderApp('/cameras/new', {
      ...signedIn,
      'POST /api/config/cameras': () => ({ status: 400, body: { errors: ["Camera 'x': OnvifUrl is not a valid URL."] } }),
    });

    await user.type(await screen.findByLabelText('Id'), 'x');
    await user.type(screen.getByLabelText('Device service URL'), 'http://x');
    await user.click(screen.getByRole('button', { name: 'Add camera' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('OnvifUrl is not a valid URL.');
    client.signOut();
  });
});
