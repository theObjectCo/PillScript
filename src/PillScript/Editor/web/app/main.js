// Starts Monaco and hands it to the rest of the page. Loaded last, once every module has
// registered its handlers.
(function () {
  'use strict';

  var SS = window.SS;
  var el = SS.el;
  var state = SS.state;

  require.config({ paths: { vs: 'vs' } });

  require(['vs/editor/editor.main'], function (loaded) {
    var monaco = state.monaco = window.monaco || loaded;

    window.defineDarkModern(monaco);

    var language = window.csharpLanguage;
    monaco.languages.register({ id: language.id, extensions: ['.cs'], aliases: ['C#'] });
    monaco.languages.setLanguageConfiguration(language.id, language.configuration);
    monaco.languages.setMonarchTokensProvider(language.id, language.language);

    SS.registerLanguageServices(language.id);

    var editor = state.editor = monaco.editor.create(el.editor, {
      theme: 'dark-modern',
      automaticLayout: true,
      fontFamily: 'Cascadia Mono, Consolas, monospace',
      fontSize: 13,
      lineHeight: 20,
      glyphMargin: true,
      minimap: { enabled: SS.minimapEnabled(), renderCharacters: false },
      renderLineHighlight: 'all',
      smoothScrolling: true,
      scrollBeyondLastLine: false,
      bracketPairColorization: { enabled: true },
      guides: { indentation: true, bracketPairs: true },
      suggestOnTriggerCharacters: true,
      quickSuggestions: { other: true, comments: false, strings: false },
      parameterHints: { enabled: true },
      tabSize: 4,
      insertSpaces: true,
      wordWrap: 'off'
    });

    editor.addCommand(monaco.KeyCode.F5, SS.compile);
    editor.addCommand(monaco.KeyMod.CtrlCmd | monaco.KeyCode.KeyB, SS.compile);
    editor.addCommand(monaco.KeyMod.CtrlCmd | monaco.KeyCode.KeyS, SS.flush);

    editor.addCommand(monaco.KeyCode.F9, function () {
      var position = editor.getPosition();
      if (position) SS.toggleBreakpoint(state.active, position.lineNumber);
    });

    editor.onDidChangeCursorPosition(function (e) {
      el['status-caret'].textContent = 'Ln ' + e.position.lineNumber + ', Col ' + e.position.column;
    });

    // The glyph margin is where a breakpoint is set, the same as in Visual Studio Code.
    editor.onMouseDown(function (e) {
      if (e.target.type !== monaco.editor.MouseTargetType.GUTTER_GLYPH_MARGIN) return;
      if (!e.target.position) return;

      SS.toggleBreakpoint(state.active, e.target.position.lineNumber);
    });

    if (window.onEditorReady) window.onEditorReady(editor, monaco);

    SS.applyLayout();
    SS.send({ type: 'ready' });

    // Opened in a browser instead of in Rhino, which is how the page itself is developed.
    if (!SS.hosted) showSample();
  });

  function showSample() {
    SS.applyProject({
      files: [
        { name: 'Script.cs', content: SAMPLE, language: 'csharp', locked: true },
        { name: 'Script.csproj', content: SAMPLE_PROJECT, language: 'xml', locked: true },
        { name: 'PolyHelper.cs', content: SAMPLE_HELPER, language: 'csharp', locked: false }
      ]
    });

    SS.applyState({
      stale: true,
      compiling: false,
      title: 'sample.gh',
      parameters: {
        inputs: [
          { name: 'points', type: 'Point', access: 'list' },
          { name: 'divisions', type: 'Integer', access: 'item' }
        ],
        outputs: [
          { name: 'out', type: 'Text', access: 'list' },
          { name: 'outline', type: 'Curve', access: 'item' }
        ]
      }
    });
  }
})();
