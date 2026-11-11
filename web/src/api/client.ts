import { isUnauthorized } from './errors';
import { HttpTransport, type HttpMethod } from './http';
import { Session } from './session';
import { browserTokenStorage, type TokenStorage } from './tokenStorage';
import type { SetupRequest, SignInStep, SignupRequest, Tokens } from './types';

export { ApiError } from './errors';
export type { TokenStorage } from './tokenStorage';

/**
 * The recorder as one object (facade over the transport and the session): authenticated calls that renew the
 * session and retry once when the recorder answers 401.
 */
export class ApiClient {
  private readonly http: HttpTransport;
  private readonly session: Session;

  constructor(baseUrl: string, storage: TokenStorage = browserTokenStorage, fetcher?: typeof fetch) {
    this.http = new HttpTransport(baseUrl, fetcher);
    this.session = new Session(storage, (refreshToken) => this.http.send<Tokens>({ method: 'POST', path: '/api/auth/refresh', body: { refreshToken } }));
  }

  get baseUrl(): string {
    return this.http.baseUrl;
  }

  /** True when a session can be resumed or is active. */
  get hasSession(): boolean {
    return this.session.active;
  }

  /** The current access token, for media URLs (?access_token=). */
  get token(): string | null {
    return this.session.token;
  }

  /** Called when the session ends: signed out, or the refresh token was rejected. */
  onSignedOut(listener: () => void): () => void {
    return this.session.onEnded(listener);
  }

  /** Signed in, or (two-factor sign-in on) a challenge for {@link completeTwoFactor}. */
  async signIn(userName: string, password: string, persistent: boolean): Promise<SignInStep | null> {
    const answer = await this.http.send<Tokens | SignInStep>({ method: 'POST', path: '/api/auth/login', body: { userName, password } });
    if ('twoFactorRequired' in answer) return answer;
    this.session.start(answer, persistent);
    return null;
  }

  /** The second step: the app's code (or a recovery code) for the challenge the password got. */
  async completeTwoFactor(challenge: string, code: string, persistent: boolean): Promise<void> {
    this.session.start(await this.http.send<Tokens>({ method: 'POST', path: '/api/auth/login/two-factor', body: { challenge, code } }), persistent);
  }

  /** First-run setup: creates the organization and its owner, and signs the owner in. */
  async completeSetup(request: SetupRequest): Promise<void> {
    this.session.start(await this.http.send<Tokens>({ method: 'POST', path: '/api/setup', body: request }), false);
  }

  /** Cloud sign-up: emails a link; nothing exists until it is opened. */
  async startSignup(request: SignupRequest): Promise<void> {
    await this.http.send({ method: 'POST', path: '/api/signup', body: request });
  }

  /** Opens the emailed link: creates the organization and its owner, and signs the owner in. */
  async completeSignup(token: string): Promise<void> {
    this.session.start(await this.http.send<Tokens>({ method: 'POST', path: '/api/signup/verify', body: { token } }), false);
  }

  /** A call that needs no session (setup status). */
  anonymous<T>(method: HttpMethod, path: string, body?: unknown): Promise<T> {
    return this.http.send<T>({ method, path, body });
  }

  /** The password change ends every session; the recorder answers with new tokens for this one. */
  async changePassword(currentPassword: string, newPassword: string): Promise<void> {
    this.session.start(await this.request<Tokens>('POST', '/api/auth/password', { currentPassword, newPassword }));
  }

  signOut(): void {
    this.session.end();
  }

  refresh(): Promise<boolean> {
    return this.session.refresh();
  }

  get<T>(path: string): Promise<T> {
    return this.request<T>('GET', path);
  }

  post<T>(path: string, body?: unknown): Promise<T> {
    return this.request<T>('POST', path, body);
  }

  put<T>(path: string, body?: unknown): Promise<T> {
    return this.request<T>('PUT', path, body);
  }

  delete(path: string): Promise<void> {
    return this.request<void>('DELETE', path);
  }

  async request<T>(method: HttpMethod, path: string, body?: unknown): Promise<T> {
    const token = await this.session.ensureToken();
    try {
      return await this.http.send<T>({ method, path, body, token });
    } catch (error) {
      if (isUnauthorized(error) && (await this.session.refresh())) {
        return this.http.send<T>({ method, path, body, token: this.session.token });
      }
      throw error;
    }
  }
}
