import { lazy, type ComponentType, type LazyExoticComponent } from 'react';
import type { Permission } from '../features/auth/permissions';

// Pages load on first use (code splitting): signing in does not download the camera editor.
const CamerasPage = lazy(() => import('../features/cameras/CamerasPage'));
const CameraEditorPage = lazy(() => import('../features/cameras/editor/CameraEditorPage'));
const UsersPage = lazy(() => import('../features/users/UsersPage'));
const AccountPage = lazy(() => import('../features/auth/AccountPage'));

export interface AppRoute {
  path: string;
  page: LazyExoticComponent<ComponentType>;
  permission: Permission;
  /** Shown in the main navigation under this label. */
  nav?: string;
}

/** The signed-in pages; a new page is one entry here (navigation and access follow from it). */
export const appRoutes: readonly AppRoute[] = [
  { path: '/cameras', page: CamerasPage, permission: 'cameras.view', nav: 'Cameras' },
  { path: '/cameras/new', page: CameraEditorPage, permission: 'cameras.manage' },
  { path: '/cameras/:id', page: CameraEditorPage, permission: 'cameras.manage' },
  { path: '/users', page: UsersPage, permission: 'users.manage', nav: 'Users' },
  { path: '/account', page: AccountPage, permission: 'cameras.view', nav: 'Account' },
];
