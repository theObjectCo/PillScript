// What Monaco asks and Roslyn answers: completion, hover, signature help and the squiggles that
// appear while typing, before anything has been compiled.
(function () {
  'use strict';

  var SS = window.SS;
  var state = SS.state;

  // Long enough that typing a word does not send a request per keystroke, short enough that a
  // mistake is underlined before the eye has moved on.
  var DIAGNOSE_DELAY = 400;

  var timers = {};

  function nameOf(model) {
    var uri = model.uri.toString();
    return uri.substring(uri.lastIndexOf('/') + 1);
  }

  function at(model, position) {
    return { file: nameOf(model), text: model.getValue(), offset: model.getOffsetAt(position) };
  }

  SS.registerLanguageServices = function (id) {
    var monaco = state.monaco;

    monaco.languages.registerCompletionItemProvider(id, {
      triggerCharacters: ['.', ' ', '(', '<', '[', ':'],

      provideCompletionItems: function (model, position, context) {
        var query = at(model, position);
        query.trigger = context.triggerCharacter || '';

        return SS.request('complete', query).then(function (entries) {
          if (!entries || !entries.length) return { suggestions: [] };

          var word = model.getWordUntilPosition(position);
          var range = {
            startLineNumber: position.lineNumber,
            endLineNumber: position.lineNumber,
            startColumn: word.startColumn,
            endColumn: word.endColumn
          };

          return {
            suggestions: entries.map(function (entry) {
              return {
                label: entry.label,
                kind: completionKind(entry.kind),
                insertText: entry.insert,
                sortText: entry.sort,
                filterText: entry.filter,
                detail: entry.detail || undefined,
                range: range,

                // Kept so the documentation can be fetched for this one item alone, once the
                // list highlights it.
                describeWith: {
                  file: query.file,
                  text: query.text,
                  offset: query.offset,
                  trigger: query.trigger,
                  index: entry.index
                }
              };
            })
          };
        });
      },

      resolveCompletionItem: function (item) {
        if (!item.describeWith) return item;

        return SS.request('describe', item.describeWith).then(function (text) {
          if (text) item.documentation = text;
          return item;
        });
      }
    });

    monaco.languages.registerHoverProvider(id, {
      provideHover: function (model, position) {
        return SS.request('hover', at(model, position)).then(function (text) {
          if (!text) return null;

          // Roslyn puts the signature first and the prose after a blank line; the signature is
          // worth showing as code, the prose is not.
          var split = text.indexOf('\n\n');
          var signature = split < 0 ? text : text.substring(0, split);
          var prose = split < 0 ? '' : text.substring(split + 2);

          var contents = [{ value: '```csharp\n' + signature + '\n```' }];
          if (prose.trim()) contents.push({ value: prose });

          return { contents: contents };
        });
      }
    });

    monaco.languages.registerSignatureHelpProvider(id, {
      signatureHelpTriggerCharacters: ['(', ','],
      signatureHelpRetriggerCharacters: [')'],

      provideSignatureHelp: function (model, position) {
        return SS.request('signature', at(model, position)).then(function (help) {
          if (!help || !help.signatures || !help.signatures.length) return null;

          return {
            value: {
              signatures: help.signatures.map(function (signature) {
                return {
                  label: signature.label,
                  documentation: signature.documentation || undefined,
                  parameters: (signature.parameters || []).map(function (parameter) {
                    return { label: parameter };
                  })
                };
              }),
              activeSignature: 0,
              activeParameter: help.active || 0
            },
            dispose: function () { }
          };
        });
      }
    });
  };

  function completionKind(name) {
    var kinds = state.monaco.languages.CompletionItemKind;
    return name && kinds[name] !== undefined ? kinds[name] : kinds.Text;
  }

  // ----- diagnostics ---------------------------------------------------------------------------

  SS.scheduleDiagnose = function (name) {
    if (timers[name]) clearTimeout(timers[name]);

    timers[name] = setTimeout(function () {
      delete timers[name];
      SS.diagnose(name);
    }, DIAGNOSE_DELAY);
  };

  SS.diagnose = function (name) {
    var model = state.models[name];
    if (!model || model.getLanguageId() !== SS.csharpId()) return;

    SS.request('diagnose', { file: name, text: model.getValue() }).then(function (items) {
      if (!items || !state.models[name]) return;

      state.monaco.editor.setModelMarkers(state.models[name], 'roslyn', items.map(SS.marker));
    });
  };

  SS.marker = function (item) {
    return {
      severity: item.severity === 'error'
        ? state.monaco.MarkerSeverity.Error
        : state.monaco.MarkerSeverity.Warning,
      message: item.id + ': ' + item.message,
      startLineNumber: item.line,
      startColumn: item.column,
      endLineNumber: item.endLine || item.line,
      endColumn: item.endColumn || item.column + 1
    };
  };
})();
