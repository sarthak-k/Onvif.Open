import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import type { Me, SetupRequest, SignupRequest } from '../../api/types';
import { useApi, useApiClient } from '../../data/ApiContext';
import { can, type Permission } from './permissions';

interface AuthState {
  /** null: signed out. undefined: still resuming the stored session. */
  user: Me | null | undefined;
  /** First-run setup: creates the organization and owner, and signs the owner in. */
  completeSetup(request: SetupRequest): Promise<void>;
  /** Cloud sign-up, step 1: emails a link to the address. */
  startSignup(request: SignupRequest): Promise<void>;
  /** Cloud sign-up, step 2: the emailed link creates the organization and signs its owner in. */
  completeSignup(token: string): Promise<void>;
  signIn(userName: string, password: string, keepSignedIn: boolean): Promise<void>;
  signOut(): void;
  changePassword(currentPassword: string, newPassword: string): Promise<void>;
}

const AuthContext = createContext<AuthState | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const api = useApi();
  const client = useApiClient();
  const queries = useQueryClient();
  const [user, setUser] = useState<Me | null | undefined>(client.hasSession ? undefined : null);

  // Signed out (here, or because the refresh token was rejected): forget the user and every cached answer.
  useEffect(
    () =>
      client.onSignedOut(() => {
        setUser(null);
        queries.clear();
      }),
    [client, queries],
  );

  // Resume the session from the stored refresh token.
  useEffect(() => {
    if (!client.hasSession) return;
    let active = true;
    api.auth.me().then(
      (me) => active && setUser(me),
      () => active && setUser(null),
    );
    return () => {
      active = false;
    };
  }, [api, client]);

  const signIn = useCallback(
    async (userName: string, password: string, keepSignedIn: boolean) => {
      await client.signIn(userName, password, keepSignedIn);
      setUser(await api.auth.me());
    },
    [api, client],
  );

  const changePassword = useCallback(
    async (currentPassword: string, newPassword: string) => {
      await client.changePassword(currentPassword, newPassword);
      setUser(await api.auth.me());
    },
    [api, client],
  );

  const completeSetup = useCallback(
    async (request: SetupRequest) => {
      await client.completeSetup(request);
      setUser(await api.auth.me());
    },
    [api, client],
  );

  const startSignup = useCallback((request: SignupRequest) => client.startSignup(request), [client]);

  const completeSignup = useCallback(
    async (token: string) => {
      await client.completeSignup(token);
      setUser(await api.auth.me());
    },
    [api, client],
  );

  const signOut = useCallback(() => client.signOut(), [client]);

  const value = useMemo(
    () => ({ user, signIn, signOut, changePassword, completeSetup, startSignup, completeSignup }),
    [user, signIn, signOut, changePassword, completeSetup, startSignup, completeSignup],
  );
  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthState {
  const auth = useContext(AuthContext);
  if (!auth) throw new Error('useAuth must be used inside AuthProvider.');
  return auth;
}

/** The signed-in user; only for pages behind RequireAuth. */
export function useUser(): Me {
  const { user } = useAuth();
  if (!user) throw new Error('No user is signed in.');
  return user;
}

/** Whether the signed-in user may do something. */
export function useCan(permission: Permission): boolean {
  return can(useAuth().user, permission);
}
