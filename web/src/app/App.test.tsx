import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { ApiClient } from '../api/client';
import type { RecorderApi } from '../api/recorder';
import { admin, memoryStorage, tokens } from '../test/fakeRecorder';
import { App, createQueryClient } from './App';

describe('App', () => {
  it('runs on any RecorderApi implementation', async () => {
    const fail = () => Promise.reject(new Error('not used'));
    const api: RecorderApi = {
      auth: { me: async () => admin, twoFactor: { setup: fail, enable: fail, disable: fail, newRecoveryCodes: fail }, singleSignOn: fail },
      setup: { status: fail, cloud: fail },
      organization: { get: fail, rename: fail, requireTwoFactor: fail, singleSignOn: fail, saveSingleSignOn: fail },
      sites: { list: async () => [], create: fail, update: fail, remove: fail },
      policies: { list: fail, create: fail, update: fail, remove: fail },
      cameras: { list: async () => [], get: fail, create: fail, update: fail, remove: fail, test: fail, discover: fail },
      recording: { status: async () => [{ cameraId: 'in-memory', name: 'In memory', state: 'Recording', mode: 'Continuous', isTriggered: false, codec: 'H265', recordsAudio: false, framesRecorded: 0, bytesRecorded: 0, segmentsCompleted: 0 }], segments: fail, start: fail, stop: fail },
      users: { list: fail, create: fail, update: fail, remove: fail, resetTwoFactor: fail },
      layouts: { list: async () => [], create: fail, update: fail, remove: fail },
      media: { liveUrl: (id) => `memory://${id}`, liveMp4Url: (id) => `memory://${id}.mp4`, recordingsUrl: (id) => `memory://${id}/recordings`, token: () => null },
      audit: { list: fail },
      events: { list: fail },
      exports: { list: async () => [], create: fail, links: fail, share: fail, revoke: fail, downloadUrl: (id) => id, sharedUrl: (p) => p },
      cases: { list: async () => [], get: fail, create: fail, update: fail, remove: fail, addClip: fail, updateClip: fail, removeClip: fail },
      alertRules: { list: async () => [], create: fail, update: fail, remove: fail, test: fail },
      alerts: { list: async () => [], summary: async () => ({ open: 0, latest: null }), acknowledge: fail },
      billing: { get: fail, checkout: fail, portal: fail, plans: fail, createPlan: fail, updatePlan: fail, retirePlan: fail },
      ptz: { status: fail, move: fail, stop: fail, presets: async () => [], goToPreset: fail, savePreset: fail, removePreset: fail, home: fail, setHome: fail },
      gateways: { list: async () => [], update: fail, pair: fail, remove: fail, createCloud: fail, start: fail, controlPlaneUrl: () => 'http://recorder' },
    };
    const client = new ApiClient('', memoryStorage('refresh-0'), (async () => new Response(JSON.stringify(tokens(1)))) as typeof fetch);

    render(
      <MemoryRouter initialEntries={['/cameras']}>
        <App client={client} queryClient={createQueryClient()} api={api} />
      </MemoryRouter>,
    );

    expect(await screen.findByRole('link', { name: 'In memory' })).toBeInTheDocument();
    expect(screen.getByText('H265')).toBeInTheDocument();
    client.signOut();
  });
});
