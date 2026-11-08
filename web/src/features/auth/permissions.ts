import type { Role, User } from '../../api/types';

/** What the app lets a user do. The recorder enforces the same; this only hides what would be refused. */
export type Permission = 'cameras.view' | 'cameras.control' | 'cameras.manage' | 'users.manage';

const rank: Record<Role, number> = { Viewer: 0, Operator: 1, Admin: 2 };

/** The lowest role with each permission (Viewer < Operator < Admin, as the recorder's policies). */
const requiredRole: Record<Permission, Role> = {
  'cameras.view': 'Viewer',
  'cameras.control': 'Operator',
  'cameras.manage': 'Admin',
  'users.manage': 'Admin',
};

export function can(user: User | null | undefined, permission: Permission): boolean {
  return !!user && rank[user.role] >= rank[requiredRole[permission]];
}

export function roleFor(permission: Permission): Role {
  return requiredRole[permission];
}
