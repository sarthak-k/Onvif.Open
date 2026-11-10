import { useCallback, useEffect, useMemo, useReducer, useRef, useState } from 'react';
import type { Layout } from '../../api/types';
import { useCameraStatus } from '../../data/cameras';
import { useDeleteLayout, useLayouts, useSaveLayout } from '../../data/layouts';
import { ConfirmDialog } from '../../shared/ui/ConfirmDialog';
import { ErrorMessage } from '../../shared/ui/ErrorMessage';
import { useCan } from '../auth/AuthContext';
import { findTemplate, isSmallTile } from './gridTemplates';
import { liveLayoutReducer, newLayout } from './liveLayout';
import { LiveTile } from './LiveTile';
import { LiveToolbar } from './LiveToolbar';
import { SaveLayoutDialog } from './SaveLayoutDialog';

const lastLayoutKey = 'onvif-open.live-layout';

function remember(id: string | undefined) {
  try {
    if (id) localStorage.setItem(lastLayoutKey, id);
    else localStorage.removeItem(lastLayoutKey);
  } catch {
    // Storage blocked: the view starts with all cameras next time.
  }
}

function recall(): string | null {
  try {
    return localStorage.getItem(lastLayoutKey);
  } catch {
    return null;
  }
}

/** Live video of several cameras in a grid; layouts are saved per user, or shared by administrators. */
export default function LivePage() {
  const canShare = useCan('users.manage');
  const statuses = useCameraStatus();
  const layouts = useLayouts();
  const save = useSaveLayout();
  const remove = useDeleteLayout();

  const cameras = useMemo(() => (statuses.data ?? []).map((s) => ({ id: s.cameraId, name: s.name })).sort((a, b) => a.name.localeCompare(b.name)), [statuses.data]);
  const cameraIds = useMemo(() => cameras.map((c) => c.id), [cameras]);
  const [state, dispatch] = useReducer(liveLayoutReducer, [], newLayout);
  const [editing, setEditing] = useState(false);
  const [focused, setFocused] = useState<number | null>(null);
  const [dialog, setDialog] = useState<'save-as' | 'delete' | null>(null);
  const grid = useRef<HTMLDivElement>(null);
  const started = useRef(false);

  // First visit: the layout used last time, or every camera.
  useEffect(() => {
    if (started.current || !statuses.data || !layouts.data) return;
    started.current = true;
    if (state.dirty) return;
    const last = layouts.data.find((l) => l.id === recall());
    dispatch(last ? { type: 'load', layout: last } : { type: 'new', cameraIds });
    // Only the first arrival of the data matters here; later updates must not replace what the user arranged.
  }, [statuses.data, layouts.data, cameraIds]);

  useEffect(() => {
    const onKey = (event: KeyboardEvent) => event.key === 'Escape' && setFocused(null);
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, []);

  const loading = statuses.isLoading || layouts.isLoading;
  const current: Layout | undefined = layouts.data?.find((l) => l.id === state.layoutId);
  const request = (name: string, shared?: boolean) => ({ name, grid: state.template.id, cameras: state.cameras, shared });

  const selectLayout = (id: string) => {
    const layout = layouts.data?.find((l) => l.id === id);
    dispatch(layout ? { type: 'load', layout } : { type: 'new', cameraIds });
    remember(layout?.id);
    setFocused(null);
  };

  const saved = (layout: Layout) => {
    dispatch({ type: 'saved', layout });
    remember(layout.id);
    setDialog(null);
  };

  const onAssign = useCallback((tile: number, cameraId: string | null) => dispatch({ type: 'assign', tile, cameraId }), []);
  const onMove = useCallback((from: number, to: number) => dispatch({ type: 'move', from, to }), []);
  const onToggleFocus = useCallback((tile: number) => setFocused((f) => (f === tile ? null : tile)), []);

  return (
    <section className="live stack">
      <h1 className="sr-only">Live view</h1>
      {!loading && (
        <LiveToolbar
          state={state}
          layouts={layouts.data ?? []}
          editing={editing}
          canSave={!!current?.editable && state.dirty}
          canDelete={!!current?.editable}
          onSelectLayout={selectLayout}
          onSelectTemplate={(id) => dispatch({ type: 'template', template: findTemplate(id) })}
          onToggleEdit={() => setEditing((e) => !e)}
          onSave={() => current && save.mutate({ id: current.id, layout: request(state.name) }, { onSuccess: saved })}
          onSaveAs={() => {
            save.reset();
            setDialog('save-as');
          }}
          onDelete={() => setDialog('delete')}
          onFullscreen={() => void grid.current?.requestFullscreen?.().catch(() => undefined)}
        />
      )}
      <ErrorMessage error={statuses.error ?? layouts.error ?? (dialog ? null : (save.error ?? remove.error))} />

      {loading ? (
        <p className="loading">Loading…</p>
      ) : cameraIds.length === 0 && state.cameras.every((c) => c === null) ? (
        <p className="empty">No cameras yet.</p>
      ) : (
        <div
          ref={grid}
          className="live-grid"
          style={{ gridTemplateColumns: `repeat(${state.template.columns}, minmax(0, 1fr))`, gridTemplateRows: `repeat(${state.template.rows}, minmax(0, 1fr))` }}
        >
          {state.template.tiles.map((placement, index) =>
            focused === null || focused === index ? (
              <LiveTile
                key={index}
                index={index}
                cameraId={state.cameras[index]}
                placement={placement}
                cameras={cameras}
                editing={editing}
                focused={focused === index}
                small={isSmallTile(state.template, placement, focused === index)}
                onAssign={onAssign}
                onMove={onMove}
                onToggleFocus={onToggleFocus}
              />
            ) : null,
          )}
        </div>
      )}

      {dialog === 'save-as' && (
        <SaveLayoutDialog
          initialName={state.layoutId ? `${state.name} (copy)` : ''}
          canShare={canShare}
          busy={save.isPending}
          error={save.error}
          onCancel={() => setDialog(null)}
          onSave={(name, shared) => save.mutate({ layout: request(name, shared) }, { onSuccess: saved })}
        />
      )}
      {dialog === 'delete' && current && (
        <ConfirmDialog
          title={`Delete layout ${current.name}?`}
          confirmLabel="Delete"
          danger
          onCancel={() => setDialog(null)}
          onConfirm={() =>
            remove.mutate(current.id, {
              onSuccess: () => {
                setDialog(null);
                selectLayout('');
              },
            })
          }
        >
          {current.shared ? 'It disappears for every user.' : 'The cameras keep recording; only this arrangement is removed.'}
        </ConfirmDialog>
      )}
    </section>
  );
}
