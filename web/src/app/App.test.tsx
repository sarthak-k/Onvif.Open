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
      auth: { me: async () => admin },
      setup: { status: fail },
      organization: { get: fail, rename: fail },
      sites: { list: async () => [], create: fail, update: fail, remove: fail },
      policies: { list: fail, create: fail, update: fail, remove: fail },
      cameras: { list: async () => [], get: fail, create: fail, update: fail, remove: fail, test: fail, discover: fail },
      recording: { status: async () => [{ cameraId: 'in-memory', name: 'In memory', state: 'Recording', mode: 'Continuous', isTriggered: false, codec: 'H265', recordsAudio: false, framesRecorded: 0, bytesRecorded: 0, segmentsCompleted: 0 }], segments: fail, start: fail, stop: fail },
      users: { list: fail, create: fail, update: fail, remove: fail },
      layouts: { list: async () => [], create: fail, update: fail, remove: fail },
      media: { liveUrl: (id) => `memory://${id}`, recordingsUrl: (id) => `memory://${id}/recordings`, token: () => null },
      audit: { list: fail },
      events: { list: fail },
      cases: { list: async () => [], get: fail, create: fail, update: fail, remove: fail, addClip: fail, updateClip: fail, removeClip: fail },
      gateways: { list: async () => [], update: fail, pair: fail, remove: fail, controlPlaneUrl: () => 'http://recorder' },
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
