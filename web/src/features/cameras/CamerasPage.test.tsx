import { screen, within } from '@testing-library/react';
import { renderApp } from '../../test/renderApp';
import { admin, mainSite, tenancyRoutes, tokens } from '../../test/fakeRecorder';

const config = {
  id: 'front',
  name: 'Front door',
  siteId: mainSite.id,
  policyId: 'p-always',
  enabled: true,
  onvifUrl: 'http://10.0.0.5/onvif/device_service',
  hasPassword: true,
  transport: 'Rtsp',
  mediaVersion: 'Auto',
  mode: 'Continuous',
  preRoll: '00:00:05',
  postRoll: '00:00:10',
  events: { enabled: true, triggerTopics: [] },
  analytics: { enabled: false, triggerClasses: [], minLikelihood: 0.5 },
  audio: { enabled: false },
};
const status = { cameraId: 'front', name: 'Front door', siteId: mainSite.id, state: 'Recording', mode: 'Continuous', isTriggered: false, codec: 'H264', recordsAudio: false, framesRecorded: 10, bytesRecorded: 2048, segmentsCompleted: 0 };

const signedIn = (role: string) => ({
  ...tenancyRoutes,
  'POST /api/auth/refresh': () => ({ body: tokens(1) }),
  'GET /api/auth/me': () => ({ body: { ...admin, role } }),
  'GET /api/cameras': () => ({ body: [status] }),
});

describe('cameras', () => {
  it('shows configuration and status to administrators', async () => {
    const { client } = renderApp('/cameras', { ...signedIn('Admin'), 'GET /api/config/cameras': () => ({ body: [config] }) });

    const row = (await screen.findByRole('link', { name: 'Front door' })).closest('tr')!;
    expect(within(row).getByText('Main St')).toBeInTheDocument();
    expect(within(row).getByText('ONVIF 10.0.0.5')).toBeInTheDocument();
    expect(within(row).getByText('Recording')).toBeInTheDocument();
    expect(within(row).getByText('2.0 KB')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Add camera' })).toBeInTheDocument();
    client.signOut();
  });

  it('shows viewers the status only', async () => {
    const { calls, client } = renderApp('/cameras', signedIn('Viewer'));

    expect(await screen.findByText('Recording')).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Add camera' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Stop' })).not.toBeInTheDocument();
    
    expect(calls.some((c) => c.path === '/api/config/cameras')).toBe(false);
    client.signOut();
  });

  it('lets operators stop recording', async () => {
    const { user, calls, client } = renderApp('/cameras', { ...signedIn('Operator'), 'POST /api/cameras/front/stop': () => ({ status: 204 }) });

    await user.click(await screen.findByRole('button', { name: 'Stop' }));
    expect(calls.some((c) => c.method === 'POST' && c.path === '/api/cameras/front/stop')).toBe(true);
    client.signOut();
  });

  it('shows why a camera cannot be deleted', async () => {
    const { user, client } = renderApp('/cameras', {
      ...signedIn('Admin'),
      'GET /api/config/cameras': () => ({ body: [config] }),
      'DELETE /api/config/cameras/front': () => ({ status: 409, body: { errors: ["Camera 'front' is assigned to plugin(s) motion: remove it from them first."] } }),
    });

    await user.click(await screen.findByRole('button', { name: 'Delete' }));
    await user.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Delete' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('assigned to plugin(s) motion');
    client.signOut();
  });
});
