import { useCallback, useMemo, useState } from 'react';
import { Link } from 'react-router-dom';
import { useCameraConfigs, useCameraStatus, useDeleteCamera, useRecordingControl } from '../../data/cameras';
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
  const [deleting, setDeleting] = useState<string | null>(null);

  const rows = useMemo(() => mergeCameras(configs.data, statuses.data), [configs.data, statuses.data]);
  const { mutate: toggle } = control;
  const onToggleRecording = useCallback((id: string, action: 'start' | 'stop') => toggle({ id, action }), [toggle]);
  const loading = configs.isLoading || statuses.isLoading;

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
                <CameraTableRow key={row.id} row={row} canManage={canManage} canControl={canControl} onToggleRecording={onToggleRecording} onDelete={setDeleting} />
              ))}
            </tbody>
          </table>
        </div>
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
