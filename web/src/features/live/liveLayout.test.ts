import type { Layout } from '../../api/types';
import { findTemplate, gridTemplates, templateFor } from './gridTemplates';
import { liveLayoutReducer, newLayout } from './liveLayout';

const layout = (grid: string, cameras: (string | null)[]): Layout => ({ id: 'l1', name: 'Saved', grid, cameras, shared: false, editable: true, updatedAt: '' });

describe('grid templates', () => {
  it('place every tile inside the grid without overlaps', () => {
    for (const template of gridTemplates) {
      const cells = new Set<string>();
      for (const tile of template.tiles) {
        for (let c = tile.column; c < tile.column + tile.columnSpan; c++) {
          for (let r = tile.row; r < tile.row + tile.rowSpan; r++) {
            expect(c).toBeLessThanOrEqual(template.columns);
            expect(r).toBeLessThanOrEqual(template.rows);
            expect(cells.has(`${c},${r}`)).toBe(false);
            cells.add(`${c},${r}`);
          }
        }
      }
      expect(cells.size).toBe(template.columns * template.rows);
    }
  });

  it('have unique ids the recorder accepts', () => {
    const ids = gridTemplates.map((t) => t.id);
    expect(new Set(ids).size).toBe(ids.length);
    ids.forEach((id) => expect(id).toMatch(/^[a-z0-9][a-z0-9+x-]{0,31}$/));
    expect(findTemplate('1+5').tiles).toHaveLength(6);
    expect(findTemplate('1+7').tiles).toHaveLength(8);
  });

  it('pick the smallest uniform grid for all cameras', () => {
    expect(templateFor(0).id).toBe('1');
    expect(templateFor(3).id).toBe('2x2');
    expect(templateFor(7).id).toBe('3x3');
    expect(templateFor(30).id).toBe('5x5');
  });
});

describe('live layout', () => {
  it('starts with every camera', () => {
    const state = newLayout(['a', 'b', 'c']);
    expect(state.template.id).toBe('2x2');
    expect(state.cameras).toEqual(['a', 'b', 'c', null]);
    expect(state.dirty).toBe(false);
  });

  it('loads a saved layout sized to its grid', () => {
    const state = liveLayoutReducer(newLayout([]), { type: 'load', layout: layout('1+5', ['a', null, 'b']) });
    expect(state).toMatchObject({ layoutId: 'l1', name: 'Saved', dirty: false });
    expect(state.cameras).toEqual(['a', null, 'b', null, null, null]);
  });

  it('falls back to 2 × 2 for a grid this version does not know', () => {
    expect(liveLayoutReducer(newLayout([]), { type: 'load', layout: layout('9x9', ['a']) }).template.id).toBe('2x2');
  });

  it('assigns, swaps and changes the grid, keeping the cameras in order', () => {
    let state = newLayout(['a', 'b', 'c']);
    state = liveLayoutReducer(state, { type: 'assign', tile: 3, cameraId: 'd' });
    state = liveLayoutReducer(state, { type: 'move', from: 0, to: 2 });
    expect(state.cameras).toEqual(['c', 'b', 'a', 'd']);
    expect(state.dirty).toBe(true);

    state = liveLayoutReducer(state, { type: 'template', template: findTemplate('3x3') });
    expect(state.cameras).toEqual(['c', 'b', 'a', 'd', null, null, null, null, null]);

    state = liveLayoutReducer(state, { type: 'assign', tile: 1, cameraId: null });
    state = liveLayoutReducer(state, { type: 'template', template: findTemplate('2x1') });
    // Shrinking packs the placed cameras first, so none is lost to an empty tile.
    expect(state.cameras).toEqual(['c', 'a']);
  });

  it('is clean after saving', () => {
    const state = liveLayoutReducer({ ...newLayout(['a']), dirty: true }, { type: 'saved', layout: { ...layout('1', ['a']), id: 'new', name: 'Mine' } });
    expect(state).toMatchObject({ layoutId: 'new', name: 'Mine', dirty: false });
  });
});
