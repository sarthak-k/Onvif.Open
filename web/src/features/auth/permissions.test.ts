import type { Role } from '../../api/types';
import { assignableRoles, can, type Permission } from './permissions';

const user = (role: Role, sites: string[] = []) => ({ id: '1', userName: 'u', role, sites, enabled: true, mustChangePassword: false, createdAt: '' });

describe('permissions', () => {
  it.each<[Permission, boolean, boolean, boolean]>([
    ['cameras.view', true, true, true],
    ['cameras.control', false, true, true],
    ['cameras.manage', false, false, true],
    ['users.manage', false, false, true],
  ])('%s: viewer %s, operator %s, admin %s', (permission, viewer, operator, admin) => {
    expect([can(user('Viewer'), permission), can(user('Operator'), permission), can(user('Admin'), permission)]).toEqual([viewer, operator, admin]);
  });

  it('keeps organization-wide settings for users who see every site', () => {
    expect(can(user('Admin', ['s1']), 'cameras.manage')).toBe(true);
    expect(can(user('Admin', ['s1']), 'users.manage')).toBe(false);
    expect(can(user('Admin', ['s1']), 'settings.manage')).toBe(false);
    expect(can(user('Admin'), 'organization.manage')).toBe(false);
    expect(can(user('Owner'), 'organization.manage')).toBe(true);
    expect(assignableRoles(user('Admin'))).not.toContain('Owner');
    expect(assignableRoles(user('Owner'))).toContain('Owner');
  });

  it('allows nothing when signed out', () => {
    expect(can(null, 'cameras.view')).toBe(false);
  });
});
