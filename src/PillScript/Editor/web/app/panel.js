// The pane along the bottom: what the script printed, the problems reported against it, and the
// values its variables held when it stopped.
(function () {
  'use strict';

  var SS = window.SS;
  var el = SS.el;

  var tabs = ['output', 'problems', 'variables'];
  var wrapLog = true;
  var wrapCode = false;

  SS.showTab = function (name) {
    tabs.forEach(function (tab) {
      el['tab-' + tab].classList.toggle('on', tab === name);
      el['view-' + tab].classList.toggle('on', tab === name);
    });
  };

  tabs.forEach(function (tab) {
    el['tab-' + tab].onclick = function () { SS.showTab(tab); };
  });

  el['panel-clear'].onclick = function () { el['view-output'].innerHTML = ''; };

  el['panel-wrap'].onclick = function () {
    wrapLog = !wrapLog;

    el['panel-wrap'].classList.toggle('on', wrapLog);
    el.panel.classList.toggle('nowrap', !wrapLog);
  };

  el['wrap-code'].onclick = function () {
    wrapCode = !wrapCode;

    el['wrap-code'].classList.toggle('on', wrapCode);
    SS.state.editor.updateOptions({ wordWrap: wrapCode ? 'on' : 'off' });
  };

  // ----- output ------------------------------------------------------------------------------

  SS.log = function (text, kind) {
    if (!text) return;

    var now = new Date();
    var stamp = [now.getHours(), now.getMinutes(), now.getSeconds()]
      .map(function (part) { return part < 10 ? '0' + part : String(part); })
      .join(':');

    text.split('\n').forEach(function (part) {
      var line = SS.div('line');

      line.appendChild(SS.span('time', stamp));
      line.appendChild(SS.span('text' + (kind ? ' ' + kind : ''), part));

      el['view-output'].appendChild(line);
    });

    el['view-output'].scrollTop = el['view-output'].scrollHeight;
  };

  SS.handlers.log = function (message) { SS.log(message.text, message.kind); };

  // ----- variables ----------------------------------------------------------------------------

  SS.renderVariables = function (rows, where) {
    el['view-variables'].innerHTML = '';
    el['view-variables'].appendChild(SS.div('frame', where || ''));

    if (!rows.length) {
      el['view-variables'].appendChild(SS.div('none', 'No locals in scope.'));
      return;
    }

    rows.forEach(function (row) {
      var line = SS.div('row');

      line.appendChild(SS.span('name', row.name));
      line.appendChild(SS.span('type', row.type));
      line.appendChild(SS.span('value', row.value));

      el['view-variables'].appendChild(line);
    });
  };

  el.inspect.onclick = function () { SS.showTab('variables'); };

  // ----- problems -----------------------------------------------------------------------------

  SS.renderProblems = function (items) {
    el['view-problems'].innerHTML = '';

    var errors = items.filter(function (i) { return i.severity === 'error'; }).length;

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

    // An error opens the pane. A warning waits until the pane is opened.
    if (errors > 0) SS.showTab('problems');

    items.forEach(function (item) { el['view-problems'].appendChild(problem(item)); });
  };

  function problem(item) {
    var row = document.createElement('li');
    row.className = item.severity;

    var mark = SS.span('mark');
    mark.innerHTML = '<svg viewBox="0 0 24 24" width="14" height="14" fill="none" ' +
      'stroke="currentColor" stroke-linecap="round"><use href="#icErr"></use></svg>';

    var body = SS.div();
    body.appendChild(SS.div('message', item.message));
    body.appendChild(SS.div('where', item.file + ' · ' + item.line + ':' + item.column +
      ' · ' + item.id + ' · ' + item.severity));

    row.appendChild(mark);
    row.appendChild(body);

    row.onclick = function () {
      if (!SS.state.models[item.file]) return;

      SS.open(item.file);
      SS.state.editor.revealLineInCenter(item.line);
      SS.state.editor.setPosition({ lineNumber: item.line, column: item.column });
      SS.state.editor.focus();
    };

    return row;
  }

  SS.showTab('output');
  el['panel-wrap'].classList.add('on');
})();
