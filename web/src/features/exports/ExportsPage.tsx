import { useState } from 'react';
import type { VideoExport } from '../../api/types';
import { useApi } from '../../data/ApiContext';
import { useExports, useRevoke, useShare, useShareLinks } from '../../data/exports';
import { useSites } from '../../data/tenancy';
import { SelectField } from '../../shared/form/fields';
import { formatBytes, formatDate } from '../../shared/format';
import { ConfirmDialog } from '../../shared/ui/ConfirmDialog';
import { ErrorMessage } from '../../shared/ui/ErrorMessage';
import { useUser } from '../auth/AuthContext';
import { can } from '../auth/permissions';
import { formatClock, formatDay, zoneName } from '../playback/time';

const statusLabels: Record<VideoExport['status'], string> = {
  Queued: 'Waiting',
  Running: 'Being made…',
  Ready: 'Ready',
  Failed: 'Failed',
  Expired: 'Expired',
};

const durations = [
  { value: '24', label: '1 day' },
  { value: '72', label: '3 days' },
  { value: '168', label: '7 days' },
];

/** Watermarked MP4 exports (kept 7 days): download them, or share them with a link that expires. */
export default function ExportsPage() {
  const api = useApi();
  const exports = useExports();
  const sites = useSites();
  const canShare = can(useUser(), 'cameras.control');
  const [sharing, setSharing] = useState<VideoExport | null>(null);
  const zoneOf = (id: string | null) => sites.data?.find((s) => s.id === id)?.timeZone;

  return (
    <section className="stack">
      <div className="heading">
        <h1>Exports</h1>
      </div>
      <p className="muted">
        MP4 files of recordings, with the organization, camera, who exported them and the time of every frame drawn into the picture. They are kept for 7 days.
      </p>
      <ErrorMessage error={exports.error} />
      {exports.isSuccess && exports.data.length === 0 && <p className="muted">No exports yet. Mark a clip in Investigate or a case and choose Export MP4.</p>}

      {!!exports.data?.length && (
        <div className="table-scroll">
          <table>
            <thead>
              <tr>
                <th>Camera</th>
                <th>Recorded</th>
                <th>Status</th>
                <th>Exported by</th>
                <th>
                  <span className="sr-only">Actions</span>
                </th>
              </tr>
            </thead>
            <tbody>
              {exports.data.map((e) => {
                const zone = zoneOf(e.siteId);
                const from = Date.parse(e.from);
                const to = Date.parse(e.to);
                return (
                  <tr key={e.id}>
                    <td>{e.cameraName ?? e.cameraId}</td>
                    <td>
                      {formatDay(from, zone)}, {formatClock(from, true, zone)} – {formatClock(to, true, zone)} {zoneName(from, zone)}
                    </td>
                    <td>
                      <span className={`state state-${e.status.toLowerCase()}`}>{statusLabels[e.status]}</span>
                      {e.status === 'Failed' && e.error && <div className="muted small">{e.error}</div>}
                      {e.status === 'Ready' && (
                        <div className="muted small" title={`SHA-256 ${e.sha256}`}>
                          {formatBytes(e.size ?? 0)} · until {formatDate(e.expiresAt)}
                        </div>
                      )}
                    </td>
                    <td>
                      {e.requestedBy ?? '—'}
                      <div className="muted small">{formatDate(e.createdAt)}</div>
                    </td>
                    <td className="row-actions">
                      {e.downloadable && (
                        <a className="button" href={api.exports.downloadUrl(e.id)} download>
                          Download
                        </a>
                      )}
                      {e.downloadable && canShare && (
                        <button type="button" onClick={() => setSharing(e)}>
                          Share
                        </button>
                      )}
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      )}
      {sharing && <ShareDialog export={sharing} onClose={() => setSharing(null)} />}
    </section>
  );
}

/** Links to an export that work without signing in, until they expire or are revoked. */
function ShareDialog({ export: item, onClose }: { export: VideoExport; onClose(): void }) {
  const api = useApi();
  const links = useShareLinks(item.id);
  const share = useShare(item.id);
  const revoke = useRevoke(item.id);
  const [hours, setHours] = useState('72');
  const [copied, setCopied] = useState(false);
  const created = share.data?.path ? api.exports.sharedUrl(share.data.path) : null;

  return (
    <ConfirmDialog title={`Share ${item.cameraName ?? 'export'}`} confirmLabel="Done" onCancel={onClose} onConfirm={onClose}>
      <div className="stack">
        <p className="muted small">Anyone with the link can download this video until it expires. Every download is in the audit log.</p>
        <div className="row">
          <SelectField label="Link works for" value={hours} options={durations} onChange={setHours} />
          <button type="button" onClick={() => share.mutate(Number(hours))} disabled={share.isPending}>
            Create link
          </button>
        </div>
        {created && (
          <div className="stack">
            <label>
              New link (shown only now)
              <input readOnly value={created} onFocus={(e) => e.target.select()} />
            </label>
            <button
              type="button"
              className="link"
              onClick={() => {
                void navigator.clipboard?.writeText(created).then(() => setCopied(true), () => undefined);
              }}
            >
              {copied ? 'Copied' : 'Copy link'}
            </button>
          </div>
        )}
        <ErrorMessage error={share.error ?? revoke.error} />
        {!!links.data?.length && (
          <ul className="devices" aria-label="Links">
            {links.data.map((link) => (
              <li key={link.id}>
                <span className="small">
                  {link.revokedAt ? 'Revoked' : `Until ${formatDate(link.expiresAt)}`} · {link.downloads} download{link.downloads === 1 ? '' : 's'} · by {link.createdBy ?? '—'}
                </span>
                {!link.revokedAt && (
                  <button type="button" className="danger" onClick={() => revoke.mutate(link.id)}>
                    Revoke
                  </button>
                )}
              </li>
            ))}
          </ul>
        )}
      </div>
    </ConfirmDialog>
  );
}
