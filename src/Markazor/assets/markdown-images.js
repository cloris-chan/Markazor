import { fromMarkdown } from 'mdast-util-from-markdown';
import { toMarkdown } from 'mdast-util-to-markdown';
import { gfmFromMarkdown, gfmToMarkdown } from 'mdast-util-gfm';
import { gfm } from 'micromark-extension-gfm';

function visit(node, action) {
  action(node);
  node.children?.forEach(child => visit(child, action));
}

// Rewrite only image destinations. Code samples, prose, and ordinary links keep
// their original bytes; display-only blob URLs never become repository paths.
export function rewriteImageUrls(markdown, replacements) {
  const mapping = replacements instanceof Map ? replacements : new Map(Object.entries(replacements ?? {}));
  if (!mapping.size || !markdown) return markdown;
  const tree = fromMarkdown(markdown, { extensions: [gfm()], mdastExtensions: [gfmFromMarkdown()] });
  const references = new Set();
  visit(tree, node => { if (node.type === 'imageReference') references.add(node.identifier); });
  const edits = [];
  visit(tree, node => {
    let value;
    if (node.type === 'image' || (node.type === 'definition' && references.has(node.identifier))) {
      const url = mapping.get(node.url);
      if (url && url !== node.url) value = toMarkdown({ ...node, url }, { extensions: [gfmToMarkdown()] }).trimEnd();
    } else if (node.type === 'html') {
      value = node.value.replace(/<img\b(?:"[^"]*"|'[^']*'|[^'">])*>/gi, tag =>
        tag.replace(/(\bsrc\s*=\s*)(?:(["'])(.*?)\2|([^\s>]+))/i, (attribute, prefix, quote, quoted, bare) => {
          const url = mapping.get((quoted ?? bare).replace(/&amp;/g, '&'));
          return url ? `${prefix}${quote || '"'}${url.replace(/&/g, '&amp;').replace(/"/g, '&quot;')}${quote || '"'}` : attribute;
        }));
      if (value === node.value) value = undefined;
    }
    if (value !== undefined) edits.push({ start: node.position.start.offset, end: node.position.end.offset, value });
  });
  edits.sort((a, b) => b.start - a.start);
  for (const edit of edits) markdown = markdown.slice(0, edit.start) + edit.value + markdown.slice(edit.end);
  return markdown;
}

export function restoreDisplayUrls(markdown, replacements) {
  const other = new Map();
  for (const [url, path] of replacements) {
    // Object URLs are generated only for this editor session. Restore even a
    // copied occurrence, without parsing the whole article on every keystroke.
    if (url.startsWith('blob:')) markdown = markdown.split(url).join(path);
    else other.set(url, path);
  }
  return other.size ? rewriteImageUrls(markdown, other) : markdown;
}
