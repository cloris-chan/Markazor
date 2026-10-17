import { Muya, InlineFormatToolbar, ParagraphFrontButton, ParagraphFrontMenu, ParagraphQuickInsertMenu } from '@muyajs/core';

const insertMenus = new WeakMap();
const blockHandles = new WeakMap();

function revealMenuItem(item) {
  const menu = item?.closest('.mu-quick-insert');
  if (!menu) return;
  const rect = item.getBoundingClientRect();
  const top = rect.top - menu.getBoundingClientRect().top + menu.scrollTop;
  if (top < menu.scrollTop) menu.scrollTop = top;
  else if (top + rect.height > menu.scrollTop + menu.clientHeight) menu.scrollTop = top + rect.height - menu.clientHeight;
}

class BlockHandle extends ParagraphFrontButton {
  static pluginName = 'blockHandle';
  constructor(muya, options) {
    super(muya, options);
    blockHandles.set(muya, this);
  }
}

// Use Muya's block definitions for both slash commands and the Insert button.
// Opening a menu must not change the document; create its target only on choice.
class InsertMenu extends ParagraphQuickInsertMenu {
  constructor(muya) {
    super(muya);
    insertMenus.set(muya, this);
  }

  listen() {
    super.listen();
    const resize = () => { if (this.status && this.reference) this.sizeToReference(this.reference); };
    this.muya.eventCenter.attachDOMEvent(window, 'resize', resize);
    this.muya.eventCenter.attachDOMEvent(window, 'scroll', resize, true);
  }

  sizeToReference(reference) {
    const rect = reference.getBoundingClientRect();
    const available = Math.max(1, rect.top - 18, window.innerHeight - rect.bottom - 18);
    this.container.style.maxHeight = `min(30rem, 65vh, ${available}px)`;
  }

  show(reference, callback) {
    // Leave room for the trigger when neither side can fit the full menu.
    this.sizeToReference(reference);
    super.show(reference, callback);
  }

  hide() {
    super.hide();
    this.insertAfter = null;
  }

  selectItem(item) {
    if (!item || !this.renderArray.includes(item) || this.container.closest('[inert]')) return;
    if (this.insertAfter) {
      const anchor = this.insertAfter;
      this.insertAfter = null;
      if (anchor.blockName === 'paragraph' && !anchor.firstContentInDescendant().text.trim()) {
        this.block = anchor.firstContentInDescendant();
      } else {
        const paragraph = this.muya.editor.scrollPage.constructor.loadBlock('paragraph')
          .create(this.muya, { name: 'paragraph', text: '' });
        anchor.parent.insertAfter(paragraph, anchor);
        this.block = paragraph.firstContentInDescendant();
      }
    }
    super.selectItem(item);
  }

  step(direction) {
    if (this.renderArray.length) super.step(direction);
  }

  activeEleScrollIntoView(item) {
    // Native scrollIntoView also scrolls Studio while the popup is offscreen.
    // Keep command navigation inside the menu, including during initialization.
    revealMenuItem(item);
  }
}

class BlockMenu extends ParagraphFrontMenu {
  listen() {
    super.listen();
    // Keep a clicked menu open until a choice, outside click, or Escape. Touch
    // scrolling can emit mouseleave while the user is reaching an action.
    this.muya.eventCenter.attachDOMEvent(this.container, 'mouseleave', event => event.stopImmediatePropagation(), true);
  }

  selectItem(event, item) {
    if (this.container.closest('[inert]')) return;
    super.selectItem(event, item);
    this.hide();
    // A conversion replaces the hovered block. Do not keep its old handle live.
    for (const button of this.muya.ui.shownButton) button.hide();
  }
}

for (const plugin of [InlineFormatToolbar, BlockHandle, BlockMenu, InsertMenu]) Muya.use(plugin);

export function showBlockHandle(muya, block) {
  const handle = blockHandles.get(muya);
  const anchor = block?.getAnchor();
  if (handle && anchor) { handle.show(anchor); handle.render(); }
}

export function focusEditor(muya) {
  const selection = muya.editor.selection;
  muya.domNode.focus({ preventScroll: true });
  if (selection.anchor && selection.focus && selection.anchorBlock?.domNode.isConnected && selection.focusBlock?.domNode.isConnected) {
    const { anchor, focus, anchorBlock, focusBlock, anchorPath, focusPath } = selection;
    selection.setSelection({ anchor, focus, anchorBlock, focusBlock, anchorPath, focusPath });
  } else {
    const block = muya.editor.scrollPage.lastContentInDescendant();
    block?.setCursor(block.text.length, block.text.length, true);
  }
}

export function openInsertMenu(muya, reference, block) {
  const menu = insertMenus.get(muya);
  if (!menu || !block) return;
  if (menu.status && menu.reference === reference) { menu.hide(); return; }
  muya.ui.hideAllFloatTools();
  menu.block = block;
  menu.insertAfter = block.getAnchor();
  menu.search('');
  menu.show(reference);
  menu.getItemElement(menu.activeItem)?.focus({ preventScroll: true });
}

export function bindEditorTools(muya, root, previousChildren, isDisabled) {
  const wrappers = [...document.body.children].filter(element => !previousChildren.has(element)
    && element.matches('.mu-float-wrapper, .mu-front-button-wrapper'));
  const labels = { strong: 'Bold', em: 'Italic', del: 'Strikethrough', inline_code: 'Inline code', link: 'Link', clear: 'Clear formatting' };
  const cleanups = [];
  const enhance = wrapper => {
    const menu = wrapper.querySelector('.mu-quick-insert, .mu-front-menu');
    const toolbar = wrapper.querySelector('.mu-format-picker');
    if (menu) {
      menu.setAttribute('role', 'menu');
      menu.setAttribute('aria-label', menu.matches('.mu-quick-insert') ? 'Insert block' : 'Block actions');
    }
    if (toolbar) { toolbar.setAttribute('role', 'toolbar'); toolbar.setAttribute('aria-label', 'Text formatting'); }
    for (const item of wrapper.querySelectorAll('.item, .turn-into-item, .mu-front-button')) {
      item.setAttribute('role', menu ? 'menuitem' : 'button');
      item.tabIndex = 0;
      let label = item.querySelector('.big-title, .text')?.textContent;
      if (!label && item.matches('.mu-front-button')) label = 'Block actions';
      if (!label) label = Object.entries(labels).find(([type]) => item.classList.contains(type))?.[1];
      if (!label) label = item.querySelector('[title]')?.title.split('\n')[0] || item.title.split('\n')[0];
      if (label) item.setAttribute('aria-label', label);
      if (item.closest('.mu-format-picker')) item.setAttribute('aria-pressed', String(item.classList.contains('active')));
      if (menu) item.title = label;
      if (item.matches('.mu-front-button')) item.setAttribute('aria-haspopup', 'menu');
    }
  };
  for (const wrapper of wrappers) {
    wrapper.classList.add('markazor-muya-popover');
    wrapper.inert = true;
    const colors = getComputedStyle(root);
    for (const name of ['--mk-surface', '--mk-soft', '--mk-ink', '--mk-muted', '--mk-line', '--mk-accent', '--mk-accent-soft', '--mk-shadow']) {
      wrapper.style.setProperty(name, colors.getPropertyValue(name));
    }
    const observer = new MutationObserver(() => enhance(wrapper));
    observer.observe(wrapper, { childList: true, attributes: true, attributeFilter: ['class'], subtree: true });
    enhance(wrapper);
    const down = event => { if (!event.target.closest('input, select, textarea')) event.preventDefault(); };
    const keyboard = event => {
      if (event.target.closest('input, select, textarea')) return;
      const items = [...wrapper.querySelectorAll('[role=button], [role=menuitem]')];
      const index = items.indexOf(document.activeElement);
      if (event.key === 'Escape') {
        event.preventDefault(); event.stopPropagation();
        muya.ui.hideAllFloatTools();
        focusEditor(muya);
      } else if (event.key === 'Enter' || event.key === ' ') {
        if (index < 0) return;
        event.preventDefault(); event.stopPropagation();
        items[index].click();
      } else if (['ArrowDown', 'ArrowUp', 'ArrowLeft', 'ArrowRight', 'Home', 'End'].includes(event.key)) {
        event.preventDefault(); event.stopPropagation();
        const next = event.key === 'Home' ? 0 : event.key === 'End' ? items.length - 1
          : (index + (['ArrowUp', 'ArrowLeft'].includes(event.key) ? -1 : 1) + items.length) % items.length;
        items[next]?.focus({ preventScroll: true });
        revealMenuItem(items[next]);
      }
    };
    wrapper.addEventListener('mousedown', down);
    wrapper.addEventListener('keydown', keyboard);
    cleanups.push(() => { observer.disconnect(); wrapper.removeEventListener('mousedown', down); wrapper.removeEventListener('keydown', keyboard); });
  }
  const changed = (tool, visible) => {
    const wrapper = wrappers.find(element => element.querySelector(`.${tool.name}`));
    if (!wrapper) return;
    wrapper.inert = !visible || isDisabled();
    if (visible && isDisabled()) { tool.hide(); return; }
    if (visible && tool.name === 'mu-format-picker') {
      for (const button of muya.ui.shownButton) if (button.name === 'mu-front-button') button.hide();
    }
    if (visible && tool.name === 'mu-front-button' && [...muya.ui.shownFloat].some(float => float.name === 'mu-format-picker')) tool.hide();
    if (tool.name === 'mu-quick-insert') root.querySelector('[data-insert-block]')?.setAttribute('aria-expanded', String(visible));
    enhance(wrapper);
  };
  muya.on('muya-float', changed);
  muya.on('muya-float-button', changed);
  muya.on('muya-front-menu', ({ reference, block }) => {
    if (!reference || !block) return;
    // The hover handle disappears when the pointer enters its menu. Anchor to
    // the block instead so hiding the handle cannot move the open menu away.
    const rect = reference.getBoundingClientRect();
    const origin = block.domNode.getBoundingClientRect();
    const left = rect.left - origin.left;
    const top = rect.top - origin.top;
    reference.contextElement = block.domNode;
    reference.getBoundingClientRect = () => {
      const current = block.domNode.getBoundingClientRect();
      return new DOMRect(current.left + left, current.top + top, rect.width, rect.height);
    };
  });
  return {
    disable() { muya.ui.hideAllFloatTools(); wrappers.forEach(wrapper => { wrapper.inert = true; }); },
    dispose() { cleanups.forEach(cleanup => cleanup()); insertMenus.delete(muya); blockHandles.delete(muya); }
  };
}
