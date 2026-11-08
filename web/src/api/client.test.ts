import { ApiClient, ApiError } from './client';
import { fakeFetch, memoryStorage, tokens } from '../test/fakeRecorder';

describe('ApiClient', () => {
  it('signs in and sends the access token', async () => {
    const storage = memoryStorage();
    const { fetcher, calls } = fakeFetch({
      'POST /api/auth/login': () => ({ body: tokens(1) }),
      'GET /api/auth/me': ({ token }) => (token === 'access-1' ? { body: { userName: 'admin' } } : { status: 401 }),
    });
    const client = new ApiClient('', storage, fetcher);

    await client.signIn('admin', 'secret', true);

    expect(await client.get('/api/auth/me')).toEqual({ userName: 'admin' });
    expect(calls[0].body).toEqual({ userName: 'admin', password: 'secret' });
    expect(storage.value).toBe('refresh-1');
    expect(storage.persistent).toBe(true);
    client.signOut();
  });

  it('resumes a stored session with the refresh token', async () => {
    const { fetcher, calls } = fakeFetch({
      'POST /api/auth/refresh': ({ body }) => ((body as { refreshToken: string }).refreshToken === 'refresh-0' ? { body: tokens(1) } : { status: 401 }),
      'GET /api/cameras': ({ token }) => (token === 'access-1' ? { body: [] } : { status: 401 }),
    });
    const client = new ApiClient('', memoryStorage('refresh-0'), fetcher);

    expect(await client.get('/api/cameras')).toEqual([]);
    expect(calls.map((c) => c.path)).toEqual(['/api/auth/refresh', '/api/cameras']);
    client.signOut();
  });

  it('refreshes once when a call is rejected, and shares one refresh between calls', async () => {
    let issued = 1;
    const { fetcher, calls } = fakeFetch({
      'POST /api/auth/login': () => ({ body: tokens(1) }),
      'POST /api/auth/refresh': () => ({ body: tokens(++issued) }),
      'GET /api/users': ({ token }) => (token === 'access-1' ? { status: 401 } : { body: [] }),
    });
    const client = new ApiClient('', memoryStorage(), fetcher);
    await client.signIn('admin', 'secret', false);

    await Promise.all([client.get('/api/users'), client.get('/api/users')]);

    expect(calls.filter((c) => c.path === '/api/auth/refresh')).toHaveLength(1);
    client.signOut();
  });

  it('signs out when the refresh token is rejected', async () => {
    const storage = memoryStorage('stale');
    const { fetcher } = fakeFetch({
      'POST /api/auth/refresh': () => ({ status: 401, body: { title: 'Unauthorized', detail: 'Sign in again.' } }),
      'GET /api/cameras': () => ({ status: 401 }),
    });
    const client = new ApiClient('', storage, fetcher);
    const signedOut = vi.fn();
    client.onSignedOut(signedOut);

    await expect(client.get('/api/cameras')).rejects.toMatchObject({ status: 401 });
    expect(signedOut).toHaveBeenCalledOnce();
    expect(storage.value).toBeNull();
    expect(client.hasSession).toBe(false);
  });

  it('reads the recorder error formats', async () => {
    const { fetcher } = fakeFetch({
      'POST /api/a': () => ({ status: 400, body: { errors: ['One', 'Two'] } }),
      'POST /api/b': () => ({ status: 409, body: { error: 'Busy.' } }),
      'POST /api/c': () => ({ status: 400, body: { errors: { Id: ['Id is required.'] } } }),
      'POST /api/d': () => ({ status: 403 }),
    });
    const client = new ApiClient('', memoryStorage(), fetcher);

    const errors = async (path: string) => {
      const error = await client.post(path).catch((e: unknown) => e);
      expect(error).toBeInstanceOf(ApiError);
      return (error as ApiError).errors;
    };

    expect(await errors('/api/a')).toEqual(['One', 'Two']);
    expect(await errors('/api/b')).toEqual(['Busy.']);
    expect(await errors('/api/c')).toEqual(['Id is required.']);
    expect(await errors('/api/d')).toEqual(['You are not allowed to do this.']);
  });

  it('reports an unreachable recorder', async () => {
    const client = new ApiClient('http://nowhere', memoryStorage(), (() => Promise.reject(new TypeError('Failed to fetch'))) as typeof fetch);
    await expect(client.post('/api/auth/login')).rejects.toMatchObject({ status: 0, errors: ['The recorder cannot be reached.'] });
  });
});
