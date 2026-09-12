// Where the script stops, and what it looks like while it is stopped. The component owns the
// marks and compiles them in, so every change here is reported to it.
(function () {
  'use strict';

  var SS = window.SS;
  var el = SS.el;
  var state = SS.state;

  SS.toggleBreakpoint = function (file, line) {
    if (!file || !state.models[file]) return;
    if (state.models[file].getLanguageId() !== SS.csharpId()) return;

    var set = state.breakpoints[file] || (state.breakpoints[file] = []);
    var at = set.indexOf(line);

    if (at < 0) set.push(line);
    else set.splice(at, 1);

    SS.renderBreakpoints(file);
    report(file);
    SS.updateBreakpointStatus();
  };

  SS.renderBreakpoints = function (file) {
    var model = state.models[file];
    if (!model) return;

    var monaco = state.monaco;
    var set = (state.breakpoints[file] || []).slice().sort(function (a, b) { return a - b; });
    var off = state.breakpointsEnabled ? '' : ' off';

    var items = set.map(function (line) {
      return {
        range: new monaco.Range(line, 1, line, 1),
        options: {
          isWholeLine: true,
          glyphMarginClassName: 'breakpoint-glyph' + off,
          className: 'breakpoint-line' + off,
          stickiness: monaco.editor.TrackedRangeStickiness.NeverGrowsWhenTypingAtEdges
        }
      };
    });

    if (state.paused && state.paused.file === file) {
      items.push({
        range: new monaco.Range(state.paused.line, 1, state.paused.line, 1),
        options: { isWholeLine: true, className: 'paused-line' }
      });
    }

    state.decorations[file] = model.deltaDecorations(state.decorations[file] || [], items);
  };

  function report(file) {
    SS.send({ type: 'breakpoints', file: file, lines: state.breakpoints[file] || [] });
  }

  function count() {
    return Object.keys(state.breakpoints).reduce(function (total, file) {
      return total + state.breakpoints[file].length;
    }, 0);
  }

  SS.updateBreakpointStatus = function () {
    var total = count();
    var enabled = state.breakpointsEnabled;

    el['status-breakpoints'].classList.toggle('hidden', total === 0);
    el['status-breakpoints'].classList.toggle('off', !enabled);

    el['status-breakpoints-text'].textContent =
      total + (total === 1 ? ' breakpoint' : ' breakpoints') + (enabled ? '' : ', off');

    el.breakpoints.classList.toggle('armed', total > 0 && enabled);

    // Highlighted means the marks are switched off, which is a state worth seeing at a glance.
    el['breakpoints-toggle'].classList.toggle('on', !enabled);
    el['breakpoints-toggle'].title = enabled ? 'Disable all breakpoints' : 'Enable all breakpoints';
    el['menu-toggle'].textContent = enabled ? 'Disable all breakpoints' : 'Enable all breakpoints';

    if (total === 0) closeMenu();
  };

  // ----- the menu on the count --------------------------------------------------------------

  function closeMenu() {
    el['breakpoint-menu'].classList.add('hidden');
  }

  el['status-breakpoints'].onclick = function (event) {
    event.stopPropagation();
    el['breakpoint-menu'].classList.toggle('hidden');
  };

  document.addEventListener('mousedown', function (event) {
    if (event.target.closest('#breakpoint-menu')) return;
    if (event.target.closest('#status-breakpoints')) return;

    closeMenu();
  });

  function toggleEnabled() {
    state.breakpointsEnabled = !state.breakpointsEnabled;
    SS.send({ type: 'breakpointsEnabled', enabled: state.breakpointsEnabled });

    Object.keys(state.breakpoints).forEach(SS.renderBreakpoints);
    SS.updateBreakpointStatus();
  }

  function removeAll() {
    Object.keys(state.breakpoints).forEach(function (file) {
      state.breakpoints[file] = [];

      SS.renderBreakpoints(file);
      report(file);
    });

    SS.updateBreakpointStatus();
  }

  el['menu-toggle'].onclick = function () { toggleEnabled(); closeMenu(); };
  el['menu-remove'].onclick = function () { removeAll(); closeMenu(); };
  el['breakpoints-toggle'].onclick = toggleEnabled;
  el.breakpoints.onclick = removeAll;

  // ----- stopped ------------------------------------------------------------------------------

  SS.handlers.paused = function (message) {
    state.paused = { file: message.file, line: message.line };

    el['debug-bar'].classList.remove('hidden');

    if (state.models[message.file]) {
      SS.open(message.file);
      state.editor.revealLineInCenter(message.line);
      SS.renderBreakpoints(message.file);
    }

    SS.renderVariables(message.variables || [], message.file + ' line ' + message.line);
    SS.showTab('variables');
  };

  SS.handlers.resumed = function () {
    var file = state.paused && state.paused.file;
    state.paused = null;

    el['debug-bar'].classList.add('hidden');

    if (file) SS.renderBreakpoints(file);
  };

  el.continue.onclick = function () { SS.send({ type: 'debugContinue' }); };
  el.stop.onclick = function () { SS.send({ type: 'debugStop' }); };
})();
