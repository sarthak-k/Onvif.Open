import { screen, within } from '@testing-library/react';
import type { Gateway } from '../../api/types';
import { renderApp } from '../../test/renderApp';
import { admin, tenancyRoutes, tokens } from '../../test/fakeRecorder';
import { describeCameras, parseSettings, toFormValues, toRequest } from './gatewayForm';

const gateway: Gateway = {
  id: 'gw-1',
  name: 'Back office',
  location: 'Local',
  embedded: true,
  status: 'Online',
  version: '1.4.0',
  startedAt: '2026-11-09T08:00:00Z',
  lastSeenAt: '2026-11-09T12:00:00Z',
  health: { cameras: 4, recording: 3, failing: 1, freeBytes: 500 * 1024 ** 3, recordedBytes: 120 * 1024 ** 3, backupProvider: 'FileSystem', pendingUploads: 2, backupError: null },
  restartRequired: false,
  storage: { minFreeSpaceGB: 1, maxSizePerCameraGB: null, maxTotalSizeGB: null },
  backup: { provider: 'FileSystem', prefix: null, keepLocalCopyHours: 0, settings: ['Path'] },
  discovery: { multicast: true, addresses: [] },
};

const signedIn = (role = 'Admin') => ({
  ...tenancyRoutes,
  'POST /api/auth/refresh': () => ({ body: tokens(1) }),
  'GET /api/auth/me': () => ({ body: { ...admin, role } }),
});

describe('gateways', () => {
  it('shows how the gateway is doing and lets administrators change its settings', async () => {
    let saved: unknown;
    let current = gateway;
    const { user, client } = renderApp('/settings/gateways', {
      ...signedIn(),
      'GET /api/gateways': () => ({ body: [current] }),
      'PUT /api/gateways/gw-1': ({ body }) => {
        saved = body;
        current = { ...gateway, restartRequired: true };
        return { body: current };
      },
    });

    const card = await screen.findByRole('article', { name: 'Back office' });
    expect(within(card).getByText('Online')).toBeInTheDocument();
    expect(within(card).getByText('3 of 4 cameras recording, 1 reconnecting')).toBeInTheDocument();
    expect(within(card).getByText('120.0 GB recorded, 500.0 GB free')).toBeInTheDocument();
    expect(within(card).getByText('A folder or NAS share, 2 files waiting')).toBeInTheDocument();

    await user.click(within(card).getByRole('button', { name: 'Edit' }));
    const dialog = screen.getByRole('dialog');
    await user.type(within(dialog).getByLabelText('Per camera (GB)'), '50');
    expect(within(dialog).getByLabelText('Settings (one Name=value per line)')).toHaveAttribute('placeholder', 'Stored: Path. Leave empty to keep them.');
    await user.type(within(dialog).getByLabelText('Keep the gateway\'s copy (hours)'), '{backspace}24');
    await user.click(within(dialog).getByLabelText('Search the local network (multicast)'));
    await user.type(within(dialog).getByLabelText('Also ask these addresses (one per line)'), '10.0.5.20{enter}10.0.6.0:3702');
    await user.click(within(dialog).getByRole('button', { name: 'Save' }));

    expect(await screen.findByText('Restart the recorder to apply the new backup settings.')).toBeInTheDocument();
    expect(saved).toEqual({
      name: 'Back office',
      storage: { minFreeSpaceGB: 1, maxSizePerCameraGB: 50, maxTotalSizeGB: null },
      backup: { provider: 'FileSystem', prefix: null, keepLocalCopyHours: 24, settings: null },
      discovery: { multicast: false, addresses: ['10.0.5.20', '10.0.6.0:3702'] },
    });
    client.signOut();
  });

  it('shows viewers the gateway without its settings', async () => {
    const { client } = renderApp('/settings/gateways', {
      ...signedIn('Viewer'),
      'GET /api/gateways': () => ({ body: [{ ...gateway, status: 'Offline', storage: null, backup: null, discovery: null }] }),
    });

    const card = await screen.findByRole('article', { name: 'Back office' });
    expect(within(card).getByText('Offline')).toBeInTheDocument();
    expect(within(card).queryByRole('button', { name: 'Edit' })).not.toBeInTheDocument();
    client.signOut();
  });

  it('sends new backup settings when the destination changes', () => {
    const values = { ...toFormValues(gateway), destination: 'Onvif.Open.Storage.S3', backupSettings: 'Bucket=cams\nSecretKey=a=b\n\n' };
    expect(toRequest(values, gateway).backup).toEqual({ provider: 'Onvif.Open.Storage.S3', prefix: null, keepLocalCopyHours: 0, settings: { Bucket: 'cams', SecretKey: 'a=b' } });
    expect(toRequest({ ...values, destination: '', backupSettings: '' }, gateway).backup?.settings).toEqual({});
    expect(parseSettings('nonsense\n=x')).toEqual({});
    expect(describeCameras({ ...gateway, health: null })).toBe('No report yet');
  });
});
