// The files of one script: the models behind them, the list in the sidebar, and saving what was
// typed back to the component.
(function () {
  'use strict';

  var SS = window.SS;
  var el = SS.el;
  var state = SS.state;

  // An edit is saved shortly after typing stops, not on every keystroke.
  var SAVE_DELAY = 300;

  var saveTimer = null;

  SS.applyProject = function (message) {
    state.files = message.files || [];

    // The component owns the marks, so the page adopts whatever it reports rather than keeping
    // its own idea of them across a reopen.
    if (typeof message.breakpointsEnabled === 'boolean') {
      state.breakpointsEnabled = message.breakpointsEnabled;
    }

    if (message.breakpoints) {
      state.breakpoints = {};

      Object.keys(message.breakpoints).forEach(function (file) {
        state.breakpoints[file] = (message.breakpoints[file] || []).slice();
      });
    }

    var known = {};

    state.files.forEach(function (file) {
      known[file.name] = true;
      adopt(file);
    });

    Object.keys(state.models).forEach(function (name) {
      if (known[name]) return;

      state.models[name].dispose();
      delete state.models[name];
      delete state.breakpoints[name];
    });

    var wanted = state.pendingActive && known[state.pendingActive]
      ? state.pendingActive
      : (state.active && known[state.active] ? state.active : (state.files[0] && state.files[0].name));

    state.pendingActive = null;

    SS.open(wanted);
    SS.renderFiles();

    Object.keys(state.breakpoints).forEach(SS.renderBreakpoints);
    SS.updateBreakpointStatus();

    Object.keys(state.models).forEach(SS.diagnose);
  };

  /// Keeps an existing model where there is one, so undo history and folding survive a refresh.
  function adopt(file) {
    var existing = state.models[file.name];

    if (existing) {
      if (existing.getValue() !== file.content) existing.setValue(file.content);
      return;
    }

    var language = file.language === 'xml' ? 'xml' : SS.csharpId();

    var model = state.monaco.editor.createModel(
      file.content, language, state.monaco.Uri.parse('inmemory://script/' + file.name));

    model.onDidChangeContent(function () {
      scheduleSave(file.name);
      SS.scheduleDiagnose(file.name);
    });

    state.models[file.name] = model;
  }

  SS.handlers.project = SS.applyProject;

  SS.open = function (name) {
    if (!name || !state.models[name]) return;

    state.active = name;

    state.editor.setModel(state.models[name]);
    state.editor.focus();

    SS.renderBreakpoints(name);
    SS.renderFiles();
  };

  // ----- saving, compiling, running -------------------------------------------------------------

  function scheduleSave(name) {
    if (saveTimer) clearTimeout(saveTimer);
    saveTimer = setTimeout(function () { save(name); }, SAVE_DELAY);
  }

  function save(name) {
    saveTimer = null;
    if (!state.models[name]) return;

    SS.send({ type: 'save', name: name, content: state.models[name].getValue() });
  }

  /// Saves what is waiting to be saved, so a compile never builds yesterday's text.
  SS.flush = function () {
    if (saveTimer) clearTimeout(saveTimer);
    if (state.active) save(state.active);
  };

  SS.compile = function () {
    SS.flush();
    SS.log('Compiling ' + Object.keys(state.models).filter(SS.isSource).join(', '));
    SS.send({ type: 'compile' });
  };

  el.compile.onclick = SS.compile;
  el.run.onclick = function () { SS.flush(); SS.send({ type: 'run' }); };
  el.folder.onclick = function () { SS.send({ type: 'openFolder' }); };

  // ----- the list in the sidebar ------------------------------------------------------------------

  SS.renderFiles = function () {
    el.files.innerHTML = '';

    state.files.forEach(function (file) {
      el.files.appendChild(entry(file));
    });
  };

  function entry(file) {
    var item = document.createElement('li');
    item.className = file.name === state.active ? 'active' : '';

    var kind = SS.span('kind');
    kind.innerHTML = SS.isSource(file.name)
      ? '<svg viewBox="0 0 24 24" width="14" height="14" fill="none" stroke="currentColor" ' +
        'stroke-linecap="round"><use href="#icSharp"></use></svg>'
      : '.';

    var label = SS.span('label', file.name);

    item.appendChild(kind);
    item.appendChild(label);
    item.onclick = function () { SS.open(file.name); };

    // Script.cs, the project file and the global usings are what the build needs, so they cannot
    // be renamed or deleted and are not offered as if they could.
    if (file.locked) return item;

    label.ondblclick = function (event) {
      event.stopPropagation();
      rename(item, file.name);
    };

    var remove = SS.span('x', '×');
    remove.title = 'Delete ' + file.name;
    remove.onclick = function (event) {
      event.stopPropagation();
      if (!window.confirm('Delete ' + file.name + '?')) return;

      SS.send({ type: 'deleteFile', name: file.name });
    };

    item.appendChild(remove);
    return item;
  }

  function rename(item, name) {
    var input = document.createElement('input');
    input.value = name;

    item.innerHTML = '';
    item.appendChild(input);

    input.focus();
    input.select();

    edit(input, function (value) {
      if (!value || value === name) return false;

      state.pendingActive = value;
      SS.send({ type: 'renameFile', from: name, to: value });

      return true;
    });
  }

  el.add.onclick = function () {
    var item = document.createElement('li');
    var input = document.createElement('input');
    input.placeholder = 'Name.cs';

    item.appendChild(input);
    el.files.appendChild(item);
    input.focus();

    edit(input, function (value) {
      if (!value) return false;

      state.pendingActive = value.indexOf('.') < 0 ? value + '.cs' : value;
      SS.send({ type: 'addFile', name: value });

      return true;
    });
  };

  /// Runs an inline text box once: Enter or losing focus commits, Escape puts the list back.
  function edit(input, commit) {
    var done = false;

    function finish(keep) {
      if (done) return;
      done = true;

      if (!keep || !commit(input.value.trim())) SS.renderFiles();
    }

    input.onblur = function () { finish(true); };
    input.onkeydown = function (event) {
      if (event.key === 'Enter') finish(true);
      if (event.key === 'Escape') finish(false);
    };
  }
})();
