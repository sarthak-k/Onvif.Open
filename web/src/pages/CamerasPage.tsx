import { useState } from 'react';
import { Link } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import type { CameraConfig, CameraStatus } from '../api/types';
import { hasRole, useAuth, useUser } from '../auth/AuthContext';
import { ConfirmDialog } from '../components/ConfirmDialog';
import { ErrorMessage } from '../components/ErrorMessage';
import { formatBytes, formatDate } from '../format';

interface Row {
  id: string;
  config?: CameraConfig;
  status?: CameraStatus;
}

/** Status (all roles) merged with the configuration (administrators). */
function merge(configs: CameraConfig[] | undefined, statuses: CameraStatus[] | undefined): Row[] {
  const rows = new Map<string, Row>();
  configs?.forEach((config) => rows.set(config.id, { id: config.id, config }));
  statuses?.forEach((status) => rows.set(status.cameraId, { ...rows.get(status.cameraId), id: status.cameraId, status }));
  return [...rows.values()].sort((a, b) => a.id.localeCompare(b.id));
}

function source(config: CameraConfig | undefined): string {
  const url = config?.onvifUrl || config?.rtspUrl;
  if (!url) return '—';
  try {
    return `${config?.onvifUrl ? 'ONVIF' : 'RTSP'} ${new URL(url).host}`;
  } catch {
    return url;
  }
}

export function CamerasPage() {
  const { api, client } = useAuth();
  const user = useUser();
  const admin = hasRole(user, 'Admin');
  const operator = hasRole(user, 'Operator');
  const queries = useQueryClient();

  const configs = useQuery({ queryKey: ['camera-configs'], queryFn: api.cameras.list, enabled: admin });
  const statuses = useQuery({ queryKey: ['camera-status'], queryFn: api.cameras.status, refetchInterval: 5000 });
  const refresh = () => {
    void queries.invalidateQueries({ queryKey: ['camera-configs'] });
    void queries.invalidateQueries({ queryKey: ['camera-status'] });
  };

  const remove = useMutation({ mutationFn: api.cameras.remove, onSettled: refresh });
  const control = useMutation({
    mutationFn: ({ id, action }: { id: string; action: 'start' | 'stop' }) => client.post(`/api/cameras/${encodeURIComponent(id)}/${action}`),
    onSettled: refresh,
  });
  const [deleting, setDeleting] = useState<string | null>(null);
  const rows = merge(configs.data, statuses.data);

  return (
    <section className="stack">
      <div className="heading">
        <h1>Cameras</h1>
        {admin && (
          <Link className="button primary" to="/cameras/new">
            Add camera
          </Link>
        )}
      </div>
      <ErrorMessage error={configs.error ?? statuses.error ?? remove.error ?? control.error} />

      {rows.length === 0 && !configs.isLoading && !statuses.isLoading ? (
        <p className="empty">No cameras yet.{admin && ' Add one, or find cameras on the network from the new camera page.'}</p>
      ) : (
        <div className="table-scroll"><table>
          <thead>
            <tr>
              <th>Camera</th>
              {admin && <th>Source</th>}
              <th>Mode</th>
              <th>State</th>
              <th>Codec</th>
              <th>Recorded</th>
              <th>Last frame</th>
              <th>
                <span className="sr-only">Actions</span>
              </th>
            </tr>
          </thead>
          <tbody>
            {rows.map(({ id, config, status }) => (
              <tr key={id}>
                <td>
                  {admin ? <Link to={`/cameras/${encodeURIComponent(id)}`}>{id}</Link> : id}
                  {config && !config.enabled && <span className="badge">disabled</span>}
                </td>
                {admin && <td>{source(config)}</td>}
                <td>{status?.mode ?? config?.mode ?? '—'}</td>
                <td>
                  <span className={`state state-${(status?.state ?? 'none').toLowerCase()}`}>{status?.state ?? (config?.enabled === false ? 'Disabled' : 'Starting')}</span>
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
                  {operator && status && (
                    <button type="button" onClick={() => control.mutate({ id, action: status.state === 'Stopped' ? 'start' : 'stop' })}>
                      {status.state === 'Stopped' ? 'Start' : 'Stop'}
                    </button>
                  )}
                  {admin && config && (
                    <button type="button" className="danger" onClick={() => setDeleting(id)}>
                      Delete
                    </button>
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table></div>
      )}

      {deleting && (
        <ConfirmDialog
          title={`Delete camera ${deleting}?`}
          confirmLabel="Delete"
          danger
          onCancel={() => setDeleting(null)}
          onConfirm={() => {
            remove.mutate(deleting);
            setDeleting(null);
          }}
        >
          The recorder stops recording it. Recordings already made are kept until retention removes them.
        </ConfirmDialog>
      )}
    </section>
  );
}
