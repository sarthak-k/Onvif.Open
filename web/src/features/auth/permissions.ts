import type { Me, Role, User } from '../../api/types';

/** What the app lets a user do. The recorder enforces the same; this only hides what would be refused. */
export type Permission =
  | 'cameras.view'
  | 'cameras.control'
  | 'cameras.manage'
  | 'users.manage'
  | 'settings.manage'
  | 'audit.view'
  | 'organization.manage'
  | 'plans.manage';

const rank: Record<Role, number> = { Viewer: 0, Operator: 1, Admin: 2, Owner: 3 };

interface Rule {
  role: Role;
  /** Organization-wide settings need a user who sees every site. */
  allSites?: boolean;
  /** Only in the organization that runs the service (its plans). */
  operator?: boolean;
}

/** The lowest role with each permission (Viewer < Operator < Admin < Owner, as the recorder's policies). */
const rules: Record<Permission, Rule> = {
  'cameras.view': { role: 'Viewer' },
  'cameras.control': { role: 'Operator' },
  'cameras.manage': { role: 'Admin' },
  'users.manage': { role: 'Admin', allSites: true },
  'settings.manage': { role: 'Admin', allSites: true },
  'audit.view': { role: 'Admin', allSites: true },
  'organization.manage': { role: 'Owner' },
  'plans.manage': { role: 'Owner', operator: true },
};

export function can(user: User | null | undefined, permission: Permission): boolean {
  const rule = rules[permission];
  return (
    !!user &&
    rank[user.role] >= rank[rule.role] &&
    (!rule.allSites || user.sites.length === 0) &&
    (!rule.operator || ('organization' in user && (user as Me).organization?.billingOperator === true))
  );
}

/** Why a page is closed, in words: "This page needs the Admin role." */
export function requirementOf(permission: Permission): string {
  const rule = rules[permission];
  if (rule.operator) return "This page is for the owners of the organization that runs this service.";
  return rule.allSites ? `This page is for ${rule.role === 'Admin' ? 'administrators' : 'owners'} who see every site.` : `This page needs the ${rule.role} role.`;
}

/** The roles a user may give others: only owners make owners. */
export function assignableRoles(user: User | null | undefined): Role[] {
  return (['Viewer', 'Operator', 'Admin', 'Owner'] as Role[]).filter((role) => role !== 'Owner' || user?.role === 'Owner');
}
