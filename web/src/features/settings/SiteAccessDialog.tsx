import { useState } from 'react';
import type { Site, User } from '../../api/types';
import { useUpdateUser } from '../../data/users';
import { ConfirmDialog } from '../../shared/ui/ConfirmDialog';
import { ErrorMessage } from '../../shared/ui/ErrorMessage';
import { SiteAccessField } from './SiteAccessField';

export function SiteAccessDialog({ user, sites, onClose }: { user: User; sites: readonly Site[]; onClose(): void }) {
  const update = useUpdateUser();
  const [value, setValue] = useState<string[]>(user.sites);

  return (
    <ConfirmDialog
      title={`Sites ${user.userName} may see`}
      confirmLabel="Save"
      busy={update.isPending}
      onCancel={onClose}
      onConfirm={() => update.mutate({ id: user.id, user: { sites: value } }, { onSuccess: onClose })}
    >
      <div className="stack">
        <SiteAccessField sites={sites} value={value} onChange={setValue} />
        <p className="muted small">The user is signed out and sees the change at the next sign-in.</p>
        <ErrorMessage error={update.error} />
      </div>
    </ConfirmDialog>
  );
}
