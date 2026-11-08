import type { Layout } from '../../api/types';
import { findTemplate, templateFor, type GridTemplate } from './gridTemplates';

/** The live view being shown or edited: a grid and the camera of each tile. */
export interface LiveLayoutState {
  /** The saved layout it came from; undefined: not saved yet. */
  layoutId?: string;
  name: string;
  template: GridTemplate;
  cameras: (string | null)[];
  /** Changed since loaded or saved. */
  dirty: boolean;
}

export type LiveLayoutAction =
  | { type: 'load'; layout: Layout }
  | { type: 'new'; cameraIds: string[] }
  | { type: 'template'; template: GridTemplate }
  | { type: 'assign'; tile: number; cameraId: string | null }
  | { type: 'move'; from: number; to: number }
  | { type: 'saved'; layout: Layout };

/** The cameras list sized to the template: extra tiles empty, cameras beyond the last tile dropped. */
function fit(cameras: (string | null)[], template: GridTemplate): (string | null)[] {
  return Array.from({ length: template.tiles.length }, (_, i) => cameras[i] ?? null);
}

export function newLayout(cameraIds: string[]): LiveLayoutState {
  const template = templateFor(cameraIds.length);
  return { name: 'All cameras', template, cameras: fit(cameraIds, template), dirty: false };
}

export function liveLayoutReducer(state: LiveLayoutState, action: LiveLayoutAction): LiveLayoutState {
  switch (action.type) {
    case 'load': {
      const template = findTemplate(action.layout.grid);
      return { layoutId: action.layout.id, name: action.layout.name, template, cameras: fit(action.layout.cameras, template), dirty: false };
    }
    case 'new':
      return newLayout(action.cameraIds);
    case 'template': {
      // Cameras keep their order; switching to a smaller grid and back loses the ones that did not fit.
      const placed = state.cameras.filter((c): c is string => c !== null);
      const cameras = state.cameras.length <= action.template.tiles.length ? state.cameras : placed;
      return { ...state, template: action.template, cameras: fit(cameras, action.template), dirty: true };
    }
    case 'assign': {
      const cameras = [...state.cameras];
      cameras[action.tile] = action.cameraId;
      return { ...state, cameras, dirty: true };
    }
    case 'move': {
      // Dropping a tile on another swaps them.
      if (action.from === action.to) return state;
      const cameras = [...state.cameras];
      [cameras[action.from], cameras[action.to]] = [cameras[action.to], cameras[action.from]];
      return { ...state, cameras, dirty: true };
    }
    case 'saved':
      return { ...state, layoutId: action.layout.id, name: action.layout.name, dirty: false };
  }
}
