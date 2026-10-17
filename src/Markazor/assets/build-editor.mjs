import { build } from 'esbuild';
import { fileURLToPath } from 'node:url';
import { resolve, dirname } from 'node:path';
import { readFile, writeFile } from 'node:fs/promises';

const project = resolve(dirname(fileURLToPath(import.meta.url)), '..');

// Muya 0.2.0 has no option for disabling diagram parsing. Keep diagram fences
// as code, matching Markdig, and omit engines that can make remote requests.
// These checked transformations deliberately fail if the pinned package changes.
const markazorMarkdown = {
  name: 'markazor-markdown-subset',
  setup(builder) {
    builder.onLoad({ filter: /@muyajs[\\/]core[\\/]lib[\\/]es[\\/]index\.js$/ }, async args => {
      let contents = await readFile(args.path, 'utf8');
      const expression = '/mermaid|vega-lite|plantuml/';
      if (contents.split(expression).length !== 2) throw new Error('Review Muya diagram parsing before updating the editor dependency.');
      contents = contents.replace(expression, '/(?!)/');
      const region = /\/\/#region src\/utils\/diagram\/index\.ts\n[\s\S]*?\/\/#endregion/;
      const original = contents.match(region)?.[0];
      const name = original?.match(/async function (\w+)\(/)?.[1];
      if (!name) throw new Error('Review Muya diagram loading before updating the editor dependency.');
      contents = contents.replace(region, `async function ${name}() { throw new Error('Diagram rendering is not enabled.'); }`);
      const imageRegion = contents.match(/\/\/#region src\/utils\/image\.ts\n[\s\S]*?\/\/#endregion/)?.[0];
      const imageFunctions = [...(imageRegion ?? '').matchAll(/function (\w+)\(\w+\) \{[\s\S]*?\n\}/g)].filter(match => match[0].includes('isUnknownType'));
      if (imageFunctions.length !== 1) throw new Error('Review Muya browser image handling before updating the editor dependency.');
      const image = imageFunctions[0];
      contents = contents.replace(image[0], `function ${image[1]}(value) {
        try {
          if (typeof value !== 'string' || !value.trim()) throw new Error();
          const url = new URL(value, document.baseURI);
          if (url.protocol === 'http:' || url.protocol === 'https:' || (url.protocol === 'blob:' && url.origin === location.origin)) {
            return { isUnknownType: false, src: url.href };
          }
        } catch {}
        return { isUnknownType: false, src: '' };
      }`);
      const inlineRegion = /\/\/#region src\/ui\/inlineFormatToolbar\/config\.ts\n[\s\S]*?(?=\/\/#region src\/ui\/linkTools\/config\.ts)/;
      const inlineSource = contents.match(inlineRegion)?.[0];
      const icons = inlineSource?.match(/"_icons", (\w+)\)/)?.[1];
      const shortcut = 'i && (e.preventDefault(), r.format(i));';
      if (!icons || inlineSource.split(shortcut).length !== 2) throw new Error('Review Muya inline tools before updating the editor dependency.');
      contents = contents.replace(inlineRegion, inlineSource.replace(shortcut, 'i && this._icons.some(item => item.type === i) && (e.preventDefault(), r.format(i));') + `
        ${icons} = ${icons}.filter(item => ['strong', 'em', 'del', 'inline_code', 'link', 'clear'].includes(item.type))
          .map(item => ({ ...item, tooltip: ({ strong: 'Bold', clear: 'Clear formatting' })[item.type] || item.tooltip }));
      `);
      const blocks = contents.match(/(\w+) = \[\n\s*\{\n\s*name: "basic blocks"/)?.[1];
      const menuBoundary = '//#region src/ui/paragraphFrontMenu/config.ts';
      if (!blocks || contents.split(menuBoundary).length !== 2) throw new Error('Review Muya block menus before updating the editor dependency.');
      contents = contents.replace(menuBoundary, `
        const markazorBlocks = ${blocks}.flatMap(group => group.children)
          .filter(item => /^(paragraph|thematic-break|atx-heading [2-6]|table|code-block|block-quote|order-list|bullet-list|task-list)$/.test(item.label))
          .map(item => ({ ...item, title: ({ paragraph: 'Text', 'thematic-break': 'Divider', table: 'Table', 'code-block': 'Code block', 'block-quote': 'Quote', 'order-list': 'Numbered list', 'bullet-list': 'Bulleted list', 'task-list': 'Task list' })[item.label] || item.title.replace('Header', 'Heading') }));
        ${blocks} = [
          { name: 'Text', labels: ['paragraph', ...[2, 3, 4, 5, 6].map(level => 'atx-heading ' + level)] },
          { name: 'Lists', labels: ['bullet-list', 'order-list', 'task-list'] },
          { name: 'Blocks', labels: ['block-quote', 'table', 'code-block', 'thematic-break'] }
        ].map(group => ({ name: group.name, children: group.labels.map(label => markazorBlocks.find(item => item.label === label)) }));
        ${menuBoundary}`);
      const floatRegion = /\/\/#region src\/ui\/baseFloat\/index\.ts\n[\s\S]*?\/\/#endregion/;
      const floatSource = contents.match(floatRegion)?.[0];
      const position = 'left: `${e}px`';
      const verticalPosition = 'top: `${t}px`';
      if (floatSource?.split(position).length !== 2 || floatSource?.split(verticalPosition).length !== 2) throw new Error('Review Muya floating menu positioning before updating the editor dependency.');
      contents = contents.replace(floatRegion, floatSource
        .replace(position, 'left: `${Math.max(window.scrollX + 8, Math.min(e, window.scrollX + window.innerWidth - n.offsetWidth - 8))}px`')
        .replace(verticalPosition, 'top: `${Math.max(window.scrollY + 8, Math.min(t, window.scrollY + window.innerHeight - n.offsetHeight - 8))}px`'));
      const destroy = 'this.eventCenter.detachAllDomEvents(), this.eventCenter.unsubscribeAll(), this.domNode.remove && this.domNode.remove(), this.ui && this.ui.hideAllFloatTools();';
      if (contents.split(destroy).length !== 2) throw new Error('Review Muya plugin cleanup before updating the editor dependency.');
      contents = contents.replace(destroy, `
        this.ui?.hideAllFloatTools();
        for (const plugin of Object.values(this._uiPlugins)) plugin.destroy?.();
        this._uiPlugins = {};
        ${destroy}`);
      const license = await readFile(resolve(project, 'assets/muya-license.txt'), 'utf8');
      return { contents: `/*! @license Muya (MIT)\n${license}\n*/\n${contents}`, loader: 'js' };
    });
  }
};

export async function buildEditorAssets(outdir = resolve(project, '../../artifacts/editor-assets')) {
  const result = await build({
    absWorkingDir: project,
    entryPoints: ['assets/markazor-markdown-editor.js', 'assets/markazor-reader.js'],
    outdir: resolve(outdir),
    bundle: true,
    splitting: true,
    format: 'esm',
    target: 'es2022',
    minify: true,
    chunkNames: 'editor/[name]-[hash]',
    assetNames: 'editor/[name]-[hash]',
    publicPath: '/_content/Markazor',
    loader: { '.png': 'file', '.svg': 'file', '.woff': 'file', '.woff2': 'file', '.ttf': 'file' },
    metafile: true,
    logLevel: 'warning',
    plugins: [markazorMarkdown]
  });
  const files = Object.keys(result.metafile.outputs).map(path => resolve(project, path));
  await writeFile(resolve(outdir, 'markazor-editor-assets.txt'), files.join('\n') + '\n');
  return result;
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const result = await buildEditorAssets(process.argv[2]);
  console.log(`Markazor: bundled ${Object.keys(result.metafile.outputs).length} editor and reader assets.`);
}
