// What the component says about itself: the title, whether the build is behind the sources,
// where the window sits, and the parameters the last build produced.
(function () {
  'use strict';

  var SS = window.SS;
  var el = SS.el;

  SS.applyState = function (message) {
    el.compile.disabled = !!message.compiling;
    el.run.disabled = !!message.compiling;

    if (message.title) renderTitle(message.title);

    if (message.compiling) el['status-build'].textContent = 'Compiling';
    else if (message.stale) el['status-build'].textContent = 'Modified since last compile';
    else if (message.build) el['status-build'].textContent = message.build;

    if (message.runtime) el.runtime.textContent = message.runtime;

    renderDock(message);

    el['references-label'].textContent = message.references
      ? 'References (' + message.references + ')'
      : 'References';

    renderParams(el.inputs, message.parameters && message.parameters.inputs);
    renderParams(el.outputs, message.parameters && message.parameters.outputs);
  };

  SS.handlers.state = SS.applyState;

  function renderTitle(title) {
    el.title.innerHTML = '';

    el.title.appendChild(SS.span('', title));
    el.title.appendChild(SS.span('sep', ' — '));
    el.title.appendChild(SS.span('name', 'C# Script'));
  }

  function renderDock(message) {
    if (typeof message.docked === 'boolean') {
      document.body.classList.toggle('docked', message.docked);

      el.splitter.classList.toggle('hidden', !message.docked);
      el['dock-close'].classList.toggle('hidden', !message.docked);
    }

    if (typeof message.dockLeft === 'boolean') {
      document.body.classList.toggle('dock-left', message.dockLeft);
    }

    // The side it already sits on is the one that puts it back into a window of its own.
    var docked = !!message.docked;
    var left = docked && !!message.dockLeft;

    el['dock-left'].classList.toggle('on', left);
    el['dock-right'].classList.toggle('on', docked && !left);

    el['dock-left'].title = left ? 'Float the editor out' : 'Dock to the left of the canvas';
    el['dock-right'].title = docked && !left
      ? 'Float the editor out'
      : 'Dock to the right of the canvas';

    if (typeof message.canDock === 'boolean') {
      el['dock-left'].classList.toggle('hidden', !message.canDock);
      el['dock-right'].classList.toggle('hidden', !message.canDock);
    }
  }

  function renderParams(target, rows) {
    target.innerHTML = '';

    if (!rows || !rows.length) {
      target.appendChild(SS.div('empty', 'none'));
      return;
    }

    rows.forEach(function (row) {
      var line = SS.div('row');

      line.appendChild(SS.span('', row.name));
      line.appendChild(SS.span('type',
        row.access === 'item' ? row.type : row.type + ' [' + row.access + ']'));

      target.appendChild(line);
    });
  }

  // ----- what a compile found ---------------------------------------------------------------

  SS.handlers.diagnostics = function (message) {
    var items = message.items || [];
    var state = SS.state;

    // The C# files are already marked up by Roslyn as they are typed; this is for the rest,
    // where a compile is the only thing that ever has an opinion.
    Object.keys(state.models).forEach(function (name) {
      if (state.models[name].getLanguageId() === SS.csharpId()) return;

      var forFile = items.filter(function (item) { return item.file === name; });
      state.monaco.editor.setModelMarkers(state.models[name], 'compile', forFile.map(SS.marker));
    });

    SS.renderProblems(items);
  };
})();
