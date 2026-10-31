import assert from 'node:assert/strict';
import { after, before, test } from 'node:test';
import { createServer } from 'node:http';
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import { execFile } from 'node:child_process';
import { promisify } from 'node:util';
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
let published;
const publishingFixture = '## Traditional 中文 heading that stays identical when it wraps across narrow screens\n\nA paragraph with **bold**, *emphasis*, ~~removed~~, H~2~O, x^2^ and :smile:.\n\n### Details\n\n> A quoted paragraph.\n\n- Parent\n  - Child\n\n- [x] Done\n- [ ] Pending\n\n| Left | Right |\n| :--- | ---: |\n| One | Two |\n\n![Book](/assets/parity.svg "Caption")\n\n```csharp\nvar name = "world";\n```\n\n```css\n:root { --accent: #fff; color: var(--accent) !important; }\n```\n\n```json\n{"ready": true, "count": 42}\n```\n\n```html\n<div title="hello">Text</div>\n```\n\nA footnote.[^source]\n\n[^source]: A source with **detail**.\n\n<div><em>Raw HTML</em></div>';

before(async () => {
  const result = await buildEditorAssets(output);
  const site = resolve(output, 'site');
  const web = resolve(site, 'wwwroot');
  await mkdir(resolve(site, 'posts'), { recursive: true });
  await mkdir(web, { recursive: true });
  await writeFile(resolve(site, 'posts/parity.md'), '---\ntitle: Parity\n---\n\n' + publishingFixture);
  await writeFile(resolve(web, 'index.html'), '<!doctype html><html><head></head><body><div id="app"></div></body></html>');
  await writeFile(resolve(web, 'staticwebapp.config.json'), '{}');
  await promisify(execFile)('dotnet', [resolve(repository, 'src/Markazor.Build/bin/Release/net10.0/Markazor.Build.dll'), site, web]);
  published = JSON.parse(await readFile(resolve(web, '_markazor/content/posts/parity.md.json'), 'utf8'));
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
      if (path === '/' || path === '/published') {
        response.setHeader('Content-Type', 'text/html');
        const article = path === '/published' ? `<article id="published" class="markazor-prose">${published.html}</article>` : '';
        response.end('<!doctype html><html lang="en"><head><meta charset="utf-8"><link rel="stylesheet" href="/base.css"><link rel="stylesheet" href="/theme.css"><link rel="stylesheet" href="/_content/Markazor/markazor-markdown-editor.css"></head><body>' + article + '<div id="root"><button data-insert-block onmousedown="event.preventDefault()" onclick="window.adapter.showInsertMenu(\'editor\', this)">Insert block</button><div id="host" class="markazor-muya-host"></div></div><script type="module">import * as adapter from "/_content/Markazor/markazor-markdown-editor.js"; window.adapter = adapter;</script></body></html>');
        return;
      }
      const styles = { '/base.css': 'src/Markazor/wwwroot/markazor.css', '/markazor-prose.css': 'src/Markazor/wwwroot/markazor-prose.css', '/theme.css': 'src/Markazor.Themes/Themes/default/theme.css' };
      if (styles[path]) { response.setHeader('Content-Type', 'text/css'); response.end(await readFile(resolve(repository, styles[path]))); return; }
      if (path === '/assets/parity.svg') { response.setHeader('Content-Type', 'image/svg+xml'); response.end('<svg xmlns="http://www.w3.org/2000/svg" width="480" height="180"><rect width="480" height="180" fill="#eae7de"/></svg>'); return; }
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
  test(`${engine}: editor and static article share content, typography, and highlighting`, { timeout: 30000 }, async t => {
    const browser = await engines[engine].launch();
    t.after(() => browser.close());
    const page = await browser.newPage({ viewport: { width: 1200, height: 1000 } });
    await page.goto(origin + '/published');
    await page.waitForFunction(() => window.adapter);
    await page.evaluate(async markdown => {
      window.adapter.initialize('editor', document.querySelector('#root'), document.querySelector('#host'), { async invokeMethodAsync() {} }, markdown, false);
      const article = document.querySelector('#published');
      document.querySelector('.mu-container').style.cssText = 'width:672px;max-width:none;padding:0;';
      article.style.width = '672px';
      document.querySelector('#host').inert = true;
      await new Promise(requestAnimationFrame);
    }, publishingFixture);
    await page.waitForFunction(() => document.querySelector('.mu-code .token.keyword') && document.querySelector('.mu-code .token.property') && document.querySelector('.mu-code .token.attr-value') && document.querySelector('.mu-code .token.important'));
    const comparison = await page.evaluate(html => {
      const editor = document.querySelector('.mu-container');
      const article = document.querySelector('#published');
      const generated = document.createElement('template');
      generated.innerHTML = html;
      const text = node => {
        const copy = node.cloneNode(true);
        for (const hint of copy.querySelectorAll('.mu-remove,.footnote-backref')) hint.remove();
        return copy.textContent.trim();
      };
      const style = (node, name) => {
        const css = getComputedStyle(node);
        const keys = ['fontFamily', 'fontSize', 'lineHeight', 'fontWeight', 'color', 'backgroundColor', 'textAlign'];
        if (name !== 'root') keys.push('padding', 'margin');
        return Object.fromEntries(keys.map(key => [key, css[key]]));
      };
      return {
        headings: [editor, article].map(root => [...root.querySelectorAll('h2,h3')].map(text)),
        tables: [editor, article].map(root => [...root.querySelectorAll('th,td')].map(node => node.textContent)),
        images: [editor, article].map(root => root.querySelector('img')?.src),
        code: [editor.querySelector('.mu-code'), article.querySelector('pre code')].map(node => node.textContent.trim()),
        formatting: [editor, article].map(root => [...root.querySelector('p').querySelectorAll('strong,em,del,sub,sup')].map(node => [node.tagName, node.textContent])),
        footnote: [editor.querySelector('.mu-footnote p'), article.querySelector('.footnotes li p')].map(text),
        styles: [
          ['root', editor, article],
          ['heading', editor.querySelector('h2'), article.querySelector('h2')],
          ['header', editor.querySelector('td'), article.querySelector('th')],
          ['cell', editor.querySelectorAll('td')[2], article.querySelector('td')],
          ['checkbox', editor.querySelector('.mu-task-list-checkbox'), article.querySelector('input')],
          ['code', editor.querySelector('.mu-code-block'), article.querySelector('pre')],
          ['token', editor.querySelector('.token.keyword'), article.querySelector('.token.keyword')]
        ].map(([name, first, second]) => ({ name, first: style(first, name), second: style(second, name) })),
        tokens: [editor.querySelectorAll('.mu-code-block .token'), article.querySelectorAll('pre code .token')].map(nodes => [...nodes].map(node => [node.textContent, getComputedStyle(node).color])),
        generatedCode: [article, generated.content].map(root => [...root.querySelectorAll('pre code')].map(node => node.innerHTML))
      };
    }, published.html);
    await page.screenshot({ path: resolve(output, `${engine}-publishing.png`), fullPage: true });
    for (const name of ['headings', 'tables', 'images', 'code', 'formatting', 'footnote', 'tokens', 'generatedCode']) assert.deepEqual(comparison[name][0], comparison[name][1], `${engine}: ${name}`);
    for (const { name, first, second } of comparison.styles) assert.deepEqual(first, second, `${engine}: ${name}`);
    for (const width of [672, 320]) {
      await page.setViewportSize({ width: width + 32, height: 1000 });
      const heights = await page.evaluate(width => {
        const editor = document.querySelector('.mu-container');
        const article = document.querySelector('#published');
        editor.style.width = article.style.width = `${width}px`;
        return ['h2', 'p', 'blockquote', 'pre', 'table', 'ul'].map(selector => [editor, article].map(root => root.querySelector(selector).getBoundingClientRect().height));
      }, width);
      const images = await page.evaluate(() => ['.mu-container', '#published'].map(selector => {
        const root = document.querySelector(selector);
        const image = root.querySelector('img').getBoundingClientRect();
        return [image.width, image.height, image.x - root.getBoundingClientRect().x];
      }));
      for (let index = 0; index < 3; index++) assert.ok(Math.abs(images[0][index] - images[1][index]) <= 1, `${engine}: image layout at ${width}px`);
      for (const [first, second] of heights) assert.ok(Math.abs(first - second) <= 1, `${engine}: body layout at ${width}px: ${first} != ${second}`);
    }
  });
  test(`${engine}: Markdown editing, formatting, images, composition, and lifecycle`, { timeout: 90000 }, async t => {
    const browser = await engines[engine].launch();
    t.after(() => browser.close());
    const page = await browser.newPage({ viewport: { width: 900, height: 800 } });
    page.setDefaultTimeout(8000);
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

    const insertMenu = page.locator('.markazor-muya-popover:not([inert]) .mu-quick-insert');
    const formatToolbar = page.locator('.markazor-muya-popover:not([inert]) .mu-format-picker');
    await load('Select these words');
    await page.locator('.mu-paragraph-content').click();
    await page.keyboard.press('Home');
    await page.keyboard.press('Shift+End');
    await formatToolbar.getByRole('button', { name: 'Bold', exact: true }).click();
    assert.ok((await value()).includes('**Select these words**'));
    assert.equal(await formatToolbar.getByRole('button', { name: 'Bold', exact: true }).getAttribute('aria-pressed'), 'true');
    assert.equal(await formatToolbar.getByRole('button').count(), 6, 'Only supported inline tools belong in the selection toolbar');
    await page.keyboard.press('Escape');

    await load('Keep this paragraph');
    await page.locator('.mu-paragraph-content').click();
    await page.keyboard.press('Home');
    await page.keyboard.press('ArrowRight');
    await page.keyboard.press('ArrowRight');
    await page.getByRole('button', { name: 'Insert block', exact: true }).click();
    assert.equal(await value(), 'Keep this paragraph', 'Opening Insert must not dirty the document');
    await page.keyboard.press('Escape');
    assert.equal(await value(), 'Keep this paragraph', 'Cancelling Insert must not add an empty block');
    assert.equal(await page.evaluate(() => getSelection().anchorOffset), 2, 'Closing Insert must restore the original caret');
    assert.equal(await page.locator('[data-insert-block]').getAttribute('aria-expanded'), 'false');

    await page.getByRole('button', { name: 'Insert block', exact: true }).click();
    await insertMenu.getByRole('menuitem', { name: 'Text', exact: true }).waitFor();
    await page.keyboard.press('ArrowDown');
    await page.keyboard.press('Enter');
    assert.equal(await page.locator('h2').count(), 1, 'Insert must be operable with the keyboard');
    assert.ok((await value()).includes('Keep this paragraph'));

    for (const [name, selector] of [['Heading 2', 'h2'], ['Heading 3', 'h3'], ['Bulleted list', 'ul'], ['Numbered list', 'ol'], ['Task list', '.mu-task-list'], ['Quote', 'blockquote'], ['Code block', '.mu-code-block'], ['Table', 'table'], ['Divider', '.mu-thematic-break']]) {
      await load('Keep existing text');
      await page.locator('.mu-paragraph-content').click();
      await page.getByRole('button', { name: 'Insert block', exact: true }).click();
      assert.equal(await insertMenu.getByText(/Front Matter|Display Math|Mermaid|Vega Chart|Plantuml|Header 1/).count(), 0);
      await insertMenu.getByRole('menuitem', { name, exact: true }).click();
      assert.ok((await value()).includes('Keep existing text'), `Inserting ${name} lost existing text`);
      assert.equal(await page.locator('#host').locator(selector).count(), 1, `Insert ${name} must create its block`);
    }

    await load('');
    await page.locator('.mu-paragraph-content').click();
    await page.keyboard.type('/task');
    await insertMenu.getByRole('menuitem', { name: 'Task list', exact: true }).waitFor();
    await page.keyboard.press('Enter');
    await page.keyboard.type('From slash menu');
    assert.match(await value(), /[-*] \[ \] From slash menu/);

    await load('');
    await page.locator('.mu-paragraph-content').click();
    await page.keyboard.type('/zzzzzzzz');
    await insertMenu.getByText('No result', { exact: true }).waitFor();
    await page.keyboard.press('ArrowDown');
    await page.keyboard.press('Enter');
    assert.ok((await value()).includes('/zzzzzzzz'), 'An unmatched slash command must not insert an earlier result');
    await page.keyboard.press('Escape');

    await load('Block to convert');
    await page.locator('.mu-paragraph-content').hover();
    await page.locator('.markazor-muya-popover:not([inert]) .mu-front-button').click();
    const blockMenu = page.locator('.markazor-muya-popover:not([inert]) .mu-front-menu');
    await blockMenu.getByRole('menuitem', { name: 'Heading 2', exact: true }).click();
    assert.equal((await value()).trim(), '## Block to convert');
    await page.locator('h2').hover();
    await page.locator('.markazor-muya-popover:not([inert]) .mu-front-button').click();
    await blockMenu.getByRole('menuitem', { name: 'Duplicate', exact: true }).hover();
    await page.waitForFunction(() => document.querySelector('.mu-front-button-wrapper').inert);
    await blockMenu.getByRole('menuitem', { name: 'Duplicate', exact: true }).click();
    assert.equal((await value()).match(/## Block to convert/g)?.length, 2);
    await page.locator('h2').last().hover();
    await page.locator('.markazor-muya-popover:not([inert]) .mu-front-button').click();
    await blockMenu.getByRole('menuitem', { name: 'Delete', exact: true }).click();
    assert.equal((await value()).match(/## Block to convert/g)?.length, 1);

    await page.setViewportSize({ width: 320, height: 700 });
    await load('Narrow screen');
    await page.locator('.mu-paragraph-content').click();
    await page.getByRole('button', { name: 'Insert block', exact: true }).click();
    const menuBounds = await insertMenu.boundingBox();
    assert.ok(menuBounds.x >= 0 && menuBounds.x + menuBounds.width <= 320, JSON.stringify(menuBounds));
    await page.evaluate(() => window.adapter.setDisabled('editor', true));
    assert.equal(await page.locator('.markazor-muya-popover:not([inert])').count(), 0, 'Disabling must also disable tools mounted outside the editor');
    await page.setViewportSize({ width: 900, height: 800 });

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
      if (eventFiles !== 1) Object.defineProperty(event, 'clipboardData', { value: { files: [file] } });
      document.querySelector('#host').dispatchEvent(event);
      return { filesBefore, eventFiles };
    });
    console.log(`${engine} synthetic clipboard: ${JSON.stringify(clipboard)}`);
    await page.waitForFunction(() => window.calls.some(call => ['NotifyImageFile', 'NotifyEditorError'].includes(call.method)));
    assert.ok(await page.evaluate(() => window.calls.some(call => call.method === 'NotifyImageFile')), JSON.stringify(await page.evaluate(() => window.calls)));
    assert.equal(await page.evaluate(() => window.calls.find(call => call.method === 'NotifyImageFile').args[0]), 'pasted.png');
    await page.evaluate(() => window.adapter.setDisabled('editor', true));
    await page.evaluate(() => window.adapter.showInsertMenu('editor', document.querySelector('[data-insert-block]')));
    assert.equal(await value(), 'Drop here.');
    await page.evaluate(() => window.adapter.dispose('editor'));
    assert.equal(await page.locator('.mu-container').count(), 0);
    assert.equal(await page.locator('.markazor-muya-popover').count(), 0);
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
