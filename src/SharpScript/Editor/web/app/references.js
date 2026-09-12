// The references window: the packages and DLLs the script is built against, and what Rhino
// already provides. Searching nuget.org for something new is in nuget.js.
(function () {
  'use strict';

  var SS = window.SS;
  var el = SS.el;

  el.references.onclick = openWindow;
  el['references-close'].onclick = closeWindow;

  el['references-panel'].addEventListener('mousedown', function (event) {
    if (event.target === el['references-panel']) closeWindow();
  });

  document.addEventListener('keydown', function (event) {
    if (event.key !== 'Escape') return;
    if (el['references-panel'].classList.contains('hidden')) return;

    closeWindow();
  });

  function openWindow() {
    el['references-panel'].classList.remove('hidden');
    note('');
    SS.nuget.hideResults();

    load().then(function () { el['ref-search'].focus(); });
  }

  function closeWindow() {
    el['references-panel'].classList.add('hidden');
    SS.nuget.hideResults();
  }

  // The search half of the window lives in nuget.js and needs these two.
  SS.refs = { note: note, change: change };

  function note(text, kind) {
    el['ref-note'].textContent = text || '';
    el['ref-note'].className = kind ? 'note ' + kind : 'note';
  }

  function setCount(id, value) {
    el[id].textContent = value ? String(value) : '';
  }

  function load() {
    return SS.request('references', {}).then(function (data) {
      if (!data) return;

      renderPackages(data.packages || []);
      renderLocal(data.local || []);
      renderHost(data.host || []);
    });
  }

  // Every change rewrites Script.csproj, so the answer is either nothing or a reason.
  function change(type, payload) {
    return SS.request(type, payload).then(function (answer) {
      if (answer && answer.error) {
        note(answer.error, 'bad');
        return;
      }

      note('Saved. Compile to pick it up.');
      return load();
    });
  }

  // ----- the lists ------------------------------------------------------------------------------

  function dropButton(name, onClick) {
    var drop = document.createElement('button');

    drop.className = 'drop';
    drop.textContent = '×';
    drop.title = 'Remove ' + name;
    drop.onclick = onClick;

    return drop;
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

      item.appendChild(SS.span('name', r.id));
      item.appendChild(SS.span('grow'));
      item.appendChild(versionPicker(r));
      item.appendChild(dropButton(r.id, function () {
        change('removePackage', { name: r.id });
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

      var head = SS.div('row');
      head.appendChild(SS.span('name', r.name));
      head.appendChild(SS.span('grow'));
      head.appendChild(dropButton(r.name, function () {
        change('removeLocalReference', { name: r.name });
      }));

      var path = SS.div(r.missing ? 'path bad' : 'path',
        r.missing ? r.path + '  (not found)' : r.path);

      path.title = r.missing ? r.path : 'Show in Explorer';
      if (!r.missing) path.onclick = function () { SS.send({ type: 'reveal', name: r.path }); };

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

      SS.request('packageVersions', { name: entry.id }).then(function (versions) {
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
      change('addPackage', { name: entry.id, content: select.value });
    };

    return select;
  }

  el['ref-add-local'].onclick = function () { change('addLocalReference', {}); };

  // ----- restoring and the project file ------------------------------------------------------------

  el['ref-restore'].onclick = function () {
    note('Restoring...');

    SS.request('restore', {}).then(function (answer) {
      if (!answer) { note('The restore did not answer.', 'bad'); return; }

      var bad = (answer.problems || []).filter(function (p) { return p.severity === 'error'; });

      if (bad.length) { note(bad[0].message, 'bad'); return; }
      note((answer.names || []).length + ' references resolved.', 'good');
    });
  };

  el['ref-edit-csproj'].onclick = function () {
    closeWindow();
    SS.open('Script.csproj');
  };
})();
