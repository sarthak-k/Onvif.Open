import type { ReactNode } from 'react';
import { Navigate, useLocation } from 'react-router-dom';
import { useAuth } from './AuthContext';
import { can, requirementOf, type Permission } from './permissions';
import { sessionOf } from './session';

/** Signed-in users only; sends a user who must change the password, or set up two-factor sign-in, there first. */
export function RequireAuth({ children }: { children: ReactNode }) {
  const { user } = useAuth();
  const location = useLocation();

  switch (sessionOf(user)) {
    case 'resuming':
      return <p className="loading">Loading…</p>;
    case 'signedOut':
      return <Navigate to="/login" replace state={{ from: location.pathname + location.search }} />;
    case 'mustChangePassword':
      return location.pathname === '/change-password' ? <>{children}</> : <Navigate to="/change-password" replace />;
    case 'mustSetUpTwoFactor':
      return location.pathname === '/two-factor' ? <>{children}</> : <Navigate to="/two-factor" replace />;
    case 'signedIn':
      return <>{children}</>;
  }
}

/** Hides a page from users without the permission (the recorder refuses their calls anyway). */
export function RequirePermission({ permission, children }: { permission: Permission; children: ReactNode }) {
  const { user } = useAuth();
  return can(user, permission) ? <>{children}</> : <p role="alert">{requirementOf(permission)}</p>;
}
