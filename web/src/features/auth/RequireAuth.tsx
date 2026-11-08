import type { ReactNode } from 'react';
import { Navigate, useLocation } from 'react-router-dom';
import { useAuth } from './AuthContext';
import { can, roleFor, type Permission } from './permissions';

/** Signed-in users only; sends a user who must change the password to that page first. */
export function RequireAuth({ children }: { children: ReactNode }) {
  const { user } = useAuth();
  const location = useLocation();

  if (user === undefined) return <p className="loading">Loading…</p>;
  if (user === null) return <Navigate to="/login" replace state={{ from: location.pathname + location.search }} />;
  if (user.mustChangePassword && location.pathname !== '/change-password') return <Navigate to="/change-password" replace />;
  return <>{children}</>;
}

/** Hides a page from users without the permission (the recorder refuses their calls anyway). */
export function RequirePermission({ permission, children }: { permission: Permission; children: ReactNode }) {
  const { user } = useAuth();
  return can(user, permission) ? <>{children}</> : <p role="alert">This page needs the {roleFor(permission)} role.</p>;
}
