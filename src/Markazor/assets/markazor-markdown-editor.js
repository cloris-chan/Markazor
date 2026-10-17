import {
  Muya, CodeBlockLanguageSelector, FootnoteTool, ImageEditTool,
  LinkTools, TableColumnToolbar, TableDragBar, TableRowColumMenu
} from '@muyajs/core';
import '@muyajs/core/lib/core.css';
import './markazor-muya.css';
import { rewriteImageUrls, restoreDisplayUrls } from './markdown-images.js';
import { bindEditorTools, focusEditor, openInsertMenu, showBlockHandle } from './muya-tools.js';

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
  instance.muya.ui.hideAllFloatTools();
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
  const previousChildren = new Set(document.body.children);
  const muya = instance.muya = new Muya(mount, {
    markdown: rewriteImageUrls(instance.value, instance.imageSources),
    frontMatter: false, math: false, footnote: true, superSubScript: true,
    hideQuickInsertHint: false, fontSize: 18, lineHeight: 1.75
  });
  muya.init();
  instance.tools = bindEditorTools(muya, root, previousChildren, () => instance.disabled || instance.disposed);
  instance.initialNormalized = restoreDisplayUrls(muya.getMarkdown(), instance.originalUrls);
  instance.loading = false;
  muya.on('json-change', () => { instance.searchText = null; void notify(instance); });
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
  on('pointerup', async event => {
    if (event.pointerType !== 'touch' || instance.disabled) return;
    const documentKey = instance.documentKey;
    // Muya updates the clicked selection on the frame after pointerup.
    await settled();
    await settled();
    if (!instance.disposed && !instance.disabled && instance.documentKey === documentKey) showBlockHandle(muya, activeBlock(instance));
  });
  const toolbarKeys = event => {
    if (!event.target.closest('.markazor-markdown-toolbar')) return;
    const menu = event.target.closest('[role=menu]');
    if (menu) {
      const item = event.target.closest('[role=menuitem]');
      if (item?.tagName === 'LABEL' && ['Enter', ' '].includes(event.key)) {
        event.preventDefault();
        item.querySelector('input')?.click();
      } else if (['ArrowDown', 'ArrowUp', 'Home', 'End'].includes(event.key)) {
        event.preventDefault();
        const items = [...menu.querySelectorAll('[role=menuitem]')];
        const index = items.indexOf(item);
        const next = event.key === 'Home' ? 0 : event.key === 'End' ? items.length - 1
          : (index + (event.key === 'ArrowUp' ? -1 : 1) + items.length) % items.length;
        items[next]?.focus();
      }
      return;
    }
    const imageMenu = event.target.closest('.markazor-markdown-image-menu')?.querySelector('[role=menu]');
    if (imageMenu && event.key === 'ArrowDown') {
      event.preventDefault();
      imageMenu.querySelector('[role=menuitem]')?.focus();
      return;
    }
    if (!['ArrowLeft', 'ArrowRight', 'Home', 'End'].includes(event.key)) return;
    const buttons = [...root.querySelectorAll('.markazor-markdown-toolbar button:not(:disabled):not(.markazor-markdown-menu-backdrop)')];
    const index = buttons.indexOf(event.target);
    if (index < 0) return;
    event.preventDefault();
    const next = event.key === 'Home' ? 0 : event.key === 'End' ? buttons.length - 1
      : (index + (event.key === 'ArrowLeft' ? -1 : 1) + buttons.length) % buttons.length;
    buttons[next]?.focus();
  };
  root.addEventListener('keydown', toolbarKeys);
  instance.cleanups.push(() => root.removeEventListener('keydown', toolbarKeys));
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
  if (instance.disabled) {
    instance.tools.disable();
    if (instance.searchText) search(id, '');
  }
}

export async function showInsertMenu(id, reference) {
  const instance = editors.get(id);
  if (!instance || instance.disabled) return;
  // Let the opening click finish before Muya's document click listener runs.
  const documentKey = instance.documentKey;
  await settled();
  if (!instance.disposed && !instance.disabled && instance.documentKey === documentKey && reference.isConnected) {
    openInsertMenu(instance.muya, reference, activeBlock(instance));
  }
}

export function hideTools(id) {
  editors.get(id)?.muya.ui.hideAllFloatTools();
}

export function focus(id) {
  const instance = editors.get(id);
  if (instance && !instance.disabled) focusEditor(instance.muya);
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
  if (command === 'undo' || command === 'redo') { await settled(); muya.ui.hideAllFloatTools(); muya[command](); await notify(instance); return; }
  const block = activeBlock(instance);
  const inline = { bold: 'strong', italic: 'em', code: 'inline_code', link: 'link', image: 'image' }[command];
  if (inline) { block?.format?.(inline); await settled(); await notify(instance); return; }
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
  instance.tools.disable();
  instance.tools.dispose();
  instance.muya.destroy();
  instance.root.classList.remove('is-muya-ready');
  editors.delete(id);
}
