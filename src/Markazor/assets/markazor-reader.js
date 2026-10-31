import { waitForUpdate, activateUpdate } from "../wwwroot/markazor-pwa.js";

const codeBlocks = new WeakSet();
const observedHeadings = new WeakSet();
const menuLabels = new WeakMap();
const sectionObserver = new IntersectionObserver(entries => {
  const visible = entries.filter(entry => entry.isIntersecting).sort((a, b) => a.boundingClientRect.top - b.boundingClientRect.top);
  if (!visible.length) return;
  const href = "#" + encodeURIComponent(visible[0].target.id);
  for (const link of document.querySelectorAll(".markazor-contents a")) {
    if (link.hash === href) link.setAttribute("aria-current", "location");
    else link.removeAttribute("aria-current");
  }
}, { rootMargin: "-5% 0px -65% 0px" });

function enhance(root = document) {
  for (const code of root.querySelectorAll(".markazor-prose pre > code")) {
    if (codeBlocks.has(code)) continue;
    codeBlocks.add(code);
    const pre = code.parentElement;
    const button = document.createElement("button");
    button.type = "button";
    button.className = "markazor-code-copy";
    button.dataset.mkCopy = "";
    button.textContent = "Copy";
    button.setAttribute("aria-label", "Copy code");
    button.setAttribute("aria-live", "polite");
    pre.append(button);
  }
  for (const heading of root.querySelectorAll(".markazor-prose h2[id], .markazor-prose h3[id]")) {
    if (observedHeadings.has(heading)) continue;
    observedHeadings.add(heading);
    sectionObserver.observe(heading);
  }
}

document.addEventListener("click", async event => {
  if (event.defaultPrevented || event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return;
  const button = event.target.closest?.("button[data-mk-copy]");
  if (button) {
    try {
      await navigator.clipboard.writeText(button.parentElement.querySelector("code").textContent);
      button.textContent = "Copied";
    } catch {
      button.textContent = "Select text to copy";
    }
    return;
  }
  const toggle = document.documentElement.hasAttribute("data-markazor-static") && event.target.closest?.(".site-menu-toggle");
  if (toggle) {
    if (!menuLabels.has(toggle)) menuLabels.set(toggle, toggle.firstChild?.textContent || "Menu ");
    const header = toggle.closest(".site-header");
    const open = header.classList.toggle("is-menu-open");
    toggle.setAttribute("aria-expanded", String(open));
    if (toggle.firstChild?.nodeType === Node.TEXT_NODE) toggle.firstChild.textContent = open ? "Close " : menuLabels.get(toggle);
  }
  const fragmentLink = event.target.closest?.("a[href]");
  if (fragmentLink?.hash && !fragmentLink.hasAttribute("download") && (!fragmentLink.target || fragmentLink.target === "_self")
    && (fragmentLink.getAttribute("href").startsWith("#")
    || (fragmentLink.origin === location.origin && fragmentLink.pathname.replace(/\/$/, "") === location.pathname.replace(/\/$/, "")))) {
    let id;
    try { id = decodeURIComponent(fragmentLink.hash.slice(1)); } catch { return; }
    const target = document.getElementById(id);
    if (target) {
      event.preventDefault();
      target.setAttribute("tabindex", "-1");
      target.focus({ preventScroll: true });
      target.scrollIntoView({ block: "start" });
      history.pushState(null, "", location.pathname + location.search + fragmentLink.hash);
    }
  }
});
document.addEventListener("keydown", event => {
  if (event.key !== "Escape" || !document.documentElement.hasAttribute("data-markazor-static")) return;
  const header = document.querySelector(".site-header.is-menu-open");
  if (header) {
    header.classList.remove("is-menu-open");
    const toggle = header.querySelector(".site-menu-toggle");
    toggle.setAttribute("aria-expanded", "false");
    if (toggle.firstChild?.nodeType === Node.TEXT_NODE) toggle.firstChild.textContent = menuLabels.get(toggle) || "Menu ";
    toggle.focus();
  }
});

let scheduled = false;
const mutationObserver = new MutationObserver(records => {
  for (const record of records) {
    for (const removed of record.removedNodes) {
      if (removed.nodeType !== Node.ELEMENT_NODE) continue;
      for (const element of [removed, ...removed.querySelectorAll("h2[id], h3[id]")]) {
        if (!element.isConnected) {
          sectionObserver.unobserve(element);
        }
      }
    }
  }
  if (scheduled) return;
  scheduled = true;
  queueMicrotask(() => { scheduled = false; enhance(); });
});
enhance();
mutationObserver.observe(document.body, { childList: true, subtree: true });

if (document.documentElement.hasAttribute("data-markazor-static")) {
  document.documentElement.classList.add("markazor-enhanced");
  if (matchMedia("(max-width: 760px)").matches) {
    document.querySelectorAll(".markazor-contents details").forEach(details => { details.open = false; });
  }
  void waitForUpdate().then(available => {
    if (!available) return;
    const prompt = document.createElement("aside");
    prompt.className = "markazor-update-prompt";
    prompt.setAttribute("role", "status");
    const text = document.createElement("strong");
    text.textContent = "A new version is ready";
    const reload = document.createElement("button");
    reload.type = "button";
    reload.textContent = "Reload";
    reload.addEventListener("click", () => { reload.disabled = true; void activateUpdate(); });
    prompt.append(text, reload);
    document.body.append(prompt);
  }).catch(() => {});
}
