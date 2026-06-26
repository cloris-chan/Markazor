import { defaultKeymap, history, historyKeymap, indentWithTab, redo, undo } from "@codemirror/commands";
import { markdown } from "@codemirror/lang-markdown";
import {
  bracketMatching,
  defaultHighlightStyle,
  foldGutter,
  foldKeymap,
  indentOnInput,
  syntaxHighlighting
} from "@codemirror/language";
import { highlightSelectionMatches, searchKeymap } from "@codemirror/search";
import { Annotation, Compartment, EditorSelection, EditorState } from "@codemirror/state";
import {
  crosshairCursor,
  drawSelection,
  dropCursor,
  EditorView,
  highlightActiveLine,
  highlightActiveLineGutter,
  keymap,
  lineNumbers,
  rectangularSelection
} from "@codemirror/view";

const editors = new Map();
const fromBlazor = Annotation.define();

const markazorTheme = EditorView.theme(
  {
    "&": {
      backgroundColor: "#fffdf8",
      color: "#151719",
      height: "100%"
    },
    "&.cm-focused": {
      outline: "none"
    },
    ".cm-scroller": {
      fontFamily: 'ui-monospace, SFMono-Regular, Consolas, "Liberation Mono", monospace'
    },
    ".cm-content": {
      caretColor: "#0aa882",
      fontSize: "0.95rem",
      lineHeight: "1.55",
      minHeight: "28rem",
      padding: "0.75rem 0"
    },
    ".cm-line": {
      padding: "0 0.75rem"
    },
    ".cm-gutters": {
      backgroundColor: "#eef3ef",
      borderRight: "1px solid rgba(21, 23, 25, 0.14)",
      color: "#6d777a"
    },
    ".cm-activeLine": {
      backgroundColor: "rgba(10, 168, 130, 0.08)"
    },
    ".cm-activeLineGutter": {
      backgroundColor: "rgba(10, 168, 130, 0.12)",
      color: "#151719"
    },
    ".cm-selectionBackground, &.cm-focused .cm-selectionBackground": {
      backgroundColor: "rgba(10, 168, 130, 0.24)"
    },
    ".cm-cursor": {
      borderLeftColor: "#0aa882"
    },
    ".cm-matchingBracket, .cm-nonmatchingBracket": {
      backgroundColor: "rgba(56, 103, 214, 0.13)",
      outline: "1px solid rgba(56, 103, 214, 0.45)"
    },
    ".cm-foldGutter span": {
      cursor: "pointer"
    },
    ".cm-searchMatch": {
      backgroundColor: "rgba(217, 131, 36, 0.22)",
      outline: "1px solid rgba(217, 131, 36, 0.5)"
    },
    ".cm-searchMatch.cm-searchMatch-selected": {
      backgroundColor: "rgba(227, 82, 103, 0.22)"
    }
  },
  { dark: false }
);

const markazorHighlightStyle = syntaxHighlighting(defaultHighlightStyle, { fallback: true });

export function initialize(id, root, host, dotNetReference, value, disabled) {
  dispose(id);

  const editableCompartment = new Compartment();
  const readOnlyCompartment = new Compartment();
  const initialValue = value ?? "";
  const isDisabled = disabled === true;

  const instance = {
    dotNetReference,
    editableCompartment,
    readOnlyCompartment,
    value: initialValue,
    view: null
  };

  const extensions = [
    lineNumbers(),
    foldGutter(),
    history(),
    drawSelection(),
    dropCursor(),
    rectangularSelection(),
    crosshairCursor(),
    highlightActiveLine(),
    highlightActiveLineGutter(),
    highlightSelectionMatches(),
    indentOnInput(),
    bracketMatching(),
    markdown(),
    markazorTheme,
    markazorHighlightStyle,
    EditorView.lineWrapping,
    editableCompartment.of(EditorView.editable.of(!isDisabled)),
    readOnlyCompartment.of(EditorState.readOnly.of(isDisabled)),
    EditorView.updateListener.of(update => {
      if (!update.docChanged || update.transactions.some(transaction => transaction.annotation(fromBlazor))) {
        return;
      }

      const nextValue = update.state.doc.toString();
      instance.value = nextValue;
      dotNetReference.invokeMethodAsync("NotifyEditorChanged", nextValue);
    }),
    keymap.of([
      {
        key: "Mod-s",
        preventDefault: true,
        run: () => {
          dotNetReference.invokeMethodAsync("NotifySaveRequested");
          return true;
        }
      },
      {
        key: "Mod-b",
        preventDefault: true,
        run: view => formatSelection(view, "bold")
      },
      {
        key: "Mod-i",
        preventDefault: true,
        run: view => formatSelection(view, "italic")
      },
      indentWithTab,
      ...defaultKeymap,
      ...historyKeymap,
      ...foldKeymap,
      ...searchKeymap
    ])
  ];

  const state = EditorState.create({
    doc: initialValue,
    extensions
  });

  instance.view = new EditorView({
    state,
    parent: host
  });

  editors.set(id, instance);
  root.classList.add("is-codemirror-ready");
}

export function setValue(id, value) {
  const instance = editors.get(id);
  if (!instance?.view) {
    return;
  }

  const nextValue = value ?? "";
  const currentValue = instance.view.state.doc.toString();
  if (currentValue === nextValue) {
    instance.value = nextValue;
    return;
  }

  instance.value = nextValue;
  instance.view.dispatch({
    changes: {
      from: 0,
      to: currentValue.length,
      insert: nextValue
    },
    annotations: fromBlazor.of(true)
  });
}

export function setDisabled(id, disabled) {
  const instance = editors.get(id);
  if (!instance?.view) {
    return;
  }

  const nextDisabled = disabled === true;
  instance.view.dispatch({
    effects: [
      instance.editableCompartment.reconfigure(EditorView.editable.of(!nextDisabled)),
      instance.readOnlyCompartment.reconfigure(EditorState.readOnly.of(nextDisabled))
    ],
    annotations: fromBlazor.of(true)
  });
}

export function focus(id) {
  const instance = editors.get(id);
  instance?.view?.focus();
}

export function format(id, command) {
  const instance = editors.get(id);
  if (!instance?.view) {
    return;
  }

  formatSelection(instance.view, command);
}

export function insertText(id, text) {
  const instance = editors.get(id);
  if (!instance?.view || instance.view.state.readOnly) {
    return;
  }

  insertInline(instance.view, text ?? "");
}

export function dispose(id) {
  const instance = editors.get(id);
  if (!instance) {
    return;
  }

  instance.view?.destroy();
  editors.delete(id);
}

function formatSelection(view, command) {
  if (view.state.readOnly) {
    return true;
  }

  switch (command) {
    case "undo":
      return undo(view);
    case "redo":
      return redo(view);
    case "bold":
      return wrapSelection(view, "**", "**", "strong text");
    case "italic":
      return wrapSelection(view, "*", "*", "emphasis");
    case "code":
      return wrapSelection(view, "`", "`", "code");
    case "link":
      return wrapSelection(view, "[", "](https://)", "link text");
    case "image":
      return wrapSelection(view, "![", "](https://)", "alt text");
    case "heading":
    case "heading2":
      return setHeadingLevel(view, 2);
    case "heading1":
      return setHeadingLevel(view, 1);
    case "heading3":
      return setHeadingLevel(view, 3);
    case "quote":
      return toggleLinePrefix(view, "> ");
    case "list":
    case "unordered-list":
      return toggleLinePrefix(view, "- ");
    case "ordered-list":
      return toggleOrderedList(view);
    case "code-block":
      return wrapBlockSelection(view, "```\n", "\n```", "code");
    case "table":
      return insertBlock(view, "| Header | Header |\n| --- | --- |\n| Value | Value |");
    case "divider":
      return insertBlock(view, "---");
    default:
      return false;
  }
}

function wrapSelection(view, before, after, placeholder) {
  const transaction = view.state.changeByRange(range => {
    const selectedText = view.state.sliceDoc(range.from, range.to) || placeholder;
    const insert = before + selectedText + after;
    const selectionFrom = range.from + before.length;
    const selectionTo = selectionFrom + selectedText.length;

    return {
      changes: {
        from: range.from,
        to: range.to,
        insert
      },
      range: EditorSelection.range(selectionFrom, selectionTo)
    };
  });

  view.dispatch({
    ...transaction,
    scrollIntoView: true
  });
  view.focus();

  return true;
}

function setHeadingLevel(view, level) {
  const headingPrefix = "#".repeat(level) + " ";
  const transaction = view.state.changeByRange(range => {
    const lines = getSelectedLines(view.state, range);
    const shouldRemove = lines.every(line => {
      const text = view.state.sliceDoc(line.from, line.to);
      return text.length === 0 || text.startsWith(headingPrefix);
    });
    const changes = [];

    for (const line of lines) {
      const text = view.state.sliceDoc(line.from, line.to);
      if (shouldRemove) {
        if (text.startsWith(headingPrefix)) {
          changes.push({
            from: line.from,
            to: line.from + headingPrefix.length,
            insert: ""
          });
        }

        continue;
      }

      const existingHeading = /^(#{1,6}\s+)/.exec(text);
      changes.push({
        from: line.from,
        to: existingHeading ? line.from + existingHeading[0].length : line.from,
        insert: headingPrefix
      });
    }

    return withMappedSelection(view.state, changes, range);
  });

  view.dispatch({
    ...transaction,
    scrollIntoView: true
  });
  view.focus();

  return true;
}

function toggleLinePrefix(view, prefix) {
  const transaction = view.state.changeByRange(range => {
    const lines = getSelectedLines(view.state, range);
    const shouldRemove = lines.every(line => {
      const text = view.state.sliceDoc(line.from, line.to);
      return text.length === 0 || text.startsWith(prefix);
    });
    const changes = [];

    for (const line of lines) {
      const text = view.state.sliceDoc(line.from, line.to);
      if (shouldRemove) {
        if (text.startsWith(prefix)) {
          changes.push({
            from: line.from,
            to: line.from + prefix.length,
            insert: ""
          });
        }
      } else {
        changes.push({
          from: line.from,
          insert: prefix
        });
      }
    }

    return withMappedSelection(view.state, changes, range);
  });

  view.dispatch({
    ...transaction,
    scrollIntoView: true
  });
  view.focus();

  return true;
}

function toggleOrderedList(view) {
  const transaction = view.state.changeByRange(range => {
    const lines = getSelectedLines(view.state, range);
    const shouldRemove = lines.every(line => {
      const text = view.state.sliceDoc(line.from, line.to);
      return text.length === 0 || /^\d+\.\s+/.test(text);
    });
    const changes = [];
    let index = 1;

    for (const line of lines) {
      const text = view.state.sliceDoc(line.from, line.to);
      if (shouldRemove) {
        const existingMarker = /^(\d+\.\s+)/.exec(text);
        if (existingMarker) {
          changes.push({
            from: line.from,
            to: line.from + existingMarker[0].length,
            insert: ""
          });
        }
      } else {
        const existingMarker = /^(\d+\.\s+)/.exec(text);
        changes.push({
          from: line.from,
          to: existingMarker ? line.from + existingMarker[0].length : line.from,
          insert: `${index}. `
        });
      }

      index += 1;
    }

    return withMappedSelection(view.state, changes, range);
  });

  view.dispatch({
    ...transaction,
    scrollIntoView: true
  });
  view.focus();

  return true;
}

function wrapBlockSelection(view, before, after, placeholder) {
  const transaction = view.state.changeByRange(range => {
    const selectedText = view.state.sliceDoc(range.from, range.to) || placeholder;
    const insert = before + selectedText + after;
    const selectionFrom = range.from + before.length;
    const selectionTo = selectionFrom + selectedText.length;

    return {
      changes: {
        from: range.from,
        to: range.to,
        insert
      },
      range: EditorSelection.range(selectionFrom, selectionTo)
    };
  });

  view.dispatch({
    ...transaction,
    scrollIntoView: true
  });
  view.focus();

  return true;
}

function insertBlock(view, text) {
  const transaction = view.state.changeByRange(range => {
    const needsLeadingBreak = range.from > 0 && view.state.sliceDoc(range.from - 1, range.from) !== "\n";
    const needsTrailingBreak = range.to < view.state.doc.length && view.state.sliceDoc(range.to, range.to + 1) !== "\n";
    const before = needsLeadingBreak ? "\n\n" : "";
    const after = needsTrailingBreak ? "\n\n" : "\n";
    const insert = before + text + after;
    const cursor = range.from + insert.length;

    return {
      changes: {
        from: range.from,
        to: range.to,
        insert
      },
      range: EditorSelection.cursor(cursor)
    };
  });

  view.dispatch({
    ...transaction,
    scrollIntoView: true
  });
  view.focus();

  return true;
}

function insertInline(view, text) {
  const transaction = view.state.changeByRange(range => {
    const insert = text ?? "";
    const cursor = range.from + insert.length;

    return {
      changes: {
        from: range.from,
        to: range.to,
        insert
      },
      range: EditorSelection.cursor(cursor)
    };
  });

  view.dispatch({
    ...transaction,
    scrollIntoView: true
  });
  view.focus();
}

function getSelectedLines(state, range) {
  const document = state.doc;
  const startLine = document.lineAt(range.from);
  const endPosition = range.to > range.from ? range.to - 1 : range.to;
  const endLine = document.lineAt(endPosition);
  const lines = [];

  for (let lineNumber = startLine.number; lineNumber <= endLine.number; lineNumber += 1) {
    lines.push(document.line(lineNumber));
  }

  return lines;
}

function withMappedSelection(state, changes, range) {
  const changeSet = state.changes(changes);

  return {
    changes,
    range: EditorSelection.range(
      changeSet.mapPos(range.from, 1),
      changeSet.mapPos(range.to, -1))
  };
}
