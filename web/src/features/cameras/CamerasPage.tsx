import { useCallback, useMemo, useState } from 'react';
import { Link } from 'react-router-dom';
import { useCameraConfigs, useCameraStatus, useDeleteCamera, useRecordingControl } from '../../data/cameras';
import { useSites } from '../../data/tenancy';
import { ConfirmDialog } from '../../shared/ui/ConfirmDialog';
import { ErrorMessage } from '../../shared/ui/ErrorMessage';
import { useCan } from '../auth/AuthContext';
import { CameraTableRow } from './CameraTableRow';
import { mergeCameras } from './cameraRows';

export default function CamerasPage() {
  const canManage = useCan('cameras.manage');
  const canControl = useCan('cameras.control');

  const configs = useCameraConfigs(canManage);
  const statuses = useCameraStatus();
  const remove = useDeleteCamera();
  const control = useRecordingControl();
  const [deleting, setDeleting] = useState<{ id: string; name: string } | null>(null);

  const sites = useSites();
  const siteNames = useMemo(() => new Map((sites.data ?? []).map((s) => [s.id, s.name])), [sites.data]);
  const rows = useMemo(() => mergeCameras(configs.data, statuses.data, siteNames), [configs.data, statuses.data, siteNames]);
  const { mutate: toggle } = control;
  const onToggleRecording = useCallback((id: string, action: 'start' | 'stop') => toggle({ id, action }), [toggle]);
  const loading = configs.isLoading || statuses.isLoading;
  const onDelete = useCallback((id: string) => setDeleting({ id, name: rows.find((r) => r.id === id)?.name ?? id }), [rows]);

  return (
    <section className="stack">
      <div className="heading">
        <h1>Cameras</h1>
        {canManage && (
          <Link className="button primary" to="/cameras/new">
            Add camera
          </Link>
        )}
      </div>
      <ErrorMessage error={configs.error ?? statuses.error ?? remove.error ?? control.error} />

      {rows.length === 0 && !loading ? (
        <p className="empty">No cameras yet.{canManage && ' Add one, or find cameras on the network from the new camera page.'}</p>
      ) : (
        <div className="table-scroll">
          <table>
            <thead>
              <tr>
                <th>Camera</th>
                <th>Site</th>
                {canManage && <th>Source</th>}
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
              {rows.map((row) => (
                <CameraTableRow
                  key={row.id}
                  row={row}
                  siteName={row.siteId ? siteNames.get(row.siteId) : undefined}
                  canManage={canManage}
                  canControl={canControl}
                  onToggleRecording={onToggleRecording}
                  onDelete={onDelete}
                />
              ))}
            </tbody>
          </table>
        </div>
      )}

      {deleting && (
        <ConfirmDialog
          title={`Delete camera ${deleting.name}?`}
          confirmLabel="Delete"
          danger
          onCancel={() => setDeleting(null)}
          onConfirm={() => {
            remove.mutate(deleting.id);
            setDeleting(null);
          }}
        >
          The recorder stops recording it. Recordings already made are kept until retention removes them.
        </ConfirmDialog>
      )}
    </section>
  );
}
