import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import type { ApiClient } from '../api/client';
import { createRecorderApi, type RecorderApi } from '../api/recorder';
import type { Role, User } from '../api/types';

const rank: Record<Role, number> = { Viewer: 0, Operator: 1, Admin: 2 };

/** Viewer < Operator < Admin, as the recorder's policies. */
export function hasRole(user: User | null, role: Role): boolean {
  return user !== null && rank[user.role] >= rank[role];
}

interface AuthState {
  api: RecorderApi;
  client: ApiClient;
  /** null: signed out. undefined: still resuming the stored session. */
  user: User | null | undefined;
  signIn(userName: string, password: string, keepSignedIn: boolean): Promise<void>;
  signOut(): void;
  changePassword(currentPassword: string, newPassword: string): Promise<void>;
}

const AuthContext = createContext<AuthState | null>(null);

export function AuthProvider({ client, children }: { client: ApiClient; children: ReactNode }) {
  const api = useMemo(() => createRecorderApi(client), [client]);
  const queries = useQueryClient();
  const [user, setUser] = useState<User | null | undefined>(client.hasSession ? undefined : null);

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
    api.me().then(
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
      setUser(await api.me());
    },
    [api, client],
  );

  const changePassword = useCallback(
    async (currentPassword: string, newPassword: string) => {
      await client.changePassword(currentPassword, newPassword);
      setUser(await api.me());
    },
    [api, client],
  );

  const signOut = useCallback(() => client.signOut(), [client]);

  const value = useMemo(() => ({ api, client, user, signIn, signOut, changePassword }), [api, client, user, signIn, signOut, changePassword]);
  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthState {
  const auth = useContext(AuthContext);
  if (!auth) throw new Error('useAuth must be used inside AuthProvider.');
  return auth;
}

/** The signed-in user; only for pages behind RequireAuth. */
export function useUser(): User {
  const { user } = useAuth();
  if (!user) throw new Error('No user is signed in.');
  return user;
}
