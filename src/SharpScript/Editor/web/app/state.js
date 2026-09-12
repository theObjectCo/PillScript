// What every part of the editor shares: the elements it draws into, the state it draws from, and
// the table the host's messages are routed through. Loaded first, so the rest can assume it.
(function () {
  'use strict';

  var SS = window.SS = window.SS || {};

  SS.state = {
    monaco: null,
    editor: null,

    // Monaco models by file name, which is also how the host names them.
    models: {},
    decorations: {},

    files: [],
    active: null,

    // Set while a rename or an add is in flight, so the new file is the one opened when the
    // project comes back.
    pendingActive: null,

    breakpoints: {},
    breakpointsEnabled: true,
    paused: null
  };

  // Filled by the modules that care about a message type: project, state, diagnostics, log,
  // paused, resumed.
  SS.handlers = {};

  SS.el = {};

  [
    'title', 'files', 'inputs', 'outputs', 'add', 'folder', 'references', 'references-label',
    'compile', 'run', 'breakpoints', 'inspect', 'format', 'editor', 'panel', 'toolbar', 'sidebar',
    'rail', 'status', 'status-build', 'status-caret', 'status-indent', 'status-breakpoints',
    'status-breakpoints-text', 'runtime', 'problem-count', 'debug-bar', 'continue',
    'stop', 'panel-clear', 'panel-wrap', 'win-min', 'win-max', 'win-close', 'titlebar',
    'breakpoints-toggle', 'references-panel', 'references-close', 'ref-local', 'ref-packages',
    'ref-host', 'ref-note', 'ref-add-local',
    'ref-search', 'ref-results', 'ref-restore', 'ref-edit-csproj', 'ref-packages-count',
    'ref-local-count', 'ref-host-count', 'ref-host-toggle',
    'breakpoint-menu', 'menu-toggle', 'menu-remove', 'dock-left', 'dock-right', 'dock-close',
    'splitter', 'locate', 'wrap-code', 'split-sidebar', 'split-panel', 'main',
    't-sidebar', 't-minimap', 't-panel', 't-toolbar', 't-zen',
    'tab-output', 'tab-problems', 'tab-variables',
    'view-output', 'view-problems', 'view-variables'
  ].forEach(function (id) { SS.el[id] = document.getElementById(id); });

  SS.isSource = function (name) {
    return name.slice(-3).toLowerCase() === '.cs';
  };

  /// The language id Monaco knows our C# by, once csharp.js has registered it.
  SS.csharpId = function () {
    return window.csharpLanguage.id;
  };

  // ----- small builders the panels share ------------------------------------------------------

  SS.span = function (className, text) {
    var element = document.createElement('span');

    if (className) element.className = className;
    if (text !== undefined) element.textContent = text;

    return element;
  };

  SS.div = function (className, text) {
    var element = document.createElement('div');

    if (className) element.className = className;
    if (text !== undefined) element.textContent = text;

    return element;
  };
})();
