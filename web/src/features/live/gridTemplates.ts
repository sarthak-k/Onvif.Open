/** One tile's place in the grid (1-based CSS grid lines). */
export interface TilePlacement {
  column: number;
  row: number;
  columnSpan: number;
  rowSpan: number;
}

export interface GridTemplate {
  id: string;
  label: string;
  columns: number;
  rows: number;
  tiles: readonly TilePlacement[];
}

function uniform(columns: number, rows: number): GridTemplate {
  const tiles: TilePlacement[] = [];
  for (let row = 1; row <= rows; row++) {
    for (let column = 1; column <= columns; column++) tiles.push({ column, row, columnSpan: 1, rowSpan: 1 });
  }
  return { id: `${columns}x${rows}`, label: `${columns} × ${rows}`, columns, rows, tiles };
}

/** A large tile in the top left corner (span × span) and single tiles around it, in reading order. */
function featured(size: number, span: number): GridTemplate {
  const tiles: TilePlacement[] = [{ column: 1, row: 1, columnSpan: span, rowSpan: span }];
  for (let row = 1; row <= size; row++) {
    for (let column = 1; column <= size; column++) {
      if (row > span || column > span) tiles.push({ column, row, columnSpan: 1, rowSpan: 1 });
    }
  }
  return { id: `1+${tiles.length - 1}`, label: `1 large + ${tiles.length - 1}`, columns: size, rows: size, tiles };
}

/** The layouts offered; a new shape is one entry here (saved layouts keep the id). */
export const gridTemplates: readonly GridTemplate[] = [
  { ...uniform(1, 1), id: '1', label: 'Single' },
  { ...uniform(2, 1), label: '2 side by side' },
  uniform(2, 2),
  uniform(3, 2),
  uniform(3, 3),
  featured(3, 2),
  uniform(4, 3),
  featured(4, 3),
  uniform(4, 4),
  uniform(5, 5),
];

export const defaultTemplate = gridTemplates.find((t) => t.id === '2x2')!;

export function findTemplate(id: string): GridTemplate {
  return gridTemplates.find((t) => t.id === id) ?? defaultTemplate;
}

/** The smallest uniform grid that shows every camera (at least 2 × 2). */
export function templateFor(cameraCount: number): GridTemplate {
  return gridTemplates.find((t) => t.tiles.length >= cameraCount && t.tiles.every((tile) => tile.columnSpan === 1)) ?? gridTemplates[gridTemplates.length - 1];
}

/**
 * Whether a tile is small enough for the camera's smaller stream: not enlarged, and at most a quarter of the grid
 * (every tile of 2 × 2 and up, the small ones of 1 + 5; not the halves of a two-camera layout, nor the large tile).
 */
export function isSmallTile(template: GridTemplate, placement: TilePlacement, focused: boolean): boolean {
  if (focused) return false;
  return placement.columnSpan * placement.rowSpan * 4 <= template.columns * template.rows;
}
