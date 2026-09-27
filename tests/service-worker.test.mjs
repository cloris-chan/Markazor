import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
import vm from 'node:vm';

const source = await readFile(new URL('../src/Markazor/buildTransitive/defaults/wwwroot/service-worker.published.js', import.meta.url), 'utf8');

function createWorker() {
  const origin = 'https://journal.example';
  const entries = new Map([
    ['/index.html', { body: 'Home', redirected: true, encoded: true }],
    ['/posts/article/index.html', { body: 'Article', redirected: true, encoded: true }],
    ['/tags/c%23/index.html', { body: 'C# collection', redirected: true, encoded: true }],
    ['/_markazor/content/posts/C%23%20hello%25.md', { body: '# Reserved filename', redirected: false }],
    ['/_markazor/app.html', { body: 'Studio shell', redirected: true, encoded: true }],
    ['/404.html', { body: 'Page not found', redirected: false }],
    ['/site.css', { body: 'body {}', redirected: false }],
  ]);
  const manifestAssets = [
    '/index.html',
    '/posts/article/index.html',
    '/tags/c#/index.html',
    '/_markazor/content/posts/C# hello%.md',
    '/_markazor/app.html',
    '/404.html',
    '/site.css',
    '/_markazor/content/drafts/private.md',
  ];
  const events = new Map();
  const state = { network: [], installed: [], deleted: [], claimed: false, skipped: false, online: false };
  const worker = {
    origin,
    assetsManifest: { version: 'current', assets: manifestAssets.map(url => ({ url })) },
    markazorRoutes: { '/': '/index.html', '/posts/article': '/posts/article/index.html', '/tags/c%23': '/tags/c%23/index.html' },
    importScripts() {},
    addEventListener(name, listener) { events.set(name, listener); },
    clients: { async claim() { state.claimed = true; } },
    skipWaiting() { state.skipped = true; },
  };
  const context = vm.createContext({
    self: worker,
    URL,
    Response,
    Request: class extends Request {
      constructor(input, options) { super(new URL(input, origin), options); }
    },
    console: { info() {} },
    caches: {
      async open() {
        return {
          async match(input) {
            const path = new URL(typeof input === 'string' ? input : input.url, origin).pathname;
            const entry = entries.get(path);
            if (!entry) return undefined;
            const response = new Response(entry.body, entry.encoded ? { headers: { 'Content-Encoding': 'br', 'Content-Length': '123', 'Content-Type': 'text/html' } } : undefined);
            if (entry.redirected) Object.defineProperty(response, 'redirected', { value: true });
            return response;
          },
          async addAll(requests) { state.installed = requests.map(request => request.url); },
        };
      },
      async keys() { return ['offline-cache-current', 'offline-cache-old', 'unrelated-cache']; },
      async delete(name) { state.deleted.push(name); return true; },
    },
    async fetch(input) {
      const url = String(input.url || input);
      state.network.push(url);
      if (url.includes('/api/') || !url.startsWith(origin)) return new Response('Network response');
      if (state.online) return new Response('Network navigation');
      throw new TypeError('Offline');
    },
  });
  vm.runInContext(source, context);
  const request = (path, overrides = {}) => context.onFetch({ request: { url: new URL(path, origin).href, method: 'GET', mode: 'navigate', ...overrides } });
  return { context, entries, events, request, state };
}

test('offline navigation returns the requested article, including a trailing slash or query', async () => {
  const { request, state } = createWorker();
  const article = await request('/posts/article/?view=reading');
  const home = await request('/');
  const collection = await request('/tags/c%23');
  assert.equal(article.redirected, false);
  assert.equal(home.redirected, false);
  assert.equal(collection.redirected, false);
  assert.equal(article.headers.get('content-encoding'), null);
  assert.equal(article.headers.get('content-length'), null);
  assert.equal(article.headers.get('content-type'), 'text/html');
  assert.equal(await article.text(), 'Article');
  assert.equal(await home.text(), 'Home');
  assert.equal(await collection.text(), 'C# collection');
  assert.equal(state.network.length, 0);
});

test('Studio navigation uses the application shell', async () => {
  const { request } = createWorker();
  const response = await request('/studio/write?markazor-preview=1');
  assert.equal(response.redirected, false);
  assert.equal(await response.text(), 'Studio shell');
});

test('known routes with a cache miss fetch the canonical navigation URL', async () => {
  const { entries, request, state } = createWorker();
  entries.delete('/posts/article/index.html');
  state.online = true;
  const response = await request('/posts/article');
  assert.equal(await response.text(), 'Network navigation');
  assert.deepEqual(state.network, ['https://journal.example/posts/article']);
});

test('unknown offline routes return a real 404 response', async () => {
  const { request } = createWorker();
  const response = await request('/posts/missing');
  assert.equal(response.status, 404);
  assert.equal(await response.text(), 'Page not found');
});

test('public cached assets are served while missing assets return 503', async () => {
  const { request } = createWorker();
  assert.equal(await (await request('/site.css', { mode: 'cors' })).text(), 'body {}');
  assert.equal((await request('/missing.png', { mode: 'cors' })).status, 503);
});

test('API, cross-origin and non-GET requests bypass the static cache', async () => {
  const { request, state } = createWorker();
  await request('/api/setup/status');
  await request('https://other.example/resource');
  await request('/api/auth/github/start', { method: 'POST' });
  assert.equal(state.network.length, 3);
});

test('installation excludes drafts and activation only removes older owned caches', async () => {
  const { context, state } = createWorker();
  await context.onInstall({});
  assert.ok(state.installed.some(url => url.endsWith('/posts/article/index.html')));
  assert.ok(state.installed.some(url => url.endsWith('/tags/c%23/index.html')));
  assert.ok(state.installed.some(url => url.endsWith('/_markazor/content/posts/C%23%20hello%25.md')));
  assert.ok(state.installed.every(url => !url.includes('#')));
  assert.ok(state.installed.every(url => !url.includes('/drafts/')));
  await context.onActivate({});
  assert.deepEqual(state.deleted, ['offline-cache-old']);
  assert.equal(state.claimed, true);
});

test('waiting updates activate only after the explicit message', () => {
  const { events, state } = createWorker();
  assert.equal(state.skipped, false);
  events.get('message')({ data: { type: 'SKIP_WAITING' } });
  assert.equal(state.skipped, true);
});
