import assert from 'node:assert/strict';
import { after, before, test } from 'node:test';
import { createServer } from 'node:http';
import { readFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import { dirname, extname, resolve, sep } from 'node:path';
import { createRequire } from 'node:module';
import { gzipSync } from 'node:zlib';
import { buildEditorAssets } from '../src/Markazor/assets/build-editor.mjs';
import { rewriteImageUrls } from '../src/Markazor/assets/markdown-images.js';

const require = createRequire(new URL('../src/Markazor/package.json', import.meta.url));
const engines = require('playwright');
const repository = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const output = resolve(repository, 'artifacts', `editor-browser-tests-${process.pid}`);
const browsers = (process.env.MARKAZOR_TEST_BROWSERS || 'chromium,firefox,webkit').split(',');
const mime = { '.js': 'text/javascript', '.css': 'text/css', '.png': 'image/png', '.svg': 'image/svg+xml', '.woff2': 'font/woff2', '.woff': 'font/woff', '.ttf': 'font/ttf' };
const pixel = Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jr1sAAAAASUVORK5CYII=', 'base64');
let server;
let origin;

before(async () => {
  const result = await buildEditorAssets(output);
  let initialBytes = 0;
  const seen = new Set();
  const inspect = async path => {
    if (seen.has(path)) return;
    seen.add(path);
    initialBytes += gzipSync(await readFile(resolve(repository, 'src/Markazor', path))).length;
    for (const item of result.metafile.outputs[path].imports) if (item.kind === 'import-statement' && result.metafile.outputs[item.path]) await inspect(item.path);
  };
  const entry = Object.keys(result.metafile.outputs).find(path => path.endsWith('/markazor-markdown-editor.js'));
  await inspect(entry);
  assert.ok(initialBytes < 512 * 1024, `Initial editor modules exceed 512 KiB gzip: ${initialBytes}`);
  console.log(`Editor initial JavaScript: ${initialBytes} bytes gzip.`);
  server = createServer(async (request, response) => {
    const path = new URL(request.url, 'http://localhost').pathname;
    try {
      if (path === '/') {
        response.setHeader('Content-Type', 'text/html');
        response.end('<!doctype html><html lang="en"><head><meta charset="utf-8"><link rel="stylesheet" href="/_content/Markazor/markazor-markdown-editor.css"></head><body><div id="root"><div id="host"></div></div><script type="module">import * as adapter from "/_content/Markazor/markazor-markdown-editor.js"; window.adapter = adapter;</script></body></html>');
        return;
      }
      if (path.startsWith('/assets/')) { response.setHeader('Content-Type', 'image/png'); response.end(pixel); return; }
      const file = resolve(output, '.' + path.replace(/^\/_content\/Markazor/, ''));
      if (!file.startsWith(output + sep)) throw new Error('Invalid asset path');
      response.setHeader('Content-Type', mime[extname(file)] || 'application/octet-stream');
      response.end(await readFile(file));
    } catch { response.writeHead(404).end(); }
  });
  await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
  origin = `http://127.0.0.1:${server.address().port}`;
});

after(async () => {
  if (server) {
    server.closeAllConnections();
    await new Promise(resolve => server.close(resolve));
  }
});

test('image display mapping preserves code samples, links, references, and attributes', () => {
  const source = '![Book](/assets/book.png "Caption"){width=640 height=480}\n\n`![Book](/assets/book.png)`\n\n```md\n![Book](/assets/book.png)\n```\n\n[link](/assets/book.png)\n\n![ref][image]\n\n[image]: /assets/book.png "Title"\n\n<img src="/assets/book.png" alt="raw">';
  const mapped = rewriteImageUrls(source, { '/assets/book.png': 'blob:local-test' });
  assert.match(mapped, /!\[Book\]\(blob:local-test "Caption"\)\{width=640 height=480\}/);
  assert.ok(mapped.includes('`![Book](/assets/book.png)`'));
  assert.ok(mapped.includes('```md\n![Book](/assets/book.png)\n```'));
  assert.ok(mapped.includes('[link](/assets/book.png)'));
  assert.ok(mapped.includes('[image]: blob:local-test "Title"'));
  assert.ok(mapped.includes('<img src="blob:local-test" alt="raw">'));
  assert.equal(rewriteImageUrls(mapped, { 'blob:local-test': '/assets/book.png' }), source);
});

for (const engine of browsers) {
  test(`${engine}: Markdown editing, formatting, images, composition, and lifecycle`, { timeout: 90000 }, async t => {
    const browser = await engines[engine].launch();
    t.after(() => browser.close());
    const page = await browser.newPage({ viewport: { width: 900, height: 800 } });
    const errors = [];
    page.on('pageerror', error => errors.push(error.message));
    await page.goto(origin);
    await page.waitForFunction(() => window.adapter);
    const load = async (markdown, sources = {}) => page.evaluate(({ markdown, sources }) => {
      window.calls = [];
      window.sources = sources;
      window.reference = { async invokeMethodAsync(method, ...args) { window.calls.push({ method, args }); } };
      window.adapter.initialize('editor', document.querySelector('#root'), document.querySelector('#host'), window.reference, markdown, false, sources, { enabled: true, maxBytes: 8 * 1024 * 1024 });
    }, { markdown, sources });
    const value = () => page.evaluate(() => window.adapter.getValue('editor'));

    const fixture = '## Traditional 中文\n\nOriginal paragraph.\n\nA footnote.[^source]\n\n[^source]: A source with **detail**.\n\n| Left | Right |\n| :--- | ---: |\n| One | Two |\n\n![Book](/assets/book.png "Caption"){width=640 height=480}\n\n[Reference link][guide]\n\n[guide]: https://example.test/reference "Guide"\n\n- Parent\n  - Child\n- [x] Done\n- [ ] Pending\n\n<div class="callout">Keep <em>HTML</em>.</div>\n\n<!-- Keep this comment -->\n\n```js\nconst emoji = "😀";\n```';
    await load(fixture);
    assert.equal(await value(), fixture, 'Opening must preserve the original Markdown bytes');
    assert.equal(await page.locator('.mu-container').count(), 1);
    assert.equal(await page.locator('h2 .mu-plain-text').innerText(), 'Traditional 中文');
    assert.equal(await page.locator('table').count(), 1);
    await page.locator('.mu-paragraph-content').first().click();
    await page.keyboard.press('End');
    await page.keyboard.type(' Updated.');
    await page.waitForFunction(() => window.calls.some(call => call.method === 'NotifyEditorChanged'));
    let edited = await value();
    assert.ok(edited.includes('Original paragraph. Updated.'), edited);
    assert.ok(edited.includes('[^source]: A source with **detail**.'));
    assert.ok(edited.includes('{width=640 height=480}'));
    assert.ok(edited.includes('https://example.test/reference'));
    assert.match(edited, /\n {2,4}[-*] Child/);
    assert.match(edited, /[-*] \[x\] Done/);
    assert.match(edited, /[-*] \[ \] Pending/);
    assert.ok(edited.includes('<div class="callout">Keep <em>HTML</em>.</div>'));
    assert.ok(edited.includes('<!-- Keep this comment -->'));
    assert.ok(edited.includes('const emoji = "😀";'));
    await page.keyboard.press('Control+s');
    await page.waitForFunction(() => window.calls.some(call => call.method === 'NotifySaveRequested'));

    await load('One paragraph.');
    const paragraph = page.locator('.mu-paragraph-content').first();
    await paragraph.click();
    await page.keyboard.press('End');
    await paragraph.dispatchEvent('compositionstart', { data: '' });
    await page.keyboard.insertText('繁體中文');
    await paragraph.dispatchEvent('compositionend', { data: '繁體中文' });
    await paragraph.dispatchEvent('input', { inputType: 'insertCompositionText', data: '繁體中文', isComposing: false });
    assert.equal((await value()).match(/繁體中文/g)?.length, 1);
    await page.evaluate(() => window.adapter.format('editor', 'undo'));
    await page.evaluate(() => window.adapter.format('editor', 'redo'));
    assert.equal((await value()).match(/繁體中文/g)?.length, 1);

    await load('');
    await page.locator('.mu-paragraph-content').click();
    await page.keyboard.type('## Typed heading');
    assert.ok((await value()).includes('## Typed heading'));
    assert.equal(await page.locator('h2').count(), 1, 'Markdown heading shortcuts must update the canvas');

    await load('| One | Two |\n| --- | --- |\n| A | B |');
    await page.locator('td').first().click();
    await page.keyboard.press('End');
    await page.keyboard.type(' edited');
    await page.keyboard.press('Tab');
    await page.keyboard.type('Next');
    const table = await value();
    assert.ok(table.includes('One edited'), table);
    assert.ok(table.includes('Next'), table);

    for (const command of ['heading2', 'heading3', 'unordered-list', 'ordered-list', 'quote', 'code-block', 'table', 'divider']) {
      await load('Convert me');
      await page.locator('.mu-paragraph-content').click();
      await page.evaluate(command => window.adapter.format('editor', command), command);
      assert.ok((await value()).trim().length > 0, `Empty Markdown after ${command}`);
      assert.ok((await value()).includes('Convert me'), `Formatting ${command} lost existing text`);
    }

    const blob = await page.evaluate(async () => {
      const canvas = document.createElement('canvas');
      canvas.width = canvas.height = 32;
      const image = await new Promise(resolve => canvas.toBlob(resolve, 'image/png'));
      return URL.createObjectURL(image);
    });
    await load('![Book](/assets/book.png "Caption"){width=640}\n\nText.', { '/assets/book.png': blob });
    assert.equal(await value(), '![Book](/assets/book.png "Caption"){width=640}\n\nText.');
    await page.waitForFunction(() => document.querySelector('.mu-inline-image img')?.naturalWidth === 32, null, { timeout: 5000 }).catch(async () => {
      throw new Error(await page.locator('#host').innerHTML());
    });
    await page.locator('.mu-paragraph-content').last().click();
    await page.keyboard.press('End');
    await page.keyboard.type(' More.');
    edited = await value();
    assert.ok(edited.includes('/assets/book.png'));
    assert.ok(!edited.includes('blob:'));

    await load('Drop here.');
    const clipboard = await page.evaluate(() => {
      const transfer = new DataTransfer();
      const file = new File([new Uint8Array([1, 2, 3])], 'pasted.png', { type: 'image/png' });
      transfer.items.add(file);
      const filesBefore = transfer.files.length;
      const event = new ClipboardEvent('paste', { bubbles: true, clipboardData: transfer });
      const eventFiles = event.clipboardData?.files.length ?? 0;
      // Firefox restricts files on a constructed clipboard event. Supply the
      // FileList contract explicitly when emulating a paste in that engine.
      if (eventFiles !== 1) Object.defineProperty(event, 'clipboardData', { value: { files: [file] } });
      document.querySelector('#host').dispatchEvent(event);
      return { filesBefore, eventFiles };
    });
    console.log(`${engine} synthetic clipboard: ${JSON.stringify(clipboard)}`);
    await page.waitForFunction(() => window.calls.some(call => ['NotifyImageFile', 'NotifyEditorError'].includes(call.method)));
    assert.ok(await page.evaluate(() => window.calls.some(call => call.method === 'NotifyImageFile')), JSON.stringify(await page.evaluate(() => window.calls)));
    assert.equal(await page.evaluate(() => window.calls.find(call => call.method === 'NotifyImageFile').args[0]), 'pasted.png');
    await page.evaluate(() => window.adapter.setDisabled('editor', true));
    await page.evaluate(() => window.adapter.format('editor', 'heading2'));
    assert.equal(await value(), 'Drop here.');
    await page.evaluate(() => window.adapter.dispose('editor'));
    assert.equal(await page.locator('.mu-container').count(), 0);
    for (const language of ['mermaid', 'vega-lite', 'plantuml', 'math']) {
      const markdown = '```' + language + '\nExample code\n```';
      await load(markdown);
      assert.equal(await value(), markdown);
      assert.equal(await page.locator('.mu-diagram, .mu-math-block').count(), 0, `${language} must remain a code block`);
    }
    await load('<img src="x" onerror="window.__editorXss=1">\n\n<script>window.__editorXss=2</script>\n\n[x](javascript:alert(1))');
    await value();
    assert.equal(await page.evaluate(() => window.__editorXss), undefined);
    assert.equal(await page.locator('#host script, #host [onerror], #host [onclick]').count(), 0);
    await load('Find this. Find that. Find another.');
    const results = await page.evaluate(() => [
      window.adapter.search('editor', 'Find'),
      window.adapter.search('editor', 'Find', 'next'),
      window.adapter.search('editor', 'Find', 'next'),
      window.adapter.search('editor', 'Find', 'next')
    ]);
    assert.deepEqual(results.map(result => result.count), [3, 3, 3, 3]);
    assert.deepEqual(results.map(result => result.index), [0, 1, 2, 0]);
    const previous = await page.evaluate(() => window.adapter.search('editor', 'Find', 'previous'));
    assert.equal(previous.index, 2);
    await page.locator('.mu-paragraph-content').click();
    await page.keyboard.press('End');
    await page.keyboard.type(' Find last.');
    await value();
    assert.equal((await page.evaluate(() => window.adapter.search('editor', 'Find', 'next'))).count, 4);
    await page.evaluate(() => window.adapter.setValue('editor', 'Replacement article.', {}, 'posts/replacement.md'));
    assert.equal(await value(), 'Replacement article.');
    await page.locator('.mu-paragraph-content').click();
    await page.keyboard.press('End');
    await page.keyboard.type(' New.');
    await value();
    assert.equal(await page.evaluate(() => window.calls.filter(call => call.method === 'NotifyEditorChanged').at(-1).args[1]), 'posts/replacement.md');
    for (const size of [20000, 200000]) {
      const line = 'A paragraph with 中文, **formatting**, and a [link](https://example.test).\n\n';
      const article = '![Book](/assets/book.png)\n\n' + line.repeat(Math.ceil(size / line.length));
      const start = performance.now();
      await load(article, { '/assets/book.png': blob });
      assert.equal(await value(), article);
      const mounted = performance.now();
      await page.locator('.mu-paragraph-content').last().click();
      await page.keyboard.press('End');
      await page.keyboard.type('Final word.');
      assert.ok((await value()).includes('Final word.'));
      console.log(`${engine} ${size} chars: mount ${(mounted-start).toFixed(0)}ms, edit+snapshot ${(performance.now()-mounted).toFixed(0)}ms`);
    }
    for (let index = 0; index < 10; index++) {
      await load('Document ' + index);
      assert.equal(await value(), 'Document ' + index);
      assert.equal(await page.locator('.mu-container').count(), 1);
    }
    assert.deepEqual(errors, []);
  });
}
