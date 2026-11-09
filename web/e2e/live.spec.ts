import { expect, test, type Page } from '@playwright/test';
import { e2e } from '../playwright.config';

test.beforeEach(async ({ page }) => {
  await page.route('**/config.json', (route) => route.fulfill({ json: { apiBaseUrl: e2e.recorderUrl } }));
});

async function signIn(page: Page) {
  await page.goto('/');
  await page.getByLabel('User name').fill('admin');
  await page.getByLabel('Password', { exact: true }).fill(e2e.adminPassword);
  await page.getByRole('button', { name: 'Sign in' }).click();
  await expect(page.getByRole('heading', { name: 'Home' })).toBeVisible();
}

async function addCamera(page: Page, id: string) {
  await page.goto('/cameras/new');
  await page.getByLabel('Name', { exact: true }).fill(id);
  await page.getByLabel('RTSP stream only').check();
  // Nothing listens there: the camera is configured but sends no video.
  await page.getByLabel('RTSP URL').fill(`rtsp://127.0.0.1:9/${id}`);
  await page.getByRole('button', { name: 'Add camera' }).click();
  await expect(page.getByRole('link', { name: id, exact: true })).toBeVisible();
}

test('live view plays through the recorder and keeps saved layouts', async ({ page }) => {
  await signIn(page);
  await addCamera(page, 'gate');
  await addCamera(page, 'yard');

  await page.getByRole('link', { name: 'Live' }).click();
  const gate = page.getByRole('region', { name: 'Tile 1: gate' });
  await expect(gate).toBeVisible();

  // hls.js fetches the playlist from the recorder (another origin, bearer token in a header); the recorder has no
  // video for the camera and says so, and the tile shows it.
  const playlist = page.waitForRequest((r) => /\/api\/cameras\/[0-9a-f-]{36}\/live\.m3u8$/.test(r.url()));
  expect((await playlist).headers()['authorization']).toMatch(/^Bearer /);
  await expect(gate.getByRole('alert')).toContainText('No video from the camera yet.', { timeout: 30_000 });

  // A layout of its own: yard large, gate beside it.
  await page.getByLabel('Grid').selectOption('1+5');
  await page.getByRole('button', { name: 'Edit cameras' }).click();
  await page.getByLabel('Camera in tile 1').selectOption('yard');
  await page.getByLabel('Camera in tile 2').selectOption('gate');
  await page.getByRole('button', { name: 'Save as…' }).click();
  await page.getByRole('dialog').getByLabel('Name', { exact: true }).fill('Yard first');
  await page.getByRole('dialog').getByRole('button', { name: 'Save' }).click();
  await expect(page.getByRole('dialog')).toHaveCount(0);

  // Back after a reload: the layout used last is shown again.
  await page.reload();
  await expect(page.getByLabel('Layout')).toHaveValue(/.+/);
  await expect(page.getByLabel('Grid')).toHaveValue('1+5');
  await expect(page.getByRole('region', { name: 'Tile 1: yard' })).toBeVisible();
  await expect(page.getByRole('region', { name: 'Tile 2: gate' })).toBeVisible();

  await page.getByRole('button', { name: 'Delete' }).click();
  await page.getByRole('dialog').getByRole('button', { name: 'Delete' }).click();
  await expect(page.getByRole('option', { name: 'Yard first' })).toHaveCount(0);

  // The cameras never answered: Home says so first, and leads to when they went offline.
  await page.getByRole('link', { name: 'Home' }).click();
  await expect(page.getByRole('list', { name: 'Problems' })).toContainText('gate is not recording', { timeout: 30_000 });
  await expect(page.getByRole('list', { name: 'Sites' })).toContainText('0 of 2 cameras recording');

  // Investigate lists them as offline, and opens one on its timeline.
  await page.getByRole('link', { name: 'Investigate' }).first().click();
  const offline = page.getByRole('list', { name: 'Events' }).getByRole('button', { name: /Camera offline.*gate/ }).first();
  await expect(offline).toBeVisible({ timeout: 30_000 });
  await offline.click();
  await expect(page.getByRole('heading', { name: 'Camera offline · gate' })).toBeVisible();
  await expect(page.getByRole('figure', { name: 'gate' })).toBeVisible();
  await expect(page.getByRole('slider', { name: 'Time' })).toBeVisible();
  await page.getByRole('checkbox', { name: 'yard' }).check();
  await expect(page.getByRole('figure', { name: 'yard' })).toBeVisible();

  // Ten seconds of it go into a new case, kept with a note.
  await page.getByRole('button', { name: 'Mark in' }).click();
  await page.getByRole('button', { name: 'Forward 10 seconds' }).click();
  await page.getByRole('button', { name: 'Mark out' }).click();
  await page.getByRole('button', { name: 'Save to case' }).click();
  await page.getByRole('dialog').getByLabel('Name of the new case').fill('Gate went dark');
  await page.getByRole('dialog').getByLabel('Note').fill('Both cameras lost');
  await page.getByRole('dialog').getByRole('button', { name: 'Save' }).click();
  await page.getByRole('status').getByRole('link', { name: 'Gate went dark' }).click();
  await expect(page.getByRole('heading', { name: /Gate went dark/ })).toBeVisible();
  await expect(page.getByText('Both cameras lost')).toBeVisible();
  await expect(page.getByRole('list', { name: 'Clips' })).toContainText('gate, yard');
  // Nothing was recorded (the cameras never answered): exporting says so, and there is nothing to download.
  await page.getByRole('list', { name: 'Clips' }).getByRole('button', { name: 'Export MP4' }).click();
  await expect(page.getByRole('alert')).toContainText('Nothing was recorded in that time.');
  await page.getByRole('button', { name: 'Close case' }).click();
  await expect(page.getByRole('button', { name: 'Reopen case' })).toBeVisible();
  await page.getByRole('link', { name: 'Exports' }).click();
  await expect(page.getByText('No exports yet.', { exact: false })).toBeVisible();

  // Clean up for the other tests: they expect no cameras.
  for (const id of ['gate', 'yard']) {
    await page.goto('/cameras');
    await page.getByRole('row', { name: new RegExp(id) }).getByRole('button', { name: 'Delete' }).click();
    await page.getByRole('dialog').getByRole('button', { name: 'Delete' }).click();
    await expect(page.getByRole('link', { name: id, exact: true })).toHaveCount(0);
  }
});
