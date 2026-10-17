import {
  Muya, CodeBlockLanguageSelector, FootnoteTool, ImageEditTool,
  LinkTools, TableColumnToolbar, TableDragBar, TableRowColumMenu
} from '@muyajs/core';
import '@muyajs/core/lib/core.css';
import './markazor-muya.css';
import { rewriteImageUrls, restoreDisplayUrls } from './markdown-images.js';

const editors = new Map();
const settled = () => new Promise(resolve => requestAnimationFrame(resolve));
for (const plugin of [CodeBlockLanguageSelector, FootnoteTool, TableColumnToolbar, TableDragBar, TableRowColumMenu]) Muya.use(plugin);
Muya.use(LinkTools, {
  jumpClick: info => {
    if (/^https?:\/\//i.test(info?.href ?? '')) window.open(info.href, '_blank', 'noopener,noreferrer');
  }
});
Muya.use(ImageEditTool);

function read(instance) {
  const markdown = restoreDisplayUrls(instance.muya.getMarkdown(), instance.originalUrls);
  return markdown === instance.initialNormalized ? instance.initialValue : markdown;
}

async function notify(instance) {
  if (instance.loading || instance.disposed) return;
  const value = read(instance);
  if (value === instance.value) return;
  instance.value = value;
  const revision = instance.inputRevision;
  const documentKey = instance.documentKey;
  await instance.dotNetReference.invokeMethodAsync('NotifyEditorChanged', value, documentKey);
  if (instance.disposed || instance.documentKey !== documentKey || instance.value !== value) return;
  instance.acknowledgedValue = value;
  instance.acknowledgedRevision = revision;
}

function updateSources(instance, sources) {
  instance.imageSources = new Map(Object.entries(sources ?? {}));
  // Undo history can still reference earlier display URLs.
  for (const [path, url] of instance.imageSources) instance.originalUrls.set(url, path);
}

function load(instance, value) {
  instance.loading = true;
  instance.initialValue = instance.value = value;
  if (instance.searchResult) instance.muya.search('');
  instance.searchText = null;
  instance.muya.setContent(rewriteImageUrls(value, instance.imageSources), false);
  instance.initialNormalized = restoreDisplayUrls(instance.muya.getMarkdown(), instance.originalUrls);
  instance.loading = false;
  setDisabled(instance.id, instance.disabled);
}

function activeBlock(instance) {
  let block = instance.muya.editor.selection.anchorBlock;
  if (!block) {
    block = instance.muya.editor.scrollPage.lastContentInDescendant();
    block?.setCursor(block.text.length, block.text.length, true);
  }
  return block;
}

export function initialize(id, root, host, dotNetReference, value, disabled, sources = {}, uploads = {}) {
  dispose(id);
  const mount = document.createElement('div');
  mount.className = 'markazor-muya-canvas';
  host.replaceChildren(mount);
  const instance = {
    id, root, host, dotNetReference, disabled: !!disabled, originalUrls: new Map(),
    imageSources: new Map(), loading: true, disposed: false, initialValue: value ?? '',
    value: value ?? '', acknowledgedValue: value ?? '', cleanups: [], uploads,
    documentKey: uploads.documentKey ?? null, inputRevision: 0, acknowledgedRevision: 0
  };
  updateSources(instance, sources);
  const muya = instance.muya = new Muya(mount, {
    markdown: rewriteImageUrls(instance.value, instance.imageSources),
    frontMatter: false, math: false, footnote: true, superSubScript: true,
    hideQuickInsertHint: true, fontSize: 18, lineHeight: 1.75
  });
  muya.init();
  instance.initialNormalized = restoreDisplayUrls(muya.getMarkdown(), instance.originalUrls);
  instance.loading = false;
  muya.on('json-change', () => { instance.searchText = null; void notify(instance); });
  const closeMore = event => {
    if (event.target.closest('.markazor-editor-more-tools button')) event.target.closest('details').open = false;
  };
  root.addEventListener('click', closeMore);
  instance.cleanups.push(() => root.removeEventListener('click', closeMore));
  const beforeUnload = event => {
    if (read(instance) !== instance.acknowledgedValue || instance.inputRevision !== instance.acknowledgedRevision) {
      event.preventDefault();
      event.returnValue = '';
    }
  };
  window.addEventListener('beforeunload', beforeUnload);
  instance.cleanups.push(() => window.removeEventListener('beforeunload', beforeUnload));
  const on = (name, handler, capture = false) => {
    host.addEventListener(name, handler, capture);
    instance.cleanups.push(() => host.removeEventListener(name, handler, capture));
  };
  on('input', () => {
    instance.inputRevision++;
    queueMicrotask(() => requestAnimationFrame(() => {
      if (!instance.disposed && read(instance) === instance.acknowledgedValue) instance.acknowledgedRevision = instance.inputRevision;
    }));
  }, true);
  on('keydown', event => {
    if ((event.ctrlKey || event.metaKey) && !event.altKey && !event.isComposing) {
      const command = { b: 'bold', i: 'italic', z: event.shiftKey ? 'redo' : 'undo', y: 'redo' }[event.key.toLowerCase()];
      if (command) {
        event.preventDefault(); event.stopPropagation();
        void format(id, command);
        return;
      }
    }
    if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 's') {
      event.preventDefault(); event.stopPropagation();
      if (!instance.disabled && !event.isComposing) void notify(instance).then(() => dotNetReference.invokeMethodAsync('NotifySaveRequested'));
    }
  }, true);
  const uploadFiles = async files => {
    for (const file of files) {
      if (instance.disposed || instance.disabled) break;
      if (file.size > uploads.maxBytes) {
        await dotNetReference.invokeMethodAsync('NotifyEditorError', 'Image uploads are limited to 8 MB.');
        continue;
      }
      const documentKey = instance.documentKey;
      try {
        const bytes = new Uint8Array(await file.arrayBuffer());
        if (instance.disposed || documentKey !== instance.documentKey) break;
        await dotNetReference.invokeMethodAsync('NotifyImageFile', file.name || 'pasted-image.png', file.type, bytes, documentKey);
      } catch {
        if (!instance.disposed && documentKey === instance.documentKey) {
          await dotNetReference.invokeMethodAsync('NotifyEditorError', 'Could not insert the image. Try choosing the file again.');
        }
      }
    }
  };
  on('paste', event => {
    if (!uploads.enabled || instance.disabled) return;
    const files = [...(event.clipboardData?.files ?? [])].filter(file => /^image\//i.test(file.type));
    if (!files.length) return;
    event.preventDefault(); event.stopImmediatePropagation();
    void uploadFiles(files);
  }, true);
  on('dragover', event => {
    if (uploads.enabled && !instance.disabled && [...(event.dataTransfer?.types ?? [])].includes('Files')) event.preventDefault();
  });
  on('drop', event => {
    const files = [...(event.dataTransfer?.files ?? [])].filter(file => /^image\//i.test(file.type));
    if (!uploads.enabled || instance.disabled || !files.length) return;
    event.preventDefault(); event.stopImmediatePropagation();
    void uploadFiles(files);
  }, true);
  editors.set(id, instance);
  root.classList.add('is-muya-ready');
  setDisabled(id, disabled);
}

export async function setValue(id, value, sources = {}, documentKey = null) {
  const instance = editors.get(id);
  if (!instance) return;
  await settled();
  if (instance.disposed) return;
  if (instance.documentKey !== documentKey) instance.originalUrls.clear();
  instance.documentKey = documentKey;
  updateSources(instance, sources);
  load(instance, value ?? '');
  instance.acknowledgedValue = instance.value;
  instance.acknowledgedRevision = instance.inputRevision;
}

export async function setImageSources(id, sources = {}) {
  const instance = editors.get(id);
  if (!instance) return;
  await settled();
  if (instance.disposed) return;
  const value = read(instance);
  const before = rewriteImageUrls(value, instance.imageSources);
  updateSources(instance, sources);
  if (before !== rewriteImageUrls(value, instance.imageSources)) load(instance, value);
}

export async function getValue(id) {
  const instance = editors.get(id);
  if (!instance) return null;
  // Muya applies text operations on the next animation frame. A synchronous
  // getMarkdown() here can save only a prefix of the last typed word.
  await settled();
  return instance.disposed ? null : read(instance);
}

export function setDisabled(id, disabled) {
  const instance = editors.get(id);
  if (!instance) return;
  instance.disabled = !!disabled;
  instance.host.inert = instance.disabled;
  instance.host.setAttribute('aria-disabled', String(instance.disabled));
}

export function focus(id) {
  const instance = editors.get(id);
  if (instance && !instance.disabled) instance.muya.focus();
}

export async function insertText(id, markdown, sources = {}) {
  const instance = editors.get(id);
  if (!instance || instance.disabled) return;
  await setImageSources(id, sources);
  activeBlock(instance);
  const selection = instance.muya.editor.selection;
  const current = selection.getSelection();
  if (current) selection.setSelection(current);
  const transfer = new DataTransfer();
  transfer.setData('text/plain', rewriteImageUrls(markdown, instance.imageSources));
  await instance.muya.editor.clipboard.pasteHandler(new ClipboardEvent('paste', { clipboardData: transfer }));
  await settled();
  await notify(instance);
}

export async function format(id, command) {
  const instance = editors.get(id);
  if (!instance || instance.disabled) return;
  const muya = instance.muya;
  if (command === 'undo' || command === 'redo') { await settled(); muya[command](); await notify(instance); return; }
  const block = activeBlock(instance);
  const inline = { bold: 'strong', italic: 'em', code: 'inline_code', link: 'link', image: 'image' }[command];
  if (inline) { block?.format?.(inline); await settled(); await notify(instance); return; }
  const anchor = block?.getAnchor();
  if (!anchor) return;
  const text = block.text.replace(/^#{1,6}\s+/, '');
  const original = anchor.getState();
  const states = {
    heading2: { name: 'atx-heading', meta: { level: 2 }, text: `## ${text}` },
    heading3: { name: 'atx-heading', meta: { level: 3 }, text: `### ${text}` },
    'unordered-list': { name: 'bullet-list', meta: { marker: '-', loose: false }, children: original.name === 'order-list' ? original.children : [{ name: 'list-item', children: [original] }] },
    'ordered-list': { name: 'order-list', meta: { start: 1, delimiter: '.', loose: false }, children: original.name === 'bullet-list' ? original.children : [{ name: 'list-item', children: [original] }] },
    quote: { name: 'block-quote', children: [original] },
    divider: { name: 'thematic-break', text: '---' },
    'code-block': { name: 'code-block', meta: { type: 'fenced', lang: '' }, text },
    table: { name: 'table', children: ['Heading', 'Text'].map(value => ({ name: 'table.row', children: [value, ''].map(cell => ({ name: 'table.cell', meta: { align: 'none' }, text: cell })) })) }
  };
  const state = states[command];
  if (!state) return;
  const type = muya.editor.scrollPage.constructor.loadBlock(state.name);
  const replacement = type.create(muya, state);
  const insert = command === 'table' || command === 'divider' || (original.children && !['quote', 'unordered-list', 'ordered-list'].includes(command));
  if (insert) anchor.parent.insertAfter(replacement, anchor);
  else anchor.replaceWith(replacement);
  const content = replacement.firstContentInDescendant();
  content?.setCursor(content.text.length, content.text.length, true);
  await settled();
  await notify(instance);
}

export function search(id, value, direction) {
  const instance = editors.get(id);
  if (!instance) return;
  if (instance.searchText !== value) {
    instance.searchResult = instance.muya.search(value, { isCaseSensitive: false });
    instance.searchText = value;
  }
  if (direction) instance.searchResult = instance.muya.find(direction);
  return { count: instance.searchResult.matches.length, index: instance.searchResult.index };
}

export function dispose(id) {
  const instance = editors.get(id);
  if (!instance) return;
  instance.disposed = true;
  instance.cleanups.forEach(cleanup => cleanup());
  instance.muya.destroy();
  instance.root.classList.remove('is-muya-ready');
  editors.delete(id);
}
