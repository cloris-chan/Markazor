---
slug: the-shape-of-a-good-page
title: The shape of a good page
summary: A reading specimen: quiet typography, working footnotes, and room for words, pictures, and code.
publishedAt: 2026-09-21T00:00:00Z
tags: [design, markdown, typography]
category: Fieldnotes
---

A good page gives each idea enough room to breathe. This article is a living specimen for Markazor's reading experience: a place to inspect the small details that disappear when everything works well.

The title belongs to the document's metadata. The writing begins here, with a paragraph, and each new section begins with a second-level heading.

## A rhythm for reading

Long-form writing asks for a different pace from a dashboard. Paragraphs need a comfortable measure, headings need a recognizable hierarchy, and links need to be visible without pulling the whole page out of balance.

> Good typography does not need to announce itself. It creates a clear path through the words, then steps aside.

The contents panel follows the article's H2 and H3 headings. Every heading also has a permanent link, available on hover or from the keyboard. These are ordinary fragment links, so navigation still works without a client-side application.

### Details within a paragraph

Emphasis, links, and inline code each need a distinct voice within the same paragraph. Their spacing should follow the surrounding text, while longer tokens need enough room to wrap without disturbing the reading column.

An inline identifier such as `MarkdownRenderResult` should remain distinct from prose. A long address should wrap inside the reading column: <https://example.test/a-long-address/with-many-segments/and-an-explanation-of-how-static-markdown-pages-are-published>.

### A small checklist

- [x] Give the page one article title.
- [x] Keep links and footnotes connected.
- [x] Use the same reading styles in the Studio preview.
- [ ] Write the next piece.

Nested thoughts have their own place:

1. Read the opening paragraph.
2. Follow the structure.
   - A section introduces an idea.
   - A subsection develops one part of it.
3. Return to the text with the keyboard, a pointer, or a touch screen.

## Code, with context

The language label comes from the fenced code block. Known languages receive a restrained highlight palette. The copy control copies the original code, and an unfamiliar language remains readable as plain text.

```csharp
public sealed record ReadingNote(string Title, string Body);

var note = new ReadingNote(
    "A small observation",
    "A public article should be readable before an application starts.");

Console.WriteLine(note.Title);
```

```json
{
  "site": {
    "name": "A personal journal",
    "baseUrls": ["https://example.test"]
  }
}
```

The next line deliberately exceeds a narrow viewport. The code block scrolls inside the page rather than making the entire document wider.

```text
article-title | publication-date | collection | language | permanent-address | reading-status | revision | note: a deliberately long line for checking horizontal scrolling
```

## Tables that keep their shape

Alignment belongs to the content: labels stay on the left, measurements on the right. Wide tables have their own scrolling region, with a hint when more columns are out of view.

| Collection | Articles | Notes | Drafts | Last reviewed | Reading format | Delivery | Owner |
| :--- | ---: | ---: | ---: | :--- | :--- | :--- | :--- |
| Journal | 12 | 8 | 3 | September 21, 2026 | Long-form and short-form | Static HTML | Author |
| Workbench | 7 | 15 | 2 | September 20, 2026 | Technical notes and examples | Static HTML | Author |
| Field studies | 4 | 6 | 1 | September 19, 2026 | Observations and references | Static HTML | Author |

These rows are specimen data for checking the layout, not statistics about this website.

## A place for pictures

![Markazor's open-book mark](/assets/site-icon.png "The shared book and bookmark mark, shown at a reserved size."){width=192 height=192}

Image dimensions reserve space before the file arrives. The first image loads eagerly; later images can load as they approach the viewport. An image title becomes a visible caption when the image occupies its own paragraph.

![A second view of the book mark](/assets/site-icon.png "A second image demonstrates deferred loading."){width=96 height=96}

## References that lead somewhere

A reference should lead to an actual note, and the note should offer a way back.[^reading] Repeated references remain connected to their source as well.[^reading]

<details>
<summary>A note about this specimen</summary>

This page deliberately combines different Markdown elements to exercise the reading layout. It is also included in the build-time static output, so its text, headings, references, and metadata can be inspected directly in the initial HTML response.

</details>

---

The useful test is simple: open the page, follow a footnote, copy an example, and keep reading. The interface should help with each step and leave the words in charge.

[^reading]: This is a demonstration footnote. Its return links point to the references in the article above.
