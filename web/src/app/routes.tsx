import { lazy, type ComponentType, type LazyExoticComponent } from 'react';
import type { Permission } from '../features/auth/permissions';

// Pages load on first use (code splitting): signing in does not download the camera editor.
const LivePage = lazy(() => import('../features/live/LivePage'));
const CamerasPage = lazy(() => import('../features/cameras/CamerasPage'));
const CameraEditorPage = lazy(() => import('../features/cameras/editor/CameraEditorPage'));
const UsersPage = lazy(() => import('../features/settings/UsersPage'));
const SitesPage = lazy(() => import('../features/settings/SitesPage'));
const PoliciesPage = lazy(() => import('../features/settings/PoliciesPage'));
const OrganizationPage = lazy(() => import('../features/settings/OrganizationPage'));
const GatewaysPage = lazy(() => import('../features/settings/GatewaysPage'));
const AuditPage = lazy(() => import('../features/settings/AuditPage'));
const AccountPage = lazy(() => import('../features/auth/AccountPage'));

export interface AppRoute {
  path: string;
  page: LazyExoticComponent<ComponentType>;
  permission: Permission;
  /** Shown in the main navigation under this label. */
  nav?: string;
  /** Shown inside the settings area (with its sub-navigation). */
  settings?: boolean;
}

/** The signed-in pages; a new page is one entry here (navigation and access follow from it). */
export const appRoutes: readonly AppRoute[] = [
  { path: '/live', page: LivePage, permission: 'cameras.view', nav: 'Live' },
  { path: '/cameras', page: CamerasPage, permission: 'cameras.view', nav: 'Cameras' },
  { path: '/cameras/new', page: CameraEditorPage, permission: 'cameras.manage' },
  { path: '/cameras/:id', page: CameraEditorPage, permission: 'cameras.manage' },
  { path: '/settings/sites', page: SitesPage, permission: 'settings.manage', settings: true },
  { path: '/settings/gateways', page: GatewaysPage, permission: 'cameras.view', settings: true },
  { path: '/settings/policies', page: PoliciesPage, permission: 'settings.manage', settings: true },
  { path: '/settings/users', page: UsersPage, permission: 'users.manage', settings: true },
  { path: '/settings/audit', page: AuditPage, permission: 'audit.view', settings: true },
  { path: '/settings/organization', page: OrganizationPage, permission: 'cameras.view', settings: true },
  { path: '/account', page: AccountPage, permission: 'cameras.view', nav: 'Account' },
];

/** The main navigation in order: pages that have a nav label, and the settings area. */
export const navItems: readonly { path: string; label: string; permission: Permission }[] = [
  { path: '/live', label: 'Live', permission: 'cameras.view' },
  { path: '/cameras', label: 'Cameras', permission: 'cameras.view' },
  { path: '/settings', label: 'Settings', permission: 'cameras.view' },
  { path: '/account', label: 'Account', permission: 'cameras.view' },
];
