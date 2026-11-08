import { useState } from 'react';
import { CheckboxField, TextField } from '../../shared/form/fields';
import { ConfirmDialog } from '../../shared/ui/ConfirmDialog';
import { ErrorMessage } from '../../shared/ui/ErrorMessage';

interface Props {
  initialName: string;
  canShare: boolean;
  busy: boolean;
  error: unknown;
  onSave(name: string, shared: boolean): void;
  onCancel(): void;
}

export function SaveLayoutDialog({ initialName, canShare, busy, error, onSave, onCancel }: Props) {
  const [name, setName] = useState(initialName);
  const [shared, setShared] = useState(false);

  return (
    <ConfirmDialog title="Save layout as" confirmLabel="Save" busy={busy || !name.trim()} onCancel={onCancel} onConfirm={() => onSave(name.trim(), shared)}>
      <div className="stack">
        <TextField label="Name" value={name} maxLength={64} onChange={setName} />
        {canShare && <CheckboxField label="Share with all users" checked={shared} onChange={setShared} />}
        <ErrorMessage error={error} />
      </div>
    </ConfirmDialog>
  );
}
