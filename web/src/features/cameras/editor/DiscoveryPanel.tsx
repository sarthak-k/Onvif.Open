import { memo } from 'react';
import type { DiscoveredDevice } from '../../../api/types';
import { useCameraConfigs, useDiscovery } from '../../../data/cameras';
import { ErrorMessage } from '../../../shared/ui/ErrorMessage';

interface Props {
  /** A device to add; `taken` lists the camera names already in use. */
  onUse(device: DiscoveredDevice, taken: string[]): void;
}

/** Cameras found with WS-Discovery; already configured ones are marked. */
export const DiscoveryPanel = memo(function DiscoveryPanel({ onUse }: Props) {
  const configs = useCameraConfigs();
  const find = useDiscovery();
  const taken = configs.data?.map((c) => c.name) ?? [];

  return (
    <div className="card stack">
      <div className="heading">
        <h2>Find cameras on the network</h2>
        <button type="button" onClick={() => find.mutate()} disabled={find.isPending}>
          {find.isPending ? 'Searching…' : 'Search'}
        </button>
      </div>
      <ErrorMessage error={find.error} />
      {find.data?.length === 0 && <p className="empty">No ONVIF cameras answered. They must be on the recorder's network.</p>}
      {!!find.data?.length && (
        <ul className="devices">
          {find.data.map((device) => (
            <li key={device.endpointReference ?? device.deviceServiceUri}>
              <div>
                <strong>{device.name || 'ONVIF device'}</strong> {device.hardware && <span className="muted">{device.hardware}</span>}
                <div className="muted small">{device.deviceServiceUri}</div>
              </div>
              {device.configuredCameraId ? (
                <span className="badge">added as {device.configuredCameraName ?? device.configuredCameraId}</span>
              ) : (
                <button type="button" onClick={() => onUse(device, taken)}>
                  Use
                </button>
              )}
            </li>
          ))}
        </ul>
      )}
    </div>
  );
});
