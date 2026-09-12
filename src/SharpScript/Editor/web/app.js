(function () {
  'use strict';

  var host = window.chrome && window.chrome.webview;

  var monacoApi = null;
  var editor = null;
  var models = {};
  var decorations = {};
  var breakpoints = {};
  var files = [];
  var active = null;
  var saveTimer = null;
  var diagnoseTimers = {};
  var pendingActive = null;

  var CRLF = String.fromCharCode(13, 10);
  var pending = {};
  var nextRequest = 1;

  var terminal = null;
  var fitAddon = null;
  var terminalStarted = false;

  var layout = { sidebar: true, minimap: true, panel: true, toolbar: true };
  var panelTab = 'output';
  var wrapLog = true;
  var wrapCode = false;
  var paused = null;
  var breakpointsEnabled = true;

  var el = {};
  ['title', 'files', 'inputs', 'outputs', 'add', 'folder', 'references', 'references-label',
   'compile', 'run', 'breakpoints', 'inspect', 'format', 'editor', 'panel', 'toolbar', 'sidebar',
   'rail', 'status', 'status-build', 'status-caret', 'status-indent', 'status-breakpoints',
   'status-breakpoints-text', 'runtime', 'runtime-dot', 'problem-count', 'debug-bar', 'continue',
   'stop', 'panel-clear', 'panel-wrap', 'win-min', 'win-max', 'win-close', 'titlebar',
   'breakpoints-toggle', 'references-panel', 'references-close', 'ref-local', 'ref-packages',
   'ref-host', 'ref-note', 'ref-add-local',
   'ref-search', 'ref-results', 'ref-restore', 'ref-edit-csproj', 'ref-packages-count',
   'ref-local-count', 'ref-host-count', 'ref-host-toggle',
   'breakpoint-menu', 'menu-toggle', 'menu-remove', 'dock-left', 'dock-right', 'dock-close',
   'splitter', 'locate', 'wrap-code', 'split-sidebar', 'split-panel', 'main',
   't-sidebar', 't-minimap', 't-panel', 't-toolbar', 't-zen',
   'tab-output', 'tab-terminal', 'tab-problems', 'tab-variables',
   'view-output', 'view-terminal', 'view-problems', 'view-variables'
  ].forEach(function (id) { el[id] = document.getElementById(id); });

  // ----- startup ---------------------------------------------------------------------------

  require.config({ paths: { vs: 'vs' } });

  require(['vs/editor/editor.main'], function (loaded) {
    monacoApi = window.monaco || loaded;

    window.defineDarkModern(monacoApi);

    var language = window.csharpLanguage;
    monacoApi.languages.register({ id: language.id, extensions: ['.cs'], aliases: ['C#'] });
    monacoApi.languages.setLanguageConfiguration(language.id, language.configuration);
    monacoApi.languages.setMonarchTokensProvider(language.id, language.language);

    registerLanguageServices(language.id);

    editor = monacoApi.editor.create(el.editor, {
      theme: 'dark-modern',
      automaticLayout: true,
      fontFamily: 'Cascadia Mono, Consolas, monospace',
      fontSize: 13,
      lineHeight: 20,
      glyphMargin: true,
      minimap: { enabled: layout.minimap, renderCharacters: false },
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

    editor.addCommand(monacoApi.KeyCode.F5, compile);
    editor.addCommand(monacoApi.KeyMod.CtrlCmd | monacoApi.KeyCode.KeyB, compile);
    editor.addCommand(monacoApi.KeyMod.CtrlCmd | monacoApi.KeyCode.KeyS, flush);
    editor.addCommand(monacoApi.KeyCode.F9, function () {
      var position = editor.getPosition();
      if (position) toggleBreakpoint(active, position.lineNumber);
    });

    editor.onDidChangeCursorPosition(function (e) {
      el['status-caret'].textContent = 'Ln ' + e.position.lineNumber + ', Col ' + e.position.column;
    });

    // The glyph margin is where a breakpoint is set, the same as in Visual Studio Code.
    editor.onMouseDown(function (e) {
      if (e.target.type !== monacoApi.editor.MouseTargetType.GUTTER_GLYPH_MARGIN) return;
      if (!e.target.position) return;

      toggleBreakpoint(active, e.target.position.lineNumber);
    });

    if (window.onEditorReady) window.onEditorReady(editor, monacoApi);

    send({ type: 'ready' });

    if (!host) showSample();
  });

  function showSample() {
    applyProject({
      files: [
        { name: 'Script.cs', content: SAMPLE, language: 'csharp', locked: true },
        { name: 'Script.csproj', content: SAMPLE_PROJECT, language: 'xml', locked: true },
        { name: 'PolyHelper.cs', content: SAMPLE_HELPER, language: 'csharp', locked: false }
      ]
    });

    applyState({
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

  // ----- bridge ----------------------------------------------------------------------------

  function send(message) {
    if (host) host.postMessage(message);
  }

  window.addEventListener('error', function (event) {
    send({ type: 'pageError', text: String(event.message) + ' at ' + event.filename + ':' + event.lineno });
  });

  window.addEventListener('unhandledrejection', function (event) {
    send({ type: 'pageError', text: 'Unhandled rejection: ' + String(event.reason) });
  });

  function request(type, payload) {
    if (!host) return Promise.resolve(null);

    return new Promise(function (resolve) {
      var id = String(nextRequest++);
      pending[id] = resolve;

      var message = { type: type, id: id };
      for (var key in payload) message[key] = payload[key];
      send(message);

      setTimeout(function () {
        if (!pending[id]) return;
        delete pending[id];
        resolve(null);
      }, 10000);
    });
  }

  if (host) {
    host.addEventListener('message', function (event) {
      var message = event.data;
      if (!message || !message.type) return;

      if (message.type === 'reply') {
        var resolve = pending[message.id];
        if (!resolve) return;

        delete pending[message.id];
        resolve(message.payload);
        return;
      }

      if (message.type === 'project') applyProject(message);
      else if (message.type === 'state') applyState(message);
      else if (message.type === 'diagnostics') applyDiagnostics(message.items || []);
      else if (message.type === 'log') log(message.text, message.kind);
      else if (message.type === 'terminal') writeTerminal(message.text);
      else if (message.type === 'terminalExit') writeTerminal(CRLF + '[the shell exited]' + CRLF);
      else if (message.type === 'paused') showPaused(message);
      else if (message.type === 'resumed') clearPaused();
    });
  }

  // ----- layout ----------------------------------------------------------------------------

  function applyLayout() {
    el.sidebar.classList.toggle('hidden', !layout.sidebar);
    el.panel.classList.toggle('hidden', !layout.panel);
    el.toolbar.classList.toggle('hidden', !layout.toolbar);

    document.body.classList.toggle('no-sidebar', !layout.sidebar);
    document.body.classList.toggle('no-panel', !layout.panel);

    el['t-sidebar'].classList.toggle('on', layout.sidebar);
    el['t-minimap'].classList.toggle('on', layout.minimap);
    el['t-panel'].classList.toggle('on', layout.panel);
    el['t-toolbar'].classList.toggle('on', layout.toolbar);

    if (editor) editor.updateOptions({ minimap: { enabled: layout.minimap, renderCharacters: false } });
    if (terminal && fitAddon && layout.panel && panelTab === 'terminal') fitTerminal();
  }

  function toggle(name) {
    layout[name] = !layout[name];
    applyLayout();
  }

  el['t-sidebar'].onclick = function () { toggle('sidebar'); };
  el['t-minimap'].onclick = function () { toggle('minimap'); };
  el['t-panel'].onclick = function () { toggle('panel'); };
  el['t-toolbar'].onclick = function () { toggle('toolbar'); };

  el['t-zen'].onclick = function () {
    var anyOn = layout.sidebar || layout.minimap || layout.panel || layout.toolbar;
    layout = { sidebar: !anyOn, minimap: !anyOn, panel: !anyOn, toolbar: !anyOn };
    applyLayout();
  };

  el['dock-left'].onclick = function () { send({ type: 'dock', action: 'left' }); };
  el['dock-right'].onclick = function () { send({ type: 'dock', action: 'right' }); };
  el['dock-close'].onclick = function () { send({ type: 'window', action: 'close' }); };
  el.locate.onclick = function () { send({ type: 'locate' }); };

  // Dragging the splitter reports how far it has moved from where it was grabbed; the host turns
  // that into a share of the Grasshopper window, so the split survives the window being resized.
  el.splitter.addEventListener('pointerdown', function (event) {
    if (event.button !== 0) return;

    var start = event.screenX;
    var ratio = window.devicePixelRatio || 1;

    // Widening means dragging away from the canvas, which is leftwards on the right side and
    // rightwards on the left one, so the sign follows the side the editor is docked to.
    var sign = document.body.classList.contains('dock-left') ? 1 : -1;

    el.splitter.setPointerCapture(event.pointerId);
    el.splitter.classList.add('dragging');
    send({ type: 'dockSplitStart' });

    function move(moved) {
      send({ type: 'dockSplit', offset: Math.round((moved.screenX - start) * ratio * sign) });
    }

    function stop() {
      el.splitter.classList.remove('dragging');
      el.splitter.removeEventListener('pointermove', move);
      el.splitter.removeEventListener('pointerup', stop);
      el.splitter.removeEventListener('pointercancel', stop);
    }

    el.splitter.addEventListener('pointermove', move);
    el.splitter.addEventListener('pointerup', stop);
    el.splitter.addEventListener('pointercancel', stop);
  });

  Array.prototype.forEach.call(document.querySelectorAll('.resize'), function (zone) {
    zone.addEventListener('mousedown', function (event) {
      if (event.button !== 0) return;
      send({ type: 'window', action: 'resize-' + zone.getAttribute('data-edge') });
    });
  });

  el['win-min'].onclick = function () { send({ type: 'window', action: 'minimize' }); };
  el['win-max'].onclick = function () { send({ type: 'window', action: 'maximize' }); };
  el['win-close'].onclick = function () { send({ type: 'window', action: 'close' }); };

  // WebView2 at the version Rhino ships has no non-client regions, so dragging the window is
  // relayed to the host instead of being declared in CSS.
  el.titlebar.addEventListener('mousedown', function (event) {
    if (event.button !== 0) return;
    if (event.target.closest('.window-buttons')) return;
    if (document.body.classList.contains('docked')) return;

    send({ type: 'window', action: 'drag' });
  });

  el.titlebar.addEventListener('dblclick', function (event) {
    if (event.target.closest('.window-buttons')) return;
    if (document.body.classList.contains('docked')) return;

    send({ type: 'window', action: 'maximize' });
  });

  // Dragging a pane edge. The size is written straight onto the pane, because these are the two
  // places where a person wants a different balance than the one that was picked for them.
  function paneSplitter(handle, pane, vertical, invert) {
    handle.addEventListener('pointerdown', function (event) {
      if (event.button !== 0) return;

      var start = vertical ? event.clientX : event.clientY;
      var from = vertical ? pane.offsetWidth : pane.offsetHeight;

      handle.setPointerCapture(event.pointerId);
      handle.classList.add('dragging');

      function move(moved) {
        var now = vertical ? moved.clientX : moved.clientY;
        var size = from + (invert ? start - now : now - start);

        size = Math.max(140, Math.min(vertical ? window.innerWidth - 220 : window.innerHeight - 220, size));

        if (vertical) pane.style.width = size + 'px';
        else pane.style.height = size + 'px';

        if (terminal && fitAddon && panelTab === 'terminal') fitTerminal();
      }

      function stop() {
        handle.classList.remove('dragging');
        handle.removeEventListener('pointermove', move);
        handle.removeEventListener('pointerup', stop);
        handle.removeEventListener('pointercancel', stop);
      }

      handle.addEventListener('pointermove', move);
      handle.addEventListener('pointerup', stop);
      handle.addEventListener('pointercancel', stop);
    });
  }

  paneSplitter(el['split-sidebar'], el.sidebar, true, false);
  paneSplitter(el['split-panel'], el.panel, false, true);

  // ----- panel -----------------------------------------------------------------------------

  function showTab(name) {
    panelTab = name;

    ['output', 'terminal', 'problems', 'variables'].forEach(function (tab) {
      el['tab-' + tab].classList.toggle('on', tab === name);
      el['view-' + tab].classList.toggle('on', tab === name);
    });

    if (name === 'terminal') startTerminal();
  }

  el['tab-output'].onclick = function () { showTab('output'); };
  el['tab-terminal'].onclick = function () { showTab('terminal'); };
  el['tab-problems'].onclick = function () { showTab('problems'); };
  el['tab-variables'].onclick = function () { showTab('variables'); };

  el['panel-clear'].onclick = function () {
    if (panelTab === 'terminal' && terminal) { terminal.clear(); return; }
    el['view-output'].innerHTML = '';
  };

  el['panel-wrap'].onclick = function () {
    wrapLog = !wrapLog;
    el['panel-wrap'].classList.toggle('on', wrapLog);
    el.panel.classList.toggle('nowrap', !wrapLog);
  };

  el['wrap-code'].onclick = function () {
    wrapCode = !wrapCode;
    el['wrap-code'].classList.toggle('on', wrapCode);
    editor.updateOptions({ wordWrap: wrapCode ? 'on' : 'off' });
  };

  function log(text, kind) {
    if (!text) return;

    var now = new Date();
    var stamp = [now.getHours(), now.getMinutes(), now.getSeconds()]
      .map(function (part) { return part < 10 ? '0' + part : String(part); })
      .join(':');

    text.split('\n').forEach(function (part) {
      var line = document.createElement('div');
      line.className = 'line';

      var time = document.createElement('span');
      time.className = 'time';
      time.textContent = stamp;

      var body = document.createElement('span');
      body.className = 'text' + (kind ? ' ' + kind : '');
      body.textContent = part;

      line.appendChild(time);
      line.appendChild(body);
      el['view-output'].appendChild(line);
    });

    el['view-output'].scrollTop = el['view-output'].scrollHeight;
  }

  // ----- terminal --------------------------------------------------------------------------

  // The shell on the other end is a process with redirected streams, not a terminal. It cannot
  // interpret a backspace, an arrow key or a break, so the line is edited here and only whole
  // lines are sent. The local echo is wiped the moment the line is submitted, because the shell
  // prints the prompt and the command itself and one copy of each is enough.

  var CR = String.fromCharCode(13);
  var LF = String.fromCharCode(10);
  var BS = String.fromCharCode(8);
  var DEL = String.fromCharCode(127);
  var ETX = String.fromCharCode(3);
  var ESC = String.fromCharCode(27);

  var ERASE_LINE = ESC + '[2K' + CR;
  var RUBOUT = BS + ' ' + BS;

  // A bare line feed means "one row down, same column" to a terminal, so output that ends its
  // lines that way walks off to the right a step at a time. The shell writes some of its lines
  // like that, so every line break is made into a proper carriage return and line feed.
  var BARE_LINE_FEED = new RegExp(CR + '?' + LF, 'g');

  var line = '';
  var history = [];
  var historyAt = 0;

  function startTerminal() {
    if (terminal || !window.Terminal) return;

    terminal = new window.Terminal({
      fontFamily: 'Cascadia Mono, Consolas, monospace',
      fontSize: 12,
      cursorBlink: true,
      allowProposedApi: true,
      theme: {
        background: '#181818',
        foreground: '#cccccc',
        cursor: '#aeafad',
        selectionBackground: '#264f78',
        black: '#272727', red: '#f14c4c', green: '#23d18b', yellow: '#f5f543',
        blue: '#3b8eea', magenta: '#d670d6', cyan: '#29b8db', white: '#e5e5e5',
        brightBlack: '#5f5f5f', brightRed: '#f14c4c', brightGreen: '#23d18b',
        brightYellow: '#f5f543', brightBlue: '#3b8eea', brightMagenta: '#d670d6',
        brightCyan: '#29b8db', brightWhite: '#e5e5e5'
      }
    });

    if (window.FitAddon && window.FitAddon.FitAddon) {
      fitAddon = new window.FitAddon.FitAddon();
      terminal.loadAddon(fitAddon);
    }

    terminal.open(el['view-terminal']);
    terminal.onData(onTyped);

    window.addEventListener('resize', fitTerminal);
    fitTerminal();

    if (!terminalStarted) {
      terminalStarted = true;
      send({ type: 'terminalStart', cols: terminal.cols, rows: terminal.rows });
    }
  }

  function onTyped(data) {
    for (var i = 0; i < data.length; i++) {
      var ch = data.charAt(i);

      if (ch === ESC) { i = handleEscape(data, i); continue; }
      if (ch === CR || ch === LF) { submitLine(); continue; }
      if (ch === DEL || ch === BS) { rubout(); continue; }
      if (ch === ETX) { cancelLine(); continue; }

      // Anything below space that is not handled above is a control key the shell cannot use.
      if (ch < ' ') continue;

      line += ch;
      terminal.write(ch);
    }
  }

  /// Consumes an escape sequence and acts on the two that matter: up and down walk the history.
  function handleEscape(data, index) {
    if (data.charAt(index + 1) !== '[') return index;

    var end = index + 2;
    while (end < data.length && (data.charAt(end) < '@' || data.charAt(end) > '~')) end++;

    var final = data.charAt(end);

    if (final === 'A') recall(-1);
    else if (final === 'B') recall(1);

    return end;
  }

  function recall(step) {
    if (!history.length) return;

    historyAt = Math.max(0, Math.min(history.length, historyAt + step));
    replaceLine(historyAt === history.length ? '' : history[historyAt]);
  }

  function replaceLine(text) {
    terminal.write(ERASE_LINE);
    line = text;
    terminal.write(text);
  }

  function rubout() {
    if (!line.length) return;

    line = line.slice(0, -1);
    terminal.write(RUBOUT);
  }

  function cancelLine() {
    terminal.write('^C' + CR + LF);
    line = '';
    historyAt = history.length;
  }

  function submitLine() {
    // The shell echoes the prompt and the command, so the copy typed here is taken back first.
    terminal.write(ERASE_LINE);

    if (line.trim()) {
      history.push(line);
      if (history.length > 200) history.shift();
    }

    historyAt = history.length;

    send({ type: 'terminalInput', text: line + LF });
    line = '';
  }

  function fitTerminal() {
    if (!terminal || !fitAddon) return;

    try {
      fitAddon.fit();
      send({ type: 'terminalResize', cols: terminal.cols, rows: terminal.rows });
    } catch (error) {
      // Fitting before the panel has a size throws; the next call gets it right.
    }
  }

  function writeTerminal(text) {
    if (!terminal || !text) return;
    terminal.write(text.replace(BARE_LINE_FEED, CR + LF));
  }

  // ----- breakpoints -----------------------------------------------------------------------

  function toggleBreakpoint(file, line) {
    if (!file || !models[file]) return;
    if (models[file].getLanguageId() !== window.csharpLanguage.id) return;

    var set = breakpoints[file] || (breakpoints[file] = []);
    var at = set.indexOf(line);

    if (at < 0) set.push(line);
    else set.splice(at, 1);

    renderBreakpoints(file);
    sendBreakpoints(file);
    updateBreakpointStatus();
  }

  function renderBreakpoints(file) {
    var model = models[file];
    if (!model) return;

    var set = (breakpoints[file] || []).slice().sort(function (a, b) { return a - b; });

    var items = set.map(function (line) {
      return {
        range: new monacoApi.Range(line, 1, line, 1),
        options: {
          isWholeLine: true,
          glyphMarginClassName: breakpointsEnabled ? 'breakpoint-glyph' : 'breakpoint-glyph off',
          className: breakpointsEnabled ? 'breakpoint-line' : 'breakpoint-line off',
          stickiness: monacoApi.editor.TrackedRangeStickiness.NeverGrowsWhenTypingAtEdges
        }
      };
    });

    if (paused && paused.file === file) {
      items.push({
        range: new monacoApi.Range(paused.line, 1, paused.line, 1),
        options: { isWholeLine: true, className: 'paused-line' }
      });
    }

    decorations[file] = model.deltaDecorations(decorations[file] || [], items);
  }

  function sendBreakpoints(file) {
    send({ type: 'breakpoints', file: file, lines: breakpoints[file] || [] });
  }

  function countBreakpoints() {
    return Object.keys(breakpoints).reduce(function (total, file) {
      return total + breakpoints[file].length;
    }, 0);
  }

  function updateBreakpointStatus() {
    var count = countBreakpoints();

    el['status-breakpoints'].classList.toggle('hidden', count === 0);
    el['status-breakpoints'].classList.toggle('off', !breakpointsEnabled);

    el['status-breakpoints-text'].textContent =
      count + (count === 1 ? ' breakpoint' : ' breakpoints') + (breakpointsEnabled ? '' : ', off');

    el.breakpoints.classList.toggle('armed', count > 0 && breakpointsEnabled);

    // Highlighted means the marks are switched off, which is a state worth seeing at a glance.
    el['breakpoints-toggle'].classList.toggle('on', !breakpointsEnabled);
    el['breakpoints-toggle'].title = breakpointsEnabled
      ? 'Disable all breakpoints'
      : 'Enable all breakpoints';

    el['menu-toggle'].textContent = breakpointsEnabled
      ? 'Disable all breakpoints'
      : 'Enable all breakpoints';

    if (count === 0) closeMenu();
  }

  function closeMenu() {
    el['breakpoint-menu'].classList.add('hidden');
  }

  el['status-breakpoints'].onclick = function (event) {
    event.stopPropagation();
    el['breakpoint-menu'].classList.toggle('hidden');
  };

  function toggleBreakpointsEnabled() {
    breakpointsEnabled = !breakpointsEnabled;
    send({ type: 'breakpointsEnabled', enabled: breakpointsEnabled });

    Object.keys(breakpoints).forEach(renderBreakpoints);
    updateBreakpointStatus();
  }

  el['menu-toggle'].onclick = function () {
    toggleBreakpointsEnabled();
    closeMenu();
  };

  el['breakpoints-toggle'].onclick = toggleBreakpointsEnabled;

  el['menu-remove'].onclick = function () {
    removeAllBreakpoints();
    closeMenu();
  };

  document.addEventListener('mousedown', function (event) {
    if (event.target.closest('#breakpoint-menu')) return;
    if (event.target.closest('#status-breakpoints')) return;

    closeMenu();
  });

  function removeAllBreakpoints() {
    Object.keys(breakpoints).forEach(function (file) {
      breakpoints[file] = [];
      renderBreakpoints(file);
      sendBreakpoints(file);
    });

    updateBreakpointStatus();
  }

  el.breakpoints.onclick = removeAllBreakpoints;

  // ----- debugging -------------------------------------------------------------------------

  function showPaused(message) {
    paused = { file: message.file, line: message.line };

    el['debug-bar'].classList.remove('hidden');
    el['runtime-dot'].className = 'dot busy';

    if (models[message.file]) {
      open(message.file);
      editor.revealLineInCenter(message.line);
      renderBreakpoints(message.file);
    }

    renderVariables(message.variables || [], message.file + ' line ' + message.line);
    showTab('variables');
  }

  function clearPaused() {
    var file = paused && paused.file;
    paused = null;

    el['debug-bar'].classList.add('hidden');
    el['runtime-dot'].className = 'dot';

    if (file) renderBreakpoints(file);
  }

  function renderVariables(rows, where) {
    el['view-variables'].innerHTML = '';

    var head = document.createElement('div');
    head.className = 'frame';
    head.textContent = where || '';
    el['view-variables'].appendChild(head);

    if (!rows.length) {
      var none = document.createElement('div');
      none.className = 'none';
      none.textContent = 'No locals in scope.';
      el['view-variables'].appendChild(none);
      return;
    }

    rows.forEach(function (row) {
      var line = document.createElement('div');
      line.className = 'row';

      var name = document.createElement('span');
      name.className = 'name';
      name.textContent = row.name;

      var type = document.createElement('span');
      type.className = 'type';
      type.textContent = row.type;

      var value = document.createElement('span');
      value.className = 'value';
      value.textContent = row.value;

      line.appendChild(name);
      line.appendChild(type);
      line.appendChild(value);
      el['view-variables'].appendChild(line);
    });
  }

  el.continue.onclick = function () { send({ type: 'debugContinue' }); };
  el.stop.onclick = function () { send({ type: 'debugStop' }); };
  el.inspect.onclick = function () { showTab('variables'); };

  // ----- language services -----------------------------------------------------------------

  function nameOf(model) {
    var uri = model.uri.toString();
    return uri.substring(uri.lastIndexOf('/') + 1);
  }

  function at(model, position) {
    return { file: nameOf(model), text: model.getValue(), offset: model.getOffsetAt(position) };
  }

  function registerLanguageServices(id) {
    monacoApi.languages.registerCompletionItemProvider(id, {
      triggerCharacters: ['.', ' ', '(', '<', '[', ':'],

      provideCompletionItems: function (model, position, context) {
        var query = at(model, position);
        query.trigger = context.triggerCharacter || '';

        return request('complete', query).then(function (entries) {
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

        return request('describe', item.describeWith).then(function (text) {
          if (text) item.documentation = text;
          return item;
        });
      }
    });

    monacoApi.languages.registerHoverProvider(id, {
      provideHover: function (model, position) {
        return request('hover', at(model, position)).then(function (text) {
          if (!text) return null;

          var split = text.indexOf('\n\n');
          var signature = split < 0 ? text : text.substring(0, split);
          var prose = split < 0 ? '' : text.substring(split + 2);

          var contents = [{ value: '```csharp\n' + signature + '\n```' }];
          if (prose.trim()) contents.push({ value: prose });

          return { contents: contents };
        });
      }
    });

    monacoApi.languages.registerSignatureHelpProvider(id, {
      signatureHelpTriggerCharacters: ['(', ','],
      signatureHelpRetriggerCharacters: [')'],

      provideSignatureHelp: function (model, position) {
        return request('signature', at(model, position)).then(function (help) {
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
  }

  function completionKind(name) {
    var kinds = monacoApi.languages.CompletionItemKind;
    return name && kinds[name] !== undefined ? kinds[name] : kinds.Text;
  }

  // ----- diagnostics -----------------------------------------------------------------------

  function scheduleDiagnose(name) {
    if (diagnoseTimers[name]) clearTimeout(diagnoseTimers[name]);

    diagnoseTimers[name] = setTimeout(function () {
      delete diagnoseTimers[name];
      diagnose(name);
    }, 400);
  }

  function diagnose(name) {
    var model = models[name];
    if (!model || model.getLanguageId() !== window.csharpLanguage.id) return;

    request('diagnose', { file: name, text: model.getValue() }).then(function (items) {
      if (!items || !models[name]) return;
      monacoApi.editor.setModelMarkers(models[name], 'roslyn', items.map(marker));
    });
  }

  function marker(item) {
    return {
      severity: item.severity === 'error'
        ? monacoApi.MarkerSeverity.Error
        : monacoApi.MarkerSeverity.Warning,
      message: item.id + ': ' + item.message,
      startLineNumber: item.line,
      startColumn: item.column,
      endLineNumber: item.endLine || item.line,
      endColumn: item.endColumn || item.column + 1
    };
  }

  // ----- project ---------------------------------------------------------------------------

  function applyProject(message) {
    files = message.files || [];

    // The component owns the marks, so the page adopts whatever it reports rather than keeping
    // its own idea of them across a reopen.
    if (typeof message.breakpointsEnabled === 'boolean') {
      breakpointsEnabled = message.breakpointsEnabled;
    }

    if (message.breakpoints) {
      breakpoints = {};
      Object.keys(message.breakpoints).forEach(function (file) {
        breakpoints[file] = (message.breakpoints[file] || []).slice();
      });
    }

    var known = {};

    files.forEach(function (file) {
      known[file.name] = true;

      if (models[file.name]) {
        if (models[file.name].getValue() !== file.content) {
          models[file.name].setValue(file.content);
        }
        return;
      }

      var language = file.language === 'xml' ? 'xml' : window.csharpLanguage.id;
      var model = monacoApi.editor.createModel(
        file.content, language, monacoApi.Uri.parse('inmemory://script/' + file.name));

      model.onDidChangeContent(function () {
        scheduleSave(file.name);
        scheduleDiagnose(file.name);
      });

      models[file.name] = model;
    });

    Object.keys(models).forEach(function (name) {
      if (known[name]) return;
      models[name].dispose();
      delete models[name];
      delete breakpoints[name];
    });

    var wanted = pendingActive && known[pendingActive] ? pendingActive
      : (active && known[active] ? active : (files[0] && files[0].name));

    pendingActive = null;
    open(wanted);
    renderFiles();

    Object.keys(breakpoints).forEach(renderBreakpoints);
    updateBreakpointStatus();

    Object.keys(models).forEach(diagnose);
  }

  function open(name) {
    if (!name || !models[name]) return;

    active = name;
    editor.setModel(models[name]);
    renderBreakpoints(name);
    editor.focus();
    renderFiles();
  }

  function scheduleSave(name) {
    if (saveTimer) clearTimeout(saveTimer);
    saveTimer = setTimeout(function () { save(name); }, 300);
  }

  function save(name) {
    saveTimer = null;
    if (!models[name]) return;

    send({ type: 'save', name: name, content: models[name].getValue() });
  }

  function flush() {
    if (saveTimer) clearTimeout(saveTimer);
    if (active) save(active);
  }

  function compile() {
    flush();
    log('Compiling ' + Object.keys(models).filter(isSource).join(', '));
    send({ type: 'compile' });
  }

  function isSource(name) {
    return name.slice(-3).toLowerCase() === '.cs';
  }

  el.compile.onclick = compile;
  el.run.onclick = function () { flush(); send({ type: 'run' }); };
  el.folder.onclick = function () { send({ type: 'openFolder' }); };

  el.references.onclick = openReferences;
  el['references-close'].onclick = closeReferences;

  el['references-panel'].addEventListener('mousedown', function (event) {
    if (event.target === el['references-panel']) closeReferences();
  });

  document.addEventListener('keydown', function (event) {
    if (event.key !== 'Escape') return;
    if (el['references-panel'].classList.contains('hidden')) return;

    closeReferences();
  });

  function openReferences() {
    el['references-panel'].classList.remove('hidden');
    note('');
    hideResults();

    loadReferences().then(function () { el['ref-search'].focus(); });
  }

  function closeReferences() {
    el['references-panel'].classList.add('hidden');
    hideResults();
  }

  function note(text, kind) {
    el['ref-note'].textContent = text || '';
    el['ref-note'].className = kind ? 'note ' + kind : 'note';
  }

  function setCount(id, value) {
    el[id].textContent = value ? String(value) : '';
  }

  function loadReferences() {
    return request('references', {}).then(function (data) {
      if (!data) return;

      renderPackages(data.packages || []);
      renderLocal(data.local || []);
      renderHost(data.host || []);
    });
  }

  // Every change rewrites Script.csproj, so the answer is either nothing or a reason.
  function changeReferences(type, payload) {
    return request(type, payload).then(function (answer) {
      if (answer && answer.error) {
        note(answer.error, 'bad');
        return;
      }

      note('Saved. Compile to pick it up.');
      return loadReferences();
    });
  }

  function dropButton(name, onClick) {
    var drop = document.createElement('button');
    drop.className = 'drop';
    drop.textContent = '×';
    drop.title = 'Remove ' + name;
    drop.onclick = onClick;

    return drop;
  }

  function spacer() {
    var element = document.createElement('span');
    element.className = 'grow';

    return element;
  }

  function empty(target, text) {
    var item = document.createElement('li');
    item.className = 'none';
    item.textContent = text;
    target.appendChild(item);
  }

  function renderPackages(rows) {
    el['ref-packages'].innerHTML = '';
    setCount('ref-packages-count', rows.length);

    if (!rows.length) {
      empty(el['ref-packages'], 'None. Search above to add one.');
      return;
    }

    rows.forEach(function (r) {
      var item = document.createElement('li');

      var name = document.createElement('span');
      name.className = 'name';
      name.textContent = r.id;

      item.appendChild(name);
      item.appendChild(spacer());
      item.appendChild(versionPicker(r));
      item.appendChild(dropButton(r.id, function () {
        changeReferences('removePackage', { name: r.id });
      }));

      el['ref-packages'].appendChild(item);
    });
  }

  // The path goes on its own line: sharing one with the name it wins every time, and the name,
  // which is what anybody actually reads, ends up squeezed.
  function renderLocal(rows) {
    el['ref-local'].innerHTML = '';
    setCount('ref-local-count', rows.length);

    if (!rows.length) {
      empty(el['ref-local'], 'None. Add a DLL to reference one by path.');
      return;
    }

    rows.forEach(function (r) {
      var entry = document.createElement('li');
      entry.className = 'entry';

      var head = document.createElement('div');
      head.className = 'row';

      var name = document.createElement('span');
      name.className = 'name';
      name.textContent = r.name;

      head.appendChild(name);
      head.appendChild(spacer());
      head.appendChild(dropButton(r.name, function () {
        changeReferences('removeLocalReference', { name: r.name });
      }));

      var path = document.createElement('div');
      path.className = r.missing ? 'path bad' : 'path';
      path.textContent = r.missing ? r.path + '  (not found)' : r.path;
      path.title = r.missing ? r.path : 'Show in Explorer';

      if (!r.missing) path.onclick = function () { send({ type: 'reveal', name: r.path }); };

      entry.appendChild(head);
      entry.appendChild(path);
      el['ref-local'].appendChild(entry);
    });
  }

  function renderHost(names) {
    setCount('ref-host-count', names.length);
    el['ref-host'].textContent = names.join('  ·  ');
  }

  el['ref-host-toggle'].onclick = function () {
    var hidden = el['ref-host'].classList.toggle('hidden');
    el['ref-host-toggle'].classList.toggle('open', !hidden);
  };

  // Starts with the version that is set and fetches the rest the first time it is opened, so a
  // window with ten packages does not make ten calls to nuget.org before anybody asks.
  function versionPicker(entry) {
    var select = document.createElement('select');
    var loaded = false;

    var current = document.createElement('option');
    current.value = entry.version;
    current.textContent = entry.version || '*';
    select.appendChild(current);

    select.title = 'Version';

    select.onmousedown = function () {
      if (loaded) return;
      loaded = true;

      request('packageVersions', { name: entry.id }).then(function (versions) {
        if (!versions || !versions.length) return;

        select.innerHTML = '';
        versions.forEach(function (v) {
          var option = document.createElement('option');
          option.value = v;
          option.textContent = v;
          option.selected = v === entry.version;
          select.appendChild(option);
        });
      });
    };

    select.onchange = function () {
      changeReferences('addPackage', { name: entry.id, content: select.value });
    };

    return select;
  }

  el['ref-add-local'].onclick = function () {
    changeReferences('addLocalReference', {});
  };

  // ----- searching nuget.org -----------------------------------------------------------------

  var searchTimer = null;

  el['ref-search'].oninput = function () {
    if (searchTimer) clearTimeout(searchTimer);

    var term = el['ref-search'].value.trim();
    if (term.length < 2) { hideResults(); note(''); return; }

    searchTimer = setTimeout(function () { searchPackages(term); }, 350);
  };

  el['ref-search'].onkeydown = function (event) {
    if (event.key === 'Escape') { hideResults(); event.stopPropagation(); return; }
    if (event.key !== 'Enter') return;

    var term = el['ref-search'].value.trim();
    if (term) addPackage(term, '');
  };

  function hideResults() {
    el['ref-results'].classList.add('hidden');
    el['ref-results'].innerHTML = '';
  }

  function addPackage(id, version) {
    return changeReferences('addPackage', { name: id, content: version }).then(function () {
      el['ref-search'].value = '';
      hideResults();
    });
  }

  function searchPackages(term) {
    note('Searching nuget.org...');

    request('searchPackages', { name: term }).then(function (hits) {
      // A slower answer for a term that has since been retyped must not overwrite a newer one.
      if (el['ref-search'].value.trim() !== term) return;

      if (!hits) {
        note('nuget.org could not be reached. Press Enter to add the id as typed.', 'bad');
        hideResults();
        return;
      }

      note('');
      renderResults(term, hits);
    });
  }

  function renderResults(term, hits) {
    el['ref-results'].innerHTML = '';
    el['ref-results'].classList.remove('hidden');

    hits.forEach(function (hit) {
      var item = document.createElement('li');
      item.title = 'Add ' + hit.id + ' ' + hit.version;

      var id = document.createElement('span');
      id.className = 'id';
      id.textContent = hit.id;

      var version = document.createElement('span');
      version.className = 'version';
      version.textContent = hit.version;

      var blurb = document.createElement('span');
      blurb.className = 'blurb';
      blurb.textContent = hit.description || '';

      item.appendChild(id);
      item.appendChild(version);
      item.appendChild(blurb);
      item.onclick = function () { addPackage(hit.id, hit.version); };

      el['ref-results'].appendChild(item);
    });

    // Not being on nuget.org is not the last word: a private feed may still know the id.
    var exact = hits.some(function (hit) { return hit.id.toLowerCase() === term.toLowerCase(); });
    if (exact) return;

    var literal = document.createElement('li');
    literal.className = 'literal';
    literal.textContent = hits.length
      ? 'Add ' + term + ' as typed, any version'
      : 'Nothing matches. Add ' + term + ' as typed, any version';

    literal.onclick = function () { addPackage(term, ''); };
    el['ref-results'].appendChild(literal);
  }

  // ----- restoring and the project file -------------------------------------------------------

  el['ref-restore'].onclick = function () {
    note('Restoring...');

    request('restore', {}).then(function (answer) {
      if (!answer) { note('The restore did not answer.', 'bad'); return; }

      var bad = (answer.problems || []).filter(function (p) { return p.severity === 'error'; });

      if (bad.length) { note(bad[0].message, 'bad'); return; }
      note((answer.names || []).length + ' references resolved.', 'good');
    });
  };

  el['ref-edit-csproj'].onclick = function () {
    closeReferences();
    open('Script.csproj');
  };

  // ----- sidebar ---------------------------------------------------------------------------

  function renderFiles() {
    el.files.innerHTML = '';

    files.forEach(function (file) {
      var item = document.createElement('li');
      item.className = file.name === active ? 'active' : '';

      var kind = document.createElement('span');
      kind.className = 'kind';
      kind.innerHTML = isSource(file.name)
        ? '<svg viewBox="0 0 24 24" width="14" height="14" fill="none" stroke="currentColor" stroke-linecap="round"><use href="#icSharp"></use></svg>'
        : '.';

      var label = document.createElement('span');
      label.className = 'label';
      label.textContent = file.name;

      item.appendChild(kind);
      item.appendChild(label);
      item.onclick = function () { open(file.name); };

      if (!file.locked) {
        label.ondblclick = function (event) {
          event.stopPropagation();
          renameInline(item, file.name);
        };

        var remove = document.createElement('span');
        remove.className = 'x';
        remove.textContent = '×';
        remove.title = 'Delete ' + file.name;
        remove.onclick = function (event) {
          event.stopPropagation();
          if (!window.confirm('Delete ' + file.name + '?')) return;
          send({ type: 'deleteFile', name: file.name });
        };

        item.appendChild(remove);
      }

      el.files.appendChild(item);
    });
  }

  function renameInline(item, name) {
    var input = document.createElement('input');
    input.value = name;

    item.innerHTML = '';
    item.appendChild(input);
    input.focus();
    input.select();

    var done = false;

    function finish(commit) {
      if (done) return;
      done = true;

      var value = input.value.trim();
      if (commit && value && value !== name) {
        pendingActive = value;
        send({ type: 'renameFile', from: name, to: value });
      } else {
        renderFiles();
      }
    }

    input.onblur = function () { finish(true); };
    input.onkeydown = function (event) {
      if (event.key === 'Enter') finish(true);
      if (event.key === 'Escape') finish(false);
    };
  }

  el.add.onclick = function () {
    var item = document.createElement('li');
    var input = document.createElement('input');
    input.placeholder = 'Name.cs';

    item.appendChild(input);
    el.files.appendChild(item);
    input.focus();

    var done = false;

    function finish(commit) {
      if (done) return;
      done = true;

      var value = input.value.trim();
      if (commit && value) {
        pendingActive = value.indexOf('.') < 0 ? value + '.cs' : value;
        send({ type: 'addFile', name: value });
      } else {
        renderFiles();
      }
    }

    input.onblur = function () { finish(true); };
    input.onkeydown = function (event) {
      if (event.key === 'Enter') finish(true);
      if (event.key === 'Escape') finish(false);
    };
  };

  // ----- state -----------------------------------------------------------------------------

  function applyState(message) {
    el.compile.disabled = !!message.compiling;
    el.run.disabled = !!message.compiling;

    if (message.title) {
      el.title.innerHTML = '';

      var document_ = document.createElement('span');
      document_.textContent = message.title;

      var separator = document.createElement('span');
      separator.className = 'sep';
      separator.textContent = ' — ';

      var name = document.createElement('span');
      name.className = 'name';
      name.textContent = 'C# Script';

      el.title.appendChild(document_);
      el.title.appendChild(separator);
      el.title.appendChild(name);
    }

    if (message.compiling) {
      el['runtime-dot'].className = 'dot busy';
      el['status-build'].textContent = 'Compiling';
    } else if (message.stale) {
      el['runtime-dot'].className = 'dot busy';
      el['status-build'].textContent = 'Modified since last compile';
    } else {
      el['runtime-dot'].className = 'dot';
      if (message.build) el['status-build'].textContent = message.build;
    }

    if (message.runtime) el.runtime.textContent = message.runtime;

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

    el['references-label'].textContent = message.references
      ? 'References (' + message.references + ')'
      : 'References';

    renderParams(el.inputs, message.parameters && message.parameters.inputs);
    renderParams(el.outputs, message.parameters && message.parameters.outputs);
  }

  function renderParams(target, rows) {
    target.innerHTML = '';

    if (!rows || !rows.length) {
      var empty = document.createElement('div');
      empty.className = 'empty';
      empty.textContent = 'none';
      target.appendChild(empty);
      return;
    }

    rows.forEach(function (row) {
      var line = document.createElement('div');
      line.className = 'row';

      var name = document.createElement('span');
      name.textContent = row.name;

      var type = document.createElement('span');
      type.className = 'type';
      type.textContent = row.access === 'item' ? row.type : row.type + ' [' + row.access + ']';

      line.appendChild(name);
      line.appendChild(type);
      target.appendChild(line);
    });
  }

  function applyDiagnostics(items) {
    Object.keys(models).forEach(function (name) {
      if (models[name].getLanguageId() === window.csharpLanguage.id) return;

      var forFile = items.filter(function (item) { return item.file === name; });
      monacoApi.editor.setModelMarkers(models[name], 'compile', forFile.map(marker));
    });

    renderProblems(items);
  }

  function renderProblems(items) {
    el['view-problems'].innerHTML = '';

    var errors = items.filter(function (i) { return i.severity === 'error'; }).length;
    var warnings = items.length - errors;

    el['problem-count'].textContent = String(items.length);
    el['problem-count'].className = items.length === 0
      ? 'badge hidden'
      : (errors > 0 ? 'badge error' : 'badge');

    if (items.length === 0) {
      var none = document.createElement('li');
      none.className = 'none';
      none.textContent = 'No problems.';
      el['view-problems'].appendChild(none);
      return;
    }

    if (errors > 0) showTab('problems');

    items.forEach(function (item) {
      var row = document.createElement('li');
      row.className = item.severity;

      var mark = document.createElement('span');
      mark.className = 'mark';
      mark.innerHTML = '<svg viewBox="0 0 24 24" width="14" height="14" fill="none" stroke="currentColor" stroke-linecap="round"><use href="#icErr"></use></svg>';

      var body = document.createElement('div');

      var message = document.createElement('div');
      message.className = 'message';
      message.textContent = item.message;

      var where = document.createElement('div');
      where.className = 'where';
      where.textContent = item.file + ' · ' + item.line + ':' + item.column +
        ' · ' + item.id + ' · ' + item.severity;

      body.appendChild(message);
      body.appendChild(where);
      row.appendChild(mark);
      row.appendChild(body);

      row.onclick = function () {
        if (!models[item.file]) return;
        open(item.file);
        editor.revealLineInCenter(item.line);
        editor.setPosition({ lineNumber: item.line, column: item.column });
        editor.focus();
      };

      el['view-problems'].appendChild(row);
    });
  }

  applyLayout();
  showTab('output');
  el['panel-wrap'].classList.add('on');
})();
