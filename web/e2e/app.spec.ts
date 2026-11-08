import { expect, test, type Page } from '@playwright/test';
import { e2e } from '../playwright.config';

// The built app reads /config.json for the recorder's URL; point it at the e2e recorder.
test.beforeEach(async ({ page }) => {
  await page.route('**/config.json', (route) => route.fulfill({ json: { apiBaseUrl: e2e.recorderUrl } }));
});

async function signIn(page: Page, userName: string, password: string) {
  await page.goto('/');
  await page.getByLabel('User name').fill(userName);
  await page.getByLabel('Password', { exact: true }).fill(password);
  await page.getByRole('button', { name: 'Sign in' }).click();
}

test('an administrator manages cameras', async ({ page }) => {
  await signIn(page, 'admin', e2e.adminPassword);
  await expect(page.getByRole('heading', { name: 'Cameras' })).toBeVisible();
  await expect(page.getByText('No cameras yet.')).toBeVisible();

  await page.getByRole('link', { name: 'Add camera' }).click();
  await page.getByLabel('Id').fill('gate');
  await page.getByLabel('RTSP stream only').check();
  // Nothing listens on port 9: the test reports the camera unreachable.
  await page.getByLabel('RTSP URL').fill('rtsp://127.0.0.1:9/stream');
  await page.getByRole('button', { name: 'Test connection' }).click();
  await expect(page.getByRole('alert')).toContainText('cannot be reached');

  await page.getByLabel('User name').fill('viewer');
  await page.getByLabel('Password', { exact: true }).fill('camera-secret');
  await page.getByLabel('Record this camera').uncheck();
  await page.getByRole('button', { name: 'Add camera' }).click();

  const row = page.getByRole('row', { name: /gate/ });
  await expect(row).toContainText('RTSP 127.0.0.1:9');
  await expect(row).toContainText('disabled');

  // Edit: the stored password is kept, the mode changes.
  await row.getByRole('link', { name: 'gate' }).click();
  await expect(page.getByLabel('Password', { exact: true })).toHaveAttribute('placeholder', 'Unchanged');
  await page.getByLabel('On events and detections only').check();
  await page.getByLabel('Post-roll (seconds)').fill('25');
  await page.getByRole('button', { name: 'Save' }).click();
  await expect(page.getByRole('row', { name: /gate/ })).toContainText('Triggered');

  await page.getByRole('link', { name: 'gate' }).click();
  await expect(page.getByLabel('Post-roll (seconds)')).toHaveValue('25');
  await expect(page.getByLabel('Remove the stored password')).toBeVisible();
  await page.getByRole('link', { name: 'Back to cameras' }).click();

  await page.getByRole('row', { name: /gate/ }).getByRole('button', { name: 'Delete' }).click();
  await page.getByRole('dialog').getByRole('button', { name: 'Delete' }).click();
  await expect(page.getByText('No cameras yet.')).toBeVisible();
});

test('a new user chooses a password and sees only their pages', async ({ page }) => {
  await signIn(page, 'admin', e2e.adminPassword);
  await page.getByRole('link', { name: 'Users' }).click();

  const form = page.getByRole('form', { name: 'Add user' });
  await form.getByLabel('User name').fill('olivia');
  await form.getByLabel('Initial password').fill('Initial-Password-1');
  await form.getByLabel('Role').selectOption('Operator');
  await form.getByRole('button', { name: 'Add user' }).click();
  await expect(page.getByRole('row', { name: /olivia/ })).toContainText('must change password');

  await page.getByRole('button', { name: 'Sign out' }).click();
  await signIn(page, 'olivia', 'Initial-Password-1');

  await expect(page.getByRole('heading', { name: 'Choose a new password' })).toBeVisible();
  await page.getByLabel('Current password').fill('Initial-Password-1');
  await page.getByLabel('New password', { exact: true }).fill('Olivia-Own-Password-2');
  await page.getByLabel('Repeat new password').fill('Olivia-Own-Password-2');
  await page.getByRole('button', { name: 'Change password' }).click();

  await expect(page.getByRole('heading', { name: 'Cameras' })).toBeVisible();
  await expect(page.getByRole('link', { name: 'Users' })).toHaveCount(0);
  await expect(page.getByRole('link', { name: 'Add camera' })).toHaveCount(0);

  // The session survives a reload only with "keep me signed in" or in the same tab.
  await page.reload();
  await expect(page.getByRole('heading', { name: 'Cameras' })).toBeVisible();

  await page.getByRole('button', { name: 'Sign out' }).click();
  await signIn(page, 'olivia', 'Initial-Password-1');
  await expect(page.getByRole('alert')).toContainText('Wrong user name or password');
});
