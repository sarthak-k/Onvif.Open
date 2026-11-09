import { memo, type CSSProperties, type DragEvent } from 'react';
import type { TilePlacement } from './gridTemplates';
import { VideoPlayer } from './player/VideoPlayer';

interface Props {
  index: number;
  cameraId: string | null;
  placement: TilePlacement;
  /** The cameras the user may see, by id, with their names. */
  cameras: readonly { id: string; name: string }[];
  editing: boolean;
  focused: boolean;
  onAssign(tile: number, cameraId: string | null): void;
  onMove(from: number, to: number): void;
  onToggleFocus(tile: number): void;
}

const dragType = 'application/x-onvif-tile';

/** One tile: a camera's live video, its name, and (while editing) a camera picker; tiles swap by drag and drop. */
export const LiveTile = memo(function LiveTile({ index, cameraId, placement, cameras, editing, focused, onAssign, onMove, onToggleFocus }: Props) {
  const style: CSSProperties = focused
    ? { gridColumn: '1 / -1', gridRow: '1 / -1' }
    : { gridColumn: `${placement.column} / span ${placement.columnSpan}`, gridRow: `${placement.row} / span ${placement.rowSpan}` };
  const camera = cameraId === null ? undefined : cameras.find((c) => c.id === cameraId);
  const missing = cameraId !== null && !camera;
  const name = camera?.name ?? cameraId;

  const onDragStart = (event: DragEvent) => event.dataTransfer.setData(dragType, String(index));
  const onDrop = (event: DragEvent) => {
    const from = Number(event.dataTransfer.getData(dragType));
    if (!Number.isNaN(from)) onMove(from, index);
  };

  return (
    <section
      className={`tile${editing ? ' editing' : ''}`}
      style={style}
      aria-label={cameraId ? `Tile ${index + 1}: ${missing ? 'missing camera' : name}` : `Tile ${index + 1}: empty`}
      draggable={editing}
      onDragStart={editing ? onDragStart : undefined}
      onDragOver={editing ? (e) => e.preventDefault() : undefined}
      onDrop={editing ? onDrop : undefined}
      onDoubleClick={() => cameraId && !missing && onToggleFocus(index)}
    >
      {cameraId && !missing ? (
        <VideoPlayer cameraId={cameraId} label={`Live video of ${name}`} />
      ) : (
        <div className="tile-empty">{missing ? 'This camera was removed, or you may not see it.' : editing ? 'Choose a camera' : 'Empty'}</div>
      )}
      <div className="tile-bar">
        {editing ? (
          <select aria-label={`Camera in tile ${index + 1}`} value={cameraId ?? ''} onChange={(e) => onAssign(index, e.target.value || null)}>
            <option value="">Empty</option>
            {cameras.map((c) => (
              <option key={c.id} value={c.id}>
                {c.name}
              </option>
            ))}
          </select>
        ) : (
          <span className="tile-name">{missing ? '' : (name ?? '')}</span>
        )}
        {cameraId && !missing && (
          <button type="button" className="tile-button" onClick={() => onToggleFocus(index)} aria-label={focused ? 'Back to the grid' : `Enlarge ${name}`}>
            {focused ? '⤡' : '⤢'}
          </button>
        )}
      </div>
    </section>
  );
});
