import { useMemo, useState, type FormEvent } from 'react';
import { Link, useNavigate, useParams } from 'react-router-dom';
import type { Case, CaseClip } from '../../api/types';
import { useCase, useDeleteCase, useRemoveClip, useUpdateCase, useUpdateClip } from '../../data/cases';
import { TextAreaField, TextField } from '../../shared/form/fields';
import { formatDate } from '../../shared/format';
import { useForm } from '../../shared/form/useForm';
import { ConfirmDialog } from '../../shared/ui/ConfirmDialog';
import { ErrorMessage } from '../../shared/ui/ErrorMessage';
import { useUser } from '../auth/AuthContext';
import { can } from '../auth/permissions';
import { ExportButton } from '../exports/ExportButton';
import { SyncedPlayer } from '../investigate/SyncedPlayer';
import { useSites } from '../../data/tenancy';
import { formatClock, formatDay, formatDuration, zoneName } from '../playback/time';

/** Video shown around a clip when it is played. */
const aroundMs = 30_000;

/** "Mon, 9 Nov 2026, 22:04:10 – 22:04:40 CET (30 s)", in the clip's site time. */
function describeClip(clip: CaseClip, timeZone?: string): string {
  const from = Date.parse(clip.from);
  const to = Date.parse(clip.to);
  const zone = zoneName(from, timeZone);
  return `${formatDay(from, timeZone)}, ${formatClock(from, true, timeZone)} – ${formatClock(to, true, timeZone)}${zone ? ` ${zone}` : ''} (${formatDuration(to - from)})`;
}

/** One case: what it is about, its clips with notes, and playing them back. */
export default function CasePage() {
  const { id = '' } = useParams();
  const found = useCase(id);
  const user = useUser();
  const canEdit = can(user, 'cameras.control');
  const canDelete = can(user, 'cameras.manage');
  const update = useUpdateCase(id);
  const remove = useDeleteCase();
  const removeClip = useRemoveClip(id);
  const navigate = useNavigate();
  const [playing, setPlaying] = useState<CaseClip | null>(null);
  const [editing, setEditing] = useState(false);
  const [deleting, setDeleting] = useState(false);
  const [noting, setNoting] = useState<CaseClip | null>(null);
  const [removing, setRemoving] = useState<CaseClip | null>(null);
  const item = found.data;
  const sites = useSites();
  const zoneOf = (id: string | null) => sites.data?.find((s) => s.id === id)?.timeZone;
  const window = useMemo(() => (playing ? { from: Date.parse(playing.from) - aroundMs, to: Date.parse(playing.to) + aroundMs } : null), [playing]);

  if (found.isError) {
    return (
      <section className="stack">
        <ErrorMessage error={found.error} />
        <Link to="/cases">Back to cases</Link>
      </section>
    );
  }
  if (!item) return null;

  const open = item.status === 'Open';
  const hidden = (item.clipCount ?? 0) - (item.clips?.length ?? 0);

  return (
    <section className="stack">
      <Link to="/cases">Back to cases</Link>
      <div className="heading">
        <h1>
          {item.name} <span className="badge">{item.status}</span>
        </h1>
        <div className="actions">
          {canEdit && (
            <>
              <button type="button" onClick={() => setEditing(true)}>
                Edit
              </button>
              <button type="button" onClick={() => update.mutate({ status: open ? 'Closed' : 'Open' })} disabled={update.isPending}>
                {open ? 'Close case' : 'Reopen case'}
              </button>
            </>
          )}
          {canDelete && (
            <button type="button" className="danger" onClick={() => setDeleting(true)}>
              Delete
            </button>
          )}
        </div>
      </div>
      {item.description && <p>{item.description}</p>}
      <p className="muted small">
        Opened by {item.createdBy ?? 'someone'} on {formatDate(item.createdAt)}.{' '}
        {open ? 'Its video is kept while the case is open.' : `Closed on ${formatDate(item.closedAt)}: its video follows the cameras' retention again.`}
      </p>
      <ErrorMessage error={update.error ?? remove.error ?? removeClip.error} />

      <h2>Clips</h2>
      {item.clips?.length === 0 && <p className="muted">No clips yet. In Investigate, mark the start and end of what matters and save it here.</p>}
      {hidden > 0 && <p className="muted small">{hidden} more clip(s) are of sites you do not see.</p>}
      <ul className="event-cards" aria-label="Clips">
        {item.clips?.map((clip) => (
          <li key={clip.id} className="card stack" aria-label={describeClip(clip, zoneOf(clip.siteId))}>
            <div className="heading">
              <strong>{describeClip(clip, zoneOf(clip.siteId))}</strong>
              <div className="actions">
                <button type="button" className="primary" aria-pressed={playing?.id === clip.id} onClick={() => setPlaying(clip)}>
                  Play
                </button>
                <ExportButton cameraIds={clip.cameraIds} from={Date.parse(clip.from)} to={Date.parse(clip.to)} caseId={item.id} />
                {canEdit && (
                  <>
                    <button type="button" onClick={() => setNoting(clip)}>
                      Edit note
                    </button>
                    <button type="button" className="danger" onClick={() => setRemoving(clip)}>
                      Remove
                    </button>
                  </>
                )}
              </div>
            </div>
            <span className="muted small">
              {clip.cameras.map((c) => c.name).join(', ')} · saved by {clip.createdBy ?? 'someone'}
            </span>
            {clip.note && <p className="clip-note">{clip.note}</p>}
          </li>
        ))}
      </ul>

      {playing && window && (
        <SyncedPlayer
          siteId={playing.siteId}
          from={window.from}
          to={window.to}
          start={Date.parse(playing.from)}
          startLabel="the clip"
          timeZone={zoneOf(playing.siteId)}
          cameras={playing.cameras.map((c) => ({ cameraId: c.id, name: c.name }))}
          initialMarks={{ from: Date.parse(playing.from), to: Date.parse(playing.to) }}
        />
      )}

      {editing && <EditCaseDialog item={item} onClose={() => setEditing(false)} />}
      {noting && <NoteDialog caseId={id} clip={noting} onClose={() => setNoting(null)} />}
      {removing && (
        <ConfirmDialog
          title="Remove this clip?"
          confirmLabel="Remove"
          danger
          onCancel={() => setRemoving(null)}
          onConfirm={() => {
            removeClip.mutate(removing.id);
            if (playing?.id === removing.id) setPlaying(null);
            setRemoving(null);
          }}
        >
          Its video is no longer kept for this case.
        </ConfirmDialog>
      )}
      {deleting && (
        <ConfirmDialog
          title={`Delete case ${item.name}?`}
          confirmLabel="Delete"
          danger
          onCancel={() => setDeleting(false)}
          onConfirm={() => remove.mutate(item.id, { onSuccess: () => navigate('/cases') })}
        >
          Its clips and notes are deleted and their video is no longer kept for it. Closing the case keeps the record instead.
        </ConfirmDialog>
      )}
    </section>
  );
}

function EditCaseDialog({ item, onClose }: { item: Case; onClose(): void }) {
  const update = useUpdateCase(item.id);
  const { values, set } = useForm({ name: item.name, description: item.description ?? '' });

  function submit(event?: FormEvent) {
    event?.preventDefault();
    update.mutate({ name: values.name.trim(), description: values.description.trim() || null }, { onSuccess: onClose });
  }

  return (
    <ConfirmDialog title={`Edit ${item.name}`} confirmLabel="Save" busy={update.isPending || !values.name.trim()} onCancel={onClose} onConfirm={() => submit()}>
      <form className="stack" aria-label="Edit case" onSubmit={submit}>
        <TextField label="Name" required maxLength={200} value={values.name} onChange={(v) => set('name', v)} />
        <TextAreaField label="Description" rows={3} value={values.description} onChange={(v) => set('description', v)} />
        <ErrorMessage error={update.error} />
      </form>
    </ConfirmDialog>
  );
}

function NoteDialog({ caseId, clip, onClose }: { caseId: string; clip: CaseClip; onClose(): void }) {
  const update = useUpdateClip(caseId);
  const [note, setNote] = useState(clip.note ?? '');

  return (
    <ConfirmDialog
      title="Note"
      confirmLabel="Save"
      busy={update.isPending}
      onCancel={onClose}
      onConfirm={() => update.mutate({ clipId: clip.id, note: note.trim() || null }, { onSuccess: onClose })}
    >
      <TextAreaField label="What the clip shows" rows={4} value={note} onChange={setNote} />
      <ErrorMessage error={update.error} />
    </ConfirmDialog>
  );
}
