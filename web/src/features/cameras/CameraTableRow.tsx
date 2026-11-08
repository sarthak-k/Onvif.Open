import { memo } from 'react';
import { Link } from 'react-router-dom';
import { formatBytes, formatDate } from '../../shared/format';
import { describeSource, describeState, type CameraRow } from './cameraRows';

interface Props {
  row: CameraRow;
  canManage: boolean;
  canControl: boolean;
  onToggleRecording(id: string, action: 'start' | 'stop'): void;
  onDelete(id: string): void;
}

/** One camera; re-rendered only when its own data changes (the list polls every few seconds). */
export const CameraTableRow = memo(function CameraTableRow({ row, canManage, canControl, onToggleRecording, onDelete }: Props) {
  const { id, config, status } = row;
  const stopped = status?.state === 'Stopped';

  return (
    <tr>
      <td>
        {canManage ? <Link to={`/cameras/${encodeURIComponent(id)}`}>{id}</Link> : id}
        {config && !config.enabled && <span className="badge">disabled</span>}
      </td>
      {canManage && <td>{describeSource(config)}</td>}
      <td>{status?.mode ?? config?.mode ?? '—'}</td>
      <td>
        <span className={`state state-${(status?.state ?? 'none').toLowerCase()}`}>{describeState(row)}</span>
        {status?.lastError && (
          <div className="muted small" title={formatDate(status.lastErrorAt)}>
            {status.lastError}
          </div>
        )}
      </td>
      <td>{status ? [status.codec, status.recordsAudio ? status.audioCodec : null].filter(Boolean).join(' + ') : '—'}</td>
      <td>{status ? formatBytes(status.bytesRecorded) : '—'}</td>
      <td>{formatDate(status?.lastFrameAt)}</td>
      <td className="row-actions">
        {canControl && status && (
          <button type="button" onClick={() => onToggleRecording(id, stopped ? 'start' : 'stop')}>
            {stopped ? 'Start' : 'Stop'}
          </button>
        )}
        {canManage && config && (
          <button type="button" className="danger" onClick={() => onDelete(id)}>
            Delete
          </button>
        )}
      </td>
    </tr>
  );
});
