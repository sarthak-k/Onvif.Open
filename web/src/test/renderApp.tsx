import { render } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { ApiClient } from '../api/client';
import { App, createQueryClient } from '../app/App';
import { fakeFetch, memoryStorage } from './fakeRecorder';

/** The whole app at a URL, against a fake recorder; a stored refresh token signs in. */
export function renderApp(path: string, routes: Parameters<typeof fakeFetch>[0], refreshToken: string | null = 'refresh-0') {
  const { fetcher, calls } = fakeFetch(routes);
  const client = new ApiClient('', memoryStorage(refreshToken), fetcher);
  const queryClient = createQueryClient();
  queryClient.setDefaultOptions({ queries: { retry: false } });

  const user = userEvent.setup();
  const view = render(
    <MemoryRouter initialEntries={[path]}>
      <App client={client} queryClient={queryClient} />
    </MemoryRouter>,
  );
  return { ...view, user, calls, client };
}
