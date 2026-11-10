import { useEffect, useMemo, useState, type KeyboardEvent } from 'react';
import type { PtzVector } from '../../../api/types';
import { useApi } from '../../../data/ApiContext';
import { usePtzPresets, usePtzSupport, useRemovePreset, useSavePreset } from '../../../data/ptz';
import { ErrorMessage } from '../../../shared/ui/ErrorMessage';
import { createPtzHold } from './ptzHold';

const speeds = [
  { value: '0.2', label: 'Slow' },
  { value: '0.5', label: 'Normal' },
  { value: '1', label: 'Fast' },
];

/** The pad: label and direction (pan, tilt; -1 left/down, 1 right/up). */
const pad: { label: string; symbol: string; pan: number; tilt: number }[] = [
  { label: 'Up and left', symbol: '↖', pan: -1, tilt: 1 },
  { label: 'Tilt up', symbol: '↑', pan: 0, tilt: 1 },
  { label: 'Up and right', symbol: '↗', pan: 1, tilt: 1 },
  { label: 'Pan left', symbol: '←', pan: -1, tilt: 0 },
  { label: 'Pan right', symbol: '→', pan: 1, tilt: 0 },
  { label: 'Down and left', symbol: '↙', pan: -1, tilt: -1 },
  { label: 'Tilt down', symbol: '↓', pan: 0, tilt: -1 },
  { label: 'Down and right', symbol: '↘', pan: 1, tilt: -1 },
];

/** Arrow keys and +/- while the panel has focus. */
const keys: Record<string, { pan?: number; tilt?: number; zoom?: number }> = {
  ArrowLeft: { pan: -1 },
  ArrowRight: { pan: 1 },
  ArrowUp: { tilt: 1 },
  ArrowDown: { tilt: -1 },
  '+': { zoom: 1 },
  '=': { zoom: 1 },
  '-': { zoom: -1 },
};

/**
 * Pan, tilt and zoom of an enlarged camera: hold a direction (or an arrow key) to move, presets, home. Operators only;
 * cameras without PTZ say so.
 */
export function PtzPanel({ cameraId, name }: { cameraId: string; name: string }) {
  const api = useApi();
  const support = usePtzSupport(cameraId);
  const presets = usePtzPresets(cameraId, support.supported);
  const save = useSavePreset(cameraId);
  const remove = useRemovePreset(cameraId);
  const [speed, setSpeed] = useState('0.5');
  const [preset, setPreset] = useState('');
  const [newPreset, setNewPreset] = useState('');
  const [error, setError] = useState<unknown>(null);

  const hold = useMemo(() => createPtzHold({ move: (v, ms) => api.ptz.move(cameraId, v, ms), stop: () => api.ptz.stop(cameraId) }, setError), [api, cameraId]);
  // Closing the panel (or the tile) stops a move in progress.
  useEffect(() => () => hold.dispose(), [hold]);

  const scaled = (v: { pan?: number; tilt?: number; zoom?: number }): PtzVector => {
    const s = Number(speed);
    return { pan: (v.pan ?? 0) * s, tilt: (v.tilt ?? 0) * s, zoom: (v.zoom ?? 0) * s };
  };
  const press = (v: { pan?: number; tilt?: number; zoom?: number }) => {
    setError(null);
    hold.press(scaled(v));
  };
  const once = (action: () => Promise<unknown>) => {
    setError(null);
    action().catch(setError);
  };
  const holdHandlers = (v: { pan?: number; tilt?: number; zoom?: number }) => ({
    onPointerDown: (e: React.PointerEvent) => {
      (e.currentTarget as HTMLElement).setPointerCapture?.(e.pointerId);
      press(v);
    },
    onPointerUp: () => hold.release(),
    onPointerCancel: () => hold.release(),
    onLostPointerCapture: () => hold.release(),
    onKeyDown: (e: KeyboardEvent) => {
      if ((e.key === ' ' || e.key === 'Enter') && !e.repeat) {
        e.preventDefault();
        press(v);
      }
    },
    onKeyUp: (e: KeyboardEvent) => (e.key === ' ' || e.key === 'Enter') && hold.release(),
  });
  const onKeyDown = (e: KeyboardEvent) => {
    const v = keys[e.key];
    if (!v) return;
    e.preventDefault();
    if (!e.repeat) press(v);
  };
  const onKeyUp = (e: KeyboardEvent) => keys[e.key] && hold.release();

  if (support.loading) return <div className="ptz-panel muted small">Checking pan, tilt and zoom…</div>;
  if (support.unsupported) return <div className="ptz-panel muted small" role="status">{name} cannot pan, tilt or zoom.</div>;

  return (
    <div className="ptz-panel" role="group" aria-label={`Pan, tilt and zoom of ${name}`} tabIndex={0} onKeyDown={onKeyDown} onKeyUp={onKeyUp}>
      <div className="ptz-pad">
        {pad.slice(0, 4).map((d) => (
          <button key={d.label} type="button" aria-label={d.label} title={d.label} {...holdHandlers(d)}>
            {d.symbol}
          </button>
        ))}
        <button type="button" aria-label="Home position" title="Home position" onClick={() => once(() => api.ptz.home(cameraId))}>
          ⌂
        </button>
        {pad.slice(4).map((d) => (
          <button key={d.label} type="button" aria-label={d.label} title={d.label} {...holdHandlers(d)}>
            {d.symbol}
          </button>
        ))}
      </div>
      <div className="ptz-zoom">
        <button type="button" aria-label="Zoom in" title="Zoom in" {...holdHandlers({ zoom: 1 })}>
          +
        </button>
        <button type="button" aria-label="Zoom out" title="Zoom out" {...holdHandlers({ zoom: -1 })}>
          −
        </button>
      </div>
      <label className="ptz-speed">
        Speed
        <select value={speed} onChange={(e) => setSpeed(e.target.value)}>
          {speeds.map((s) => (
            <option key={s.value} value={s.value}>
              {s.label}
            </option>
          ))}
        </select>
      </label>
      <div className="ptz-presets">
        <label>
          Preset
          <select value={preset} onChange={(e) => setPreset(e.target.value)}>
            <option value="">Choose…</option>
            {(presets.data ?? []).map((p) => (
              <option key={p.token} value={p.token}>
                {p.name || p.token}
              </option>
            ))}
          </select>
        </label>
        <button type="button" disabled={!preset} onClick={() => once(() => api.ptz.goToPreset(cameraId, preset))}>
          Go
        </button>
        <button type="button" disabled={!preset || remove.isPending} onClick={() => remove.mutate(preset, { onSuccess: () => setPreset(''), onError: setError })}>
          Delete
        </button>
        <form
          className="ptz-save"
          onSubmit={(e) => {
            e.preventDefault();
            if (newPreset.trim()) save.mutate(newPreset.trim(), { onSuccess: () => setNewPreset(''), onError: setError });
          }}
        >
          <input aria-label="New preset name" placeholder="Name this view" maxLength={64} value={newPreset} onChange={(e) => setNewPreset(e.target.value)} />
          <button type="submit" disabled={!newPreset.trim() || save.isPending}>
            Save
          </button>
        </form>
        <button type="button" className="link" onClick={() => once(() => api.ptz.setHome(cameraId))}>
          Make this the home position
        </button>
      </div>
      <ErrorMessage error={error ?? support.error} />
    </div>
  );
}
