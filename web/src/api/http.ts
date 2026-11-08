import { ApiError, readErrors } from './errors';

export type HttpMethod = 'GET' | 'POST' | 'PUT' | 'PATCH' | 'DELETE';

export interface HttpRequest {
  method: HttpMethod;
  path: string;
  body?: unknown;
  /** Bearer token, when the call is authenticated. */
  token?: string | null;
}

/** JSON over fetch: one place for headers, serialization and turning failures into ApiError. */
export class HttpTransport {
  constructor(
    readonly baseUrl: string,
    private readonly fetcher: typeof fetch = (...args) => fetch(...args),
  ) {}

  async send<T>({ method, path, body, token }: HttpRequest): Promise<T> {
    const headers: Record<string, string> = { Accept: 'application/json' };
    if (body !== undefined) headers['Content-Type'] = 'application/json';
    if (token) headers.Authorization = `Bearer ${token}`;

    let response: Response;
    try {
      response = await this.fetcher(this.baseUrl + path, { method, headers, body: body === undefined ? undefined : JSON.stringify(body) });
    } catch {
      throw new ApiError(0, ['The recorder cannot be reached.']);
    }

    if (!response.ok) throw new ApiError(response.status, await readErrors(response));
    if (response.status === 204) return undefined as T;

    const text = await response.text();
    return (text ? JSON.parse(text) : undefined) as T;
  }
}
