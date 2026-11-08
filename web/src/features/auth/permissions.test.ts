import type { Role } from '../../api/types';
import { can, type Permission } from './permissions';

const user = (role: Role) => ({ id: '1', userName: 'u', role, enabled: true, mustChangePassword: false, createdAt: '' });

describe('permissions', () => {
  it.each<[Permission, boolean, boolean, boolean]>([
    ['cameras.view', true, true, true],
    ['cameras.control', false, true, true],
    ['cameras.manage', false, false, true],
    ['users.manage', false, false, true],
  ])('%s: viewer %s, operator %s, admin %s', (permission, viewer, operator, admin) => {
    expect([can(user('Viewer'), permission), can(user('Operator'), permission), can(user('Admin'), permission)]).toEqual([viewer, operator, admin]);
  });

  it('allows nothing when signed out', () => {
    expect(can(null, 'cameras.view')).toBe(false);
  });
});
