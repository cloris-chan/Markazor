import assert from 'node:assert/strict';
import { test } from 'node:test';
import { createServer } from 'node:http';
import { readFile, stat } from 'node:fs/promises';
import { createRequire } from 'node:module';
import { fileURLToPath } from 'node:url';
import { dirname, extname, resolve, sep } from 'node:path';

const repository = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const root = resolve(repository, process.env.MARKAZOR_PUBLISHED_ROOT || 'artifacts/template-smoke/publish/web/wwwroot');
const require = createRequire(new URL('../src/Markazor/package.json', import.meta.url));
const { chromium } = require('playwright');
const types = { '.html': 'text/html', '.js': 'text/javascript', '.css': 'text/css', '.json': 'application/json', '.wasm': 'application/wasm', '.svg': 'image/svg+xml', '.png': 'image/png', '.woff2': 'font/woff2', '.ttf': 'font/ttf' };

test('published Studio initializes and edits through the trimmed Blazor interop', { timeout: 60000 }, async t => {
  await stat(resolve(root, '_markazor/app.html'));
  const server = createServer(async (request, response) => {
    try {
      const url = new URL(request.url, 'http://localhost');
      const path = /^\/(studio|auth)(\/|$)/.test(url.pathname) ? '/_markazor/app.html' : decodeURIComponent(url.pathname);
      let file = resolve(root, '.' + path);
      if (file !== root && !file.startsWith(root + sep)) throw new Error('Outside published directory');
      if ((await stat(file)).isDirectory()) file = resolve(file, 'index.html');
      response.setHeader('Content-Type', types[extname(file)] || 'application/octet-stream');
      response.end(await readFile(file));
    } catch { response.writeHead(404).end('Not found'); }
  });
  await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
  t.after(async () => {
    server.closeAllConnections();
    await new Promise(resolve => server.close(resolve));
  });
  const browser = await chromium.launch();
  t.after(() => browser.close());
  const page = await browser.newPage({ viewport: { width: 1440, height: 1000 }, serviceWorkers: 'block' });
  const errors = [];
  const failed = [];
  const requests = [];
  page.on('pageerror', error => errors.push(error.message));
  page.on('console', message => { if (message.type() === 'error') errors.push(message.text()); });
  page.on('response', response => { if (response.status() >= 400) failed.push(`${response.status()} ${response.url()}`); });
  page.on('request', request => requests.push(request.url()));
  const origin = `http://127.0.0.1:${server.address().port}`;
  await page.goto(origin);
  await page.waitForLoadState('networkidle');
  assert.ok(!requests.some(url => /markazor-markdown-editor[^/]*\.js/.test(url)), 'Public reading must not load Muya');

  // The explicit local-preview flag uses fixtures; this test never writes GitHub content.
  await page.goto(`${origin}/studio/write?markazor-preview=1`);
  await page.waitForSelector('.is-muya-ready', { timeout: 20000 }).catch(error => {
    assert.fail(`Published editor failed to initialize: ${errors.join('\n')}\n${error.message}`);
  });
  assert.equal(await page.locator('.mu-container').count(), 1);
  assert.equal(await page.locator('.markazor-markdown-editor-fallback').isVisible(), false);
  assert.equal(await page.locator('.markazor-document-state .markazor-status-pill').innerText(), 'Saved');
  await page.locator('.mu-paragraph-content').first().click();
  await page.keyboard.press('End');
  await page.keyboard.insertText(' Published 繁體中文.');
  await page.waitForFunction(() => document.querySelector('.markazor-markdown-editor-fallback')?.value.includes('Published 繁體中文.'));
  await page.getByLabel('Insert image', { exact: true }).click();
  const png = await page.evaluate(() => {
    const canvas = document.createElement('canvas');
    canvas.width = canvas.height = 32;
    return canvas.toDataURL('image/png').split(',')[1];
  });
  await page.locator('input[type=file]').setInputFiles({ name: 'published.png', mimeType: 'image/png', buffer: Buffer.from(png, 'base64') });
  await page.waitForFunction(() => document.querySelector('.mu-inline-image img')?.naturalWidth === 32);
  const markdown = await page.locator('.markazor-markdown-editor-fallback').inputValue();
  assert.ok(markdown.includes('Published 繁體中文.'));
  assert.ok(markdown.includes('/assets/'));
  assert.ok(!markdown.includes('blob:'));
  await page.getByRole('button', { name: 'Undo', exact: true }).click();
  await page.getByRole('button', { name: 'Redo', exact: true }).click();
  await page.waitForFunction(() => document.querySelector('.mu-inline-image img')?.naturalWidth === 32);

  await page.setViewportSize({ width: 390, height: 844 });
  assert.equal(await page.locator('#markazor-content-browser').isVisible(), false);
  assert.equal(await page.evaluate(() => document.documentElement.scrollWidth), 390);
  await page.getByRole('button', { name: 'Articles', exact: true }).click();
  const openArticle = () => page.getByRole('button', { name: /Hello Workbench/ }).click();
  await Promise.all([page.waitForEvent('dialog').then(dialog => dialog.dismiss()), openArticle()]);
  assert.equal(await page.locator('.markazor-document-title h2').innerText(), 'Workbench Redesign');
  await Promise.all([page.waitForEvent('dialog').then(dialog => dialog.accept()), openArticle()]);
  await page.waitForFunction(() => document.querySelector('.markazor-document-title h2')?.textContent === 'Hello Workbench');
  assert.equal(await page.locator('#markazor-content-browser').isVisible(), false);
  assert.ok(!(await page.locator('.markazor-markdown-editor-fallback').inputValue()).includes('Published 繁體中文.'));
  assert.deepEqual(errors, []);
  assert.deepEqual(failed, []);
});
