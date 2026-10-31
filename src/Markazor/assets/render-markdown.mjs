import { renderToStaticHTML, loadCodeLanguages } from '@muyajs/core';
import profile from './markdown-profile.json';

async function main() {
  let input = '';
  process.stdin.setEncoding('utf8');
  for await (const chunk of process.stdin) input += chunk;
  const documents = JSON.parse(input);
  const languages = new Set(documents.flatMap(markdown => [...markdown.matchAll(/(?:`{3,}|~{3,})[ \t]*([\w#+-]+)/g)].map(match => match[1])));
  if (languages.size) await loadCodeLanguages([...languages]);
  process.stdout.write(JSON.stringify(documents.map(markdown => renderToStaticHTML(markdown, { ...profile.markdown, sanitize: false }))));
}

main().catch(error => { console.error(error); process.exitCode = 1; });
