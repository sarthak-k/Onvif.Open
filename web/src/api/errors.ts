/** An API call that failed: the HTTP status and the recorder's messages ({ errors: [...] }, problem details or text). */
export class ApiError extends Error {
  readonly status: number;
  readonly errors: string[];

  constructor(status: number, errors: string[]) {
    super(errors.join(' ') || `Request failed (${status}).`);
    this.name = 'ApiError';
    this.status = status;
    this.errors = errors;
  }

  /** 4xx: asking again does not help. */
  get isClientError(): boolean {
    return this.status >= 400 && this.status < 500;
  }
}

export const isUnauthorized = (error: unknown): boolean => error instanceof ApiError && error.status === 401;

/** Reads every error shape the recorder answers with into plain messages. */
export async function readErrors(response: Response): Promise<string[]> {
  const fallback = response.status === 403 ? ['You are not allowed to do this.'] : [`Request failed (${response.status}).`];
  const text = await response.text().catch(() => '');
  if (!text) return fallback;

  let body: { errors?: unknown; error?: unknown; detail?: unknown; title?: unknown };
  try {
    body = JSON.parse(text);
  } catch {
    return [text];
  }

  if (Array.isArray(body.errors)) return body.errors.map(String);
  // ASP.NET validation problem details: { errors: { field: [messages] } }
  if (body.errors && typeof body.errors === 'object') return Object.values(body.errors as Record<string, unknown>).flat().map(String);
  for (const candidate of [body.error, body.detail, body.title]) {
    if (typeof candidate === 'string') return [candidate];
  }
  return fallback;
}
