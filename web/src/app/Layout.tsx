import { Suspense } from 'react';
import { NavLink, Outlet, useLocation } from 'react-router-dom';
import { useAuth, useUser } from '../features/auth/AuthContext';
import { can } from '../features/auth/permissions';
import { ErrorBoundary } from '../shared/ui/ErrorBoundary';
import { appRoutes } from './routes';

export function Layout() {
  const user = useUser();
  const { signOut } = useAuth();
  const { pathname } = useLocation();
  const nav = appRoutes.filter((route) => route.nav && can(user, route.permission));

  return (
    <div className="shell">
      <header className="topbar">
        <span className="brand">Onvif.Open</span>
        <nav aria-label="Main">
          {nav.map((route) => (
            <NavLink key={route.path} to={route.path}>
              {route.nav}
            </NavLink>
          ))}
        </nav>
        <span className="who">
          {user.userName} <span className="badge">{user.role}</span>
        </span>
        <button type="button" className="link" onClick={signOut}>
          Sign out
        </button>
      </header>
      <main className="content">
        <ErrorBoundary resetKey={pathname}>
          <Suspense fallback={<p className="loading">Loading…</p>}>
            <Outlet />
          </Suspense>
        </ErrorBoundary>
      </main>
    </div>
  );
}
