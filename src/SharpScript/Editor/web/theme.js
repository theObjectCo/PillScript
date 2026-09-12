// Dark Modern, the theme Visual Studio Code ships as its dark default, transcribed for Monaco.
// Workbench colours live in style.css; this file covers the editor surface and the token colours.

window.defineDarkModern = function (monaco) {
  monaco.editor.defineTheme('dark-modern', {
    base: 'vs-dark',
    inherit: true,
    rules: [
      { token: '', foreground: 'cccccc', background: '1f1f1f' },

      { token: 'comment', foreground: '6a9955' },
      { token: 'comment.doc', foreground: '6a9955' },

      { token: 'string', foreground: 'ce9178' },
      { token: 'string.quote', foreground: 'ce9178' },
      { token: 'string.escape', foreground: 'd7ba7d' },
      { token: 'string.escape.invalid', foreground: 'f44747' },
      { token: 'string.invalid', foreground: 'f44747' },

      { token: 'number', foreground: 'b5cea8' },
      { token: 'number.float', foreground: 'b5cea8' },
      { token: 'number.hex', foreground: 'b5cea8' },
      { token: 'constant', foreground: '4fc1ff' },

      { token: 'keyword', foreground: '569cd6' },
      { token: 'keyword.control', foreground: 'c586c0' },
      { token: 'keyword.directive', foreground: 'c586c0' },
      { token: 'keyword.operator', foreground: 'd4d4d4' },

      { token: 'identifier', foreground: '9cdcfe' },
      { token: 'type.identifier', foreground: '4ec9b0' },
      { token: 'namespace', foreground: '4ec9b0' },
      { token: 'annotation', foreground: '4ec9b0' },
      { token: 'method', foreground: 'dcdcaa' },

      { token: 'operator', foreground: 'd4d4d4' },
      { token: 'delimiter', foreground: 'cccccc' },
      { token: 'delimiter.angle', foreground: 'cccccc' },
      { token: 'delimiter.bracket', foreground: 'cccccc' },
      { token: 'delimiter.curly', foreground: 'cccccc' },
      { token: 'delimiter.parenthesis', foreground: 'cccccc' },
      { token: 'delimiter.square', foreground: 'cccccc' },

      { token: 'tag', foreground: '569cd6' },
      { token: 'metatag', foreground: '569cd6' },
      { token: 'attribute.name', foreground: '9cdcfe' },
      { token: 'attribute.value', foreground: 'ce9178' }
    ],
    colors: {
      'editor.background': '#1f1f1f',
      'editor.foreground': '#cccccc',
      'editorLineNumber.foreground': '#6e7681',
      'editorLineNumber.activeForeground': '#cccccc',
      'editorCursor.foreground': '#aeafad',
      'editor.selectionBackground': '#264f78',
      'editor.inactiveSelectionBackground': '#3a3d41',
      'editor.selectionHighlightBackground': '#add6ff26',
      'editor.wordHighlightBackground': '#575757b8',
      'editor.wordHighlightStrongBackground': '#004972b8',
      'editor.findMatchBackground': '#9e6a03',
      'editor.findMatchHighlightBackground': '#ea5c0055',
      'editor.lineHighlightBorder': '#282828',
      'editorWhitespace.foreground': '#404040',
      'editorIndentGuide.background1': '#404040',
      'editorIndentGuide.activeBackground1': '#707070',
      'editorBracketMatch.background': '#0064001a',
      'editorBracketMatch.border': '#888888',
      'editorBracketHighlight.foreground1': '#ffd700',
      'editorBracketHighlight.foreground2': '#da70d6',
      'editorBracketHighlight.foreground3': '#179fff',
      'editorRuler.foreground': '#2b2b2b',
      'editorOverviewRuler.border': '#010409',
      'editorGutter.background': '#1f1f1f',
      'editorError.foreground': '#f14c4c',
      'editorWarning.foreground': '#cca700',
      'editorWidget.background': '#202020',
      'editorWidget.border': '#313131',
      'editorSuggestWidget.background': '#202020',
      'editorSuggestWidget.border': '#313131',
      'editorSuggestWidget.selectedBackground': '#04395e',
      'editorHoverWidget.background': '#202020',
      'editorHoverWidget.border': '#313131',
      'scrollbar.shadow': '#00000000',
      'scrollbarSlider.background': '#79797966',
      'scrollbarSlider.hoverBackground': '#646464b3',
      'scrollbarSlider.activeBackground': '#bfbfbf66',
      'minimap.background': '#1f1f1f'
    }
  });
};
