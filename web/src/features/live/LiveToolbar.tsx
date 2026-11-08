import type { Layout } from '../../api/types';
import { gridTemplates } from './gridTemplates';
import type { LiveLayoutState } from './liveLayout';

interface Props {
  state: LiveLayoutState;
  layouts: readonly Layout[];
  editing: boolean;
  canSave: boolean;
  canDelete: boolean;
  onSelectLayout(id: string): void;
  onSelectTemplate(id: string): void;
  onToggleEdit(): void;
  onSave(): void;
  onSaveAs(): void;
  onDelete(): void;
  onFullscreen(): void;
}

export const unsavedLayout = '';

export function LiveToolbar(props: Props) {
  const { state, layouts, editing } = props;

  return (
    <div className="toolbar" role="toolbar" aria-label="Live view">
      <label>
        Layout
        <select value={state.layoutId ?? unsavedLayout} onChange={(e) => props.onSelectLayout(e.target.value)}>
          <option value={unsavedLayout}>{state.layoutId ? 'All cameras' : `${state.name}${state.dirty ? ' (not saved)' : ''}`}</option>
          {layouts.map((layout) => (
            <option key={layout.id} value={layout.id}>
              {layout.name}
              {layout.shared ? ' (shared)' : ''}
              {layout.id === state.layoutId && state.dirty ? ' (changed)' : ''}
            </option>
          ))}
        </select>
      </label>
      <label>
        Grid
        <select value={state.template.id} onChange={(e) => props.onSelectTemplate(e.target.value)}>
          {gridTemplates.map((template) => (
            <option key={template.id} value={template.id}>
              {template.label}
            </option>
          ))}
        </select>
      </label>
      <div className="actions start">
        <button type="button" aria-pressed={editing} onClick={props.onToggleEdit}>
          {editing ? 'Done' : 'Edit cameras'}
        </button>
        {props.canSave && (
          <button type="button" className="primary" onClick={props.onSave}>
            Save
          </button>
        )}
        <button type="button" onClick={props.onSaveAs}>
          Save as…
        </button>
        {props.canDelete && (
          <button type="button" className="danger" onClick={props.onDelete}>
            Delete
          </button>
        )}
        <button type="button" onClick={props.onFullscreen}>
          Full screen
        </button>
      </div>
    </div>
  );
}
