import { screen, within } from '@testing-library/react';
import { renderApp } from '../../../test/renderApp';
import { admin, tenancyRoutes, tokens } from '../../../test/fakeRecorder';
import { describeProfile, eachLimited, fromAddress, withProbe } from './candidates';

const lobby = 'http://10.0.0.11/onvif/device_service';
const dock = 'http://10.0.0.12/onvif/device_service';
const picture = 'data:image/jpeg;base64,/9j/2Q==';
const profiles = [
  { token: 'main', name: 'mainStream', encoding: 'H264', width: 1920, height: 1080, frameRate: 25, ptz: false, analytics: false },
  { token: 'sub', name: 'subStream', encoding: 'H264', width: 640, height: 360, frameRate: 15, ptz: false, analytics: false },
];

describe('add cameras', () => {
  it('finds cameras, signs in once, and adds them named and with a policy', async () => {
    const created: unknown[] = [];
    const tested: unknown[] = [];
    const { user, client } = renderApp('/cameras/add', {
      ...tenancyRoutes,
      'POST /api/auth/refresh': () => ({ body: tokens(1) }),
      'GET /api/auth/me': () => ({ body: admin }),
      'GET /api/config/cameras': () => ({ body: [] }),
      'GET /api/discovery': () => ({
        body: [
          { name: 'Lobby', hardware: 'IPC-1', deviceServiceUri: lobby, serviceAddresses: [] },
          { name: 'Dock', hardware: 'IPC-2', deviceServiceUri: dock, serviceAddresses: [] },
          { name: 'Gate', deviceServiceUri: 'http://10.0.0.13/onvif/device_service', serviceAddresses: [], configuredCameraId: 'cam-gate' },
        ],
      }),
      'POST /api/config/cameras/test': ({ body, query }) => {
        const camera = body as { onvifUrl: string; username: string; password: string };
        tested.push([camera.onvifUrl, camera.password, query.get('snapshot')]);
        const ok = camera.onvifUrl === lobby ? camera.password === 'shared-pass' : camera.password === 'dock-pass';
        return { body: ok ? { ok: true, profiles, recommended: { mainProfile: 'main', subProfile: 'sub' }, snapshot: picture } : { ok: false, kind: 'unauthorized', error: 'Refused' } };
      },
      'POST /api/config/cameras': ({ body }) => {
        created.push(body);
        return { status: 201, body: { ...(body as object), id: `id-${created.length}` } };
      },
    });

    // 1. What the gateway found, without the camera already added; one more by address, then left out.
    expect(await screen.findByText('Lobby')).toBeInTheDocument();
    expect(screen.getByText('Dock')).toBeInTheDocument();
    expect(screen.queryByText('Gate')).not.toBeInTheDocument();
    await user.type(screen.getByLabelText('Camera address'), '10.0.5.20');
    await user.click(screen.getByRole('button', { name: 'Add to the list' }));
    await user.click(screen.getByRole('checkbox', { name: /10\.0\.5\.20/ }));
    await user.click(screen.getByRole('button', { name: 'Continue with 2 cameras' }));

    // 2. One shared password; the camera that refuses it gets its own.
    await user.type(screen.getByLabelText('Password'), 'shared-pass');
    await user.click(screen.getByRole('button', { name: 'Test cameras' }));
    const dockCard = await screen.findByRole('listitem', { name: 'Dock' });
    expect(await within(dockCard).findByText('Wrong user name or password')).toBeInTheDocument();
    expect(within(screen.getByRole('listitem', { name: 'Lobby' })).getByText('Ready')).toBeInTheDocument();
    expect(within(screen.getByRole('listitem', { name: 'Lobby' })).getByRole('img')).toHaveAttribute('src', picture);

    await user.click(within(dockCard).getByRole('button', { name: 'Use its own password' }));
    await user.type(within(dockCard).getByLabelText('Password for Dock'), 'dock-pass');
    await user.click(within(dockCard).getByRole('button', { name: 'Try again' }));
    expect(await within(dockCard).findByText('Ready')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Continue with 2 cameras' }));

    // 3. Names, sites and policies; the stream is chosen unless changed under Advanced.
    const name = screen.getByLabelText('Name of Lobby');
    await user.clear(name);
    await user.type(name, 'Front lobby');
    await user.selectOptions(screen.getByLabelText('Site of Dock'), 'site-lyon');
    await user.selectOptions(screen.getByLabelText('Recording of Dock'), 'p-events');
    await user.click(within(screen.getByRole('listitem', { name: 'Dock' })).getByText('Advanced'));
    await user.selectOptions(screen.getByLabelText('Stream to record for Dock'), 'sub');
    await user.click(screen.getByRole('button', { name: 'Add 2 cameras' }));

    // 4. Done.
    expect(await screen.findByText('2 cameras are recording: Front lobby, Dock.')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Watch live' })).toHaveAttribute('href', '/live');

    expect(tested).toEqual([
      [lobby, 'shared-pass', 'true'],
      [dock, 'shared-pass', 'true'],
      [dock, 'dock-pass', 'true'],
    ]);
    expect(created).toMatchObject([
      { name: 'Front lobby', siteId: 'site-main', onvifUrl: lobby, username: 'admin', password: 'shared-pass', profileToken: 'main', subProfileToken: 'sub', enabled: true },
      { name: 'Dock', siteId: 'site-lyon', policyId: 'p-events', onvifUrl: dock, username: 'admin', password: 'dock-pass', profileToken: 'sub', subProfileToken: null },
    ]);
    expect((created[0] as { policyId?: string | null }).policyId ?? null).toBeNull();
    client.signOut();
  });

  it('keeps cameras it cannot reach out of the next step', async () => {
    const { user, client } = renderApp('/cameras/add', {
      ...tenancyRoutes,
      'POST /api/auth/refresh': () => ({ body: tokens(1) }),
      'GET /api/auth/me': () => ({ body: admin }),
      'GET /api/config/cameras': () => ({ body: [] }),
      'GET /api/discovery': () => ({ body: [] }),
      'POST /api/config/cameras/test': () => ({ body: { ok: false, kind: 'unreachable', error: 'No answer within 15 seconds.' } }),
    });

    expect(await screen.findByText('No new cameras answered. Add one by its address below.')).toBeInTheDocument();
    await user.type(screen.getByLabelText('Camera address'), '10.0.5.20:8080');
    await user.click(screen.getByRole('button', { name: 'Add to the list' }));
    await user.click(screen.getByRole('button', { name: 'Continue with 1 camera' }));
    await user.click(screen.getByRole('button', { name: 'Test cameras' }));

    expect(await screen.findByText('Cannot be reached')).toBeInTheDocument();
    expect(screen.getByText('No answer within 15 seconds.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Test the cameras first' })).toBeDisabled();
    client.signOut();
  });

  it('turns addresses and test results into cameras', async () => {
    expect(fromAddress('10.0.5.20', [])?.onvifUrl).toBe('http://10.0.5.20/onvif/device_service');
    expect(fromAddress('https://cam.example/onvif/device_service', [])?.onvifUrl).toBe('https://cam.example/onvif/device_service');
    expect(fromAddress('  ', [])).toBeNull();
    expect(fromAddress('10.0.5.20', ['10.0.5.20'])?.name).toBe('10.0.5.20 2');

    const candidate = fromAddress('10.0.5.20', [])!;
    expect(withProbe(candidate, { ok: false, kind: 'unauthorized', error: 'no' }).state).toBe('unauthorized');
    expect(withProbe({ ...candidate, profileToken: 'mine' }, { ok: true, recommended: { mainProfile: 'main' } }).profileToken).toBe('mine');
    expect(describeProfile(profiles[0])).toBe('1920×1080 H264, 25 fps');

    let running = 0;
    let most = 0;
    await eachLimited([1, 2, 3, 4, 5, 6], 2, async () => {
      most = Math.max(most, ++running);
      await new Promise((resolve) => setTimeout(resolve, 5));
      running--;
    });
    expect(most).toBe(2);
  });
});
