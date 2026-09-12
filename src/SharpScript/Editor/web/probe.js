// Stands in for the component when index.html is opened with ?probe=1, so the editor's Monaco
// wiring can be exercised in a browser. Loaded before the app modules, which then talk to this
// instead of to WebView2. Nothing here runs when the component hosts the page.

(function () {
  if (location.search.indexOf('probe') < 0) return;

  var listeners = [];

  function dispatch(payload) {
    listeners.forEach(function (listener) { listener({ data: payload }); });
  }

  var COMPLETIONS = [
    { label: 'CreateFromBox', kind: 'Method', detail: '', insert: 'CreateFromBox', sort: '0', filter: 'CreateFromBox', index: 0 },
    { label: 'CreateFromSphere', kind: 'Method', detail: '', insert: 'CreateFromSphere', sort: '1', filter: 'CreateFromSphere', index: 1 },
    { label: 'CreateFromBrep', kind: 'Method', detail: '', insert: 'CreateFromBrep', sort: '2', filter: 'CreateFromBrep', index: 2 },
    { label: 'Empty', kind: 'Property', detail: 'Mesh', insert: 'Empty', sort: '3', filter: 'Empty', index: 3 }
  ];

  window.chrome = {
    webview: {
      postMessage: function (message) {
        if (message.type === 'ready') {
          dispatch({
            type: 'project',
            files: [
              { name: 'Script.cs', content: SAMPLE, language: 'csharp', locked: true },
              { name: 'Script.csproj', content: SAMPLE_PROJECT, language: 'xml', locked: true },
              { name: 'PolyHelper.cs', content: SAMPLE_HELPER, language: 'csharp', locked: false }
            ],
            folder: 'C:\probe'
          });

          dispatch({
            type: 'state',
            stale: true,
            compiling: false,
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

          return;
        }

        if (!message.id) return;

        var payload = null;

        if (message.type === 'complete') payload = COMPLETIONS;
        else if (message.type === 'describe') payload = 'Creates a mesh from a box.';
        else if (message.type === 'hover') payload = 'class Rhino.Geometry.Mesh\n\nA mesh of triangles and quads.';
        else if (message.type === 'signature') {
          payload = {
            signatures: [{
              label: 'Circle.Circle(Plane plane, double radius)',
              documentation: 'A circle on a plane.',
              parameters: ['Plane plane', 'double radius']
            }],
            active: 1
          };
        } else if (message.type === 'diagnose') {
          payload = [{
            file: message.file, line: 12, column: 13, endLine: 12, endColumn: 19,
            severity: 'warning', id: 'CS0219',
            message: "The variable 'unused' is assigned but its value is never used"
          }];
        }

        setTimeout(function () {
          dispatch({ type: 'reply', id: message.id, payload: payload });
        }, 10);
      },

      addEventListener: function (name, callback) {
        if (name === 'message') listeners.push(callback);
      }
    }
  };

  // Once the editor exists, put the caret after a dot and ask for the suggestion list, so a
  // headless run can see whether the provider is wired up.
  window.onEditorReady = function (editor, monaco) {
    setTimeout(function () {
      var model = editor.getModel();
      if (!model) return;

      var last = model.getLineCount();
      editor.setPosition({ lineNumber: last, column: model.getLineMaxColumn(last) });
      editor.trigger('probe', 'type', { text: '\nvar m = Mesh.' });

      setTimeout(function () {
        editor.trigger('probe', 'editor.action.triggerSuggest', {});
      }, 400);
    }, 1200);
  };
})();
