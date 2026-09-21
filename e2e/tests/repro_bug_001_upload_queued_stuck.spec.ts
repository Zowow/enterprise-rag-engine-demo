import { test, expect } from '@playwright/test';
import * as path from 'path';
import * as fs from 'fs';
import { fileURLToPath } from 'url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

test.describe('BUG-001 Reproduction: Document Upload Ingestion Hang', () => {
  test('reproduces upload stuck at Queued 5% when worker cannot process queue', async ({ page }) => {
    // 1. Navigate to application and verify SignalR is connected
    await page.goto('/');
    await expect(page.locator('.rag-status-text')).toContainText(/Connected/i, { timeout: 10000 });

    const fixturePath = path.resolve(__dirname, '../fixtures/repro-policy.md');
    expect(fs.existsSync(fixturePath)).toBeTruthy();

    // 2. Select and upload file
    const fileInput = page.getByTestId('document-file-input');
    await fileInput.setInputFiles(fixturePath);

    const uploadBtn = page.getByTestId('document-upload-button');
    await expect(uploadBtn).toBeEnabled();
    await uploadBtn.click();

    // 3. Progress bar appears
    const progressBar = page.getByTestId('ingestion-progress-bar');
    await expect(progressBar).toBeVisible({ timeout: 10000 });

    // 4. Ingestion must proceed and reach Completed status
    const statusBadge = page.getByTestId('ingestion-status-badge').first();
    // This MUST reach Completed. If worker is offline or dead, it stays stuck in Queued / 5% and times out.
    await expect(statusBadge).toHaveText(/Completed/i, { timeout: 15000 });
  });
});
