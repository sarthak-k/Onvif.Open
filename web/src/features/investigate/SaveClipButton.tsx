import { useState, type FormEvent } from 'react';
import { Link } from 'react-router-dom';
import type { CaseClip } from '../../api/types';
import { useAddClip, useCases, useCreateCase } from '../../data/cases';
import { SelectField, TextAreaField, TextField } from '../../shared/form/fields';
import { useForm } from '../../shared/form/useForm';
import { ConfirmDialog } from '../../shared/ui/ConfirmDialog';
import { ErrorMessage } from '../../shared/ui/ErrorMessage';

interface Props {
  cameraIds: string[];
  marks: { from: number; to: number };
  eventId?: string | null;
}

const newCase = 'new';

/** Saves the marked clip, of the cameras on screen, with a note to an open case or a new one. */
export function SaveClipButton({ cameraIds, marks, eventId = null }: Props) {
  const [open, setOpen] = useState(false);
  const [saved, setSaved] = useState<{ caseId: string; caseName: string; clip: CaseClip } | null>(null);

  return (
    <>
      <button type="button" className="primary" onClick={() => setOpen(true)}>
        Save to case
      </button>
      {saved && (
        <span className="small" role="status">
          Saved to <Link to={`/cases/${saved.caseId}`}>{saved.caseName}</Link>.
        </span>
      )}
      {open && (
        <SaveClipDialog
          cameraIds={cameraIds}
          marks={marks}
          eventId={eventId}
          onClose={() => setOpen(false)}
          onSaved={(result) => {
            setSaved(result);
            setOpen(false);
          }}
        />
      )}
    </>
  );
}

function SaveClipDialog({
  cameraIds,
  marks,
  eventId,
  onClose,
  onSaved,
}: Props & { onClose(): void; onSaved(result: { caseId: string; caseName: string; clip: CaseClip }): void }) {
  const cases = useCases('Open');
  const create = useCreateCase();
  const add = useAddClip();
  const firstCase = cases.data?.[0]?.id ?? newCase;
  const { values, set } = useForm({ caseId: '', name: '', note: '' });
  const caseId = values.caseId || firstCase;
  const creating = caseId === newCase;
  const options = [...(cases.data ?? []).map((c) => ({ value: c.id, label: c.name })), { value: newCase, label: 'A new case…' }];

  async function submit(event?: FormEvent) {
    event?.preventDefault();
    const target = creating ? await create.mutateAsync({ name: values.name.trim() }) : cases.data!.find((c) => c.id === caseId)!;
    const clip = await add.mutateAsync({
      caseId: target.id,
      clip: { cameraIds, from: new Date(marks.from).toISOString(), to: new Date(marks.to).toISOString(), note: values.note.trim() || null, eventId: eventId ?? null },
    });
    onSaved({ caseId: target.id, caseName: target.name, clip });
  }

  const busy = create.isPending || add.isPending || cases.isPending || (creating && !values.name.trim());

  return (
    <ConfirmDialog title="Save to case" confirmLabel="Save" busy={busy} onCancel={onClose} onConfirm={() => void submit().catch(() => undefined)}>
      <form className="stack" aria-label="Save to case" onSubmit={(e) => void submit(e).catch(() => undefined)}>
        <SelectField label="Case" value={caseId} options={options} onChange={(v) => set('caseId', v)} />
        {creating && <TextField label="Name of the new case" required maxLength={200} value={values.name} onChange={(v) => set('name', v)} />}
        <TextAreaField label="Note" rows={3} placeholder="What the clip shows" value={values.note} onChange={(v) => set('note', v)} />
        <p className="muted small">The video of an open case is kept until the case is closed.</p>
        <ErrorMessage error={create.error ?? add.error} />
      </form>
    </ConfirmDialog>
  );
}
