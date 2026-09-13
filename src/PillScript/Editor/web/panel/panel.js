// Draws what the published components declare and reports every change back. The host owns when a
// change becomes a solve; this only says what was touched.
//
// A payload arrives after every solve, so redrawing the lot each time would take the control out
// from under the pointer halfway through a drag. Instead the page keeps a patch for each control
// and only rebuilds when the shape of the payload changes: a component published or gone, a
// control added, a kind changed.
(function () {
  'use strict';

  var SP = window.SP;
  var host = window.chrome && window.chrome.webview;

  var sections = document.getElementById('sections');
  var author = document.getElementById('author');
  var count = document.getElementById('count');
  var lamp = document.getElementById('lamp');
  var solved = document.getElementById('solved');
  var documentName = document.getElementById('document');

  var state = { layers: [], shape: null, patches: {}, shut: {} };

  function say(message) { if (host) host.postMessage(message); }

  function setter(component) {
    return function (name, value) {
      say({ type: 'set', component: component, name: name, value: value });
    };
  }

  function asker(component) {
    return function (kind, name, value) {
      say({ type: kind, component: component, name: name, value: value });
    };
  }

  /// What the page has to be rebuilt for. Values are deliberately not part of it.
  function shapeOf(payload) {
    return (payload.sections || []).map(function (section) {
      return section.component + ':' + (section.widgets || []).map(function (w) {
        return w.kind + '.' + w.name;
      }).join(',') + '|' + (section.problem || '');
    }).join(';');
  }

  function head(section) {
    var node = SP.el('div', 'head');

    var chevron = SP.glyph('M9 6l6 6-6 6');
    chevron.setAttribute('class', 'chevron');
    node.appendChild(chevron);

    var pill = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
    pill.setAttribute('viewBox', '0 0 24 24');
    pill.setAttribute('class', 'glyph');
    var use = document.createElementNS('http://www.w3.org/2000/svg', 'use');
    use.setAttribute('href', '#icPill');
    pill.appendChild(use);
    node.appendChild(pill);

    node.appendChild(SP.el('span', 'title', section.title || 'Script'));
    node.appendChild(SP.el('div', 'spacer'));
    node.appendChild(SP.el('span', 'mark mono', section.mark || ''));

    return node;
  }

  function problem(text) {
    var node = SP.el('div', 'problem');
    node.appendChild(SP.glyph('M12 5v9M12 17.6v.4', 1.8));
    node.appendChild(SP.el('span', null, text));
    return node;
  }

  /// Builds one section, and files a patch under component/name for every control in it.
  function build(section) {
    var block = SP.el('div', 'section');
    block.dataset.component = section.component;
    if (state.shut[section.component]) block.classList.add('shut');

    var bar = head(section);
    bar.onclick = function () {
      // The heading is also what a section is dragged by, so a press that moved is not a click.
      if (sections.dataset.dragged) {
        delete sections.dataset.dragged;
        return;
      }

      var shut = !block.classList.contains('shut');
      block.classList.toggle('shut', shut);
      state.shut[section.component] = shut;
      say({ type: 'collapse', component: section.component, value: shut });
    };

    block.appendChild(bar);

    var body = SP.el('div', 'body');
    var send = setter(section.component);
    var ask = asker(section.component);
    var widgets = section.widgets || [];

    for (var i = 0; i < widgets.length; i++) {
      var widget = widgets[i];

      // A run of buttons shares one row.
      if (widget.kind === 'button') {
        var run = [];
        while (i < widgets.length && widgets[i].kind === 'button') run.push(widgets[i++]);
        i--;

        body.appendChild(SP.buttons(run, send).node);
        continue;
      }

      var builder = SP.builders[widget.kind];
      if (!builder) continue;

      var made = builder(widget, send, ask, state);
      body.appendChild(made.node);

      if (widget.name) state.patches[section.component + '/' + widget.name] = made;
    }

    if (section.problem) body.appendChild(problem(section.problem));

    block.appendChild(body);
    return block;
  }

  function render(payload) {
    sections.innerHTML = '';
    state.patches = {};

    var list = payload.sections || [];

    if (!list.length) {
      sections.appendChild(SP.el('div', 'empty',
        'Nothing published. A script component offers Publish to panel in its menu.'));
      return;
    }

    list.forEach(function (section) { sections.appendChild(build(section)); });
  }

  /// The same payload against a page that already has the right controls on it. A control the
  /// pointer or the caret is in is left alone: it already shows what the host is being told.
  function patch(payload) {
    (payload.sections || []).forEach(function (section) {
      (section.widgets || []).forEach(function (widget) {
        var made = state.patches[section.component + '/' + widget.name];
        if (!made || (made.busy && made.busy())) return;

        made.patch(widget.value);
      });
    });
  }

  function fold(payload) {
    (payload.sections || []).forEach(function (section) {
      var block = sections.querySelector('.section[data-component="' + section.component + '"]');
      if (block) block.classList.toggle('shut', !!section.collapsed);
    });
  }

  function chrome(payload) {
    count.textContent = payload.count === 1 ? '1 script' : payload.count + ' scripts';
    documentName.textContent = payload.document || '';

    var status = payload.status || { milliseconds: 0, failed: false };
    lamp.classList.toggle('failed', !!status.failed);

    solved.textContent = status.milliseconds
      ? 'solved ' + (status.milliseconds < 10
          ? status.milliseconds.toFixed(1)
          : Math.round(status.milliseconds)) + ' ms'
      : 'not solved yet';
  }

  function arrived(payload) {
    state.layers = payload.layers || state.layers;

    // Last sheet in the document, so the author's rules win without needing !important.
    author.textContent = payload.css || '';

    (payload.sections || []).forEach(function (section) {
      state.shut[section.component] = !!section.collapsed;
    });

    var shape = shapeOf(payload);

    if (shape !== state.shape) {
      state.shape = shape;
      render(payload);
    } else {
      patch(payload);
    }

    // Rolled up or down says nothing about the shape, so a section that changed while the page
    // was only patching would otherwise keep the state it was drawn with.
    fold(payload);
    chrome(payload);
  }

  SP.reorder(sections, say);

  // Rolls everything up, unless everything is up already, in which case it rolls it back down.
  document.getElementById('fold').onclick = function () {
    var blocks = sections.querySelectorAll('.section');
    var shut = Array.prototype.some.call(blocks, function (b) { return !b.classList.contains('shut'); });

    Array.prototype.forEach.call(blocks, function (block) {
      var component = block.dataset.component;

      block.classList.toggle('shut', shut);
      state.shut[component] = shut;
      say({ type: 'collapse', component: component, value: shut });
    });
  };

  if (!host) {
    // Opened in a browser, for work on the page itself.
    arrived({
      count: 2, document: 'sample.gh', status: { milliseconds: 14, failed: false },
      layers: ['Default', 'Default::Solids'],
      sections: [
        {
          component: 'sample-a', title: 'Outline', mark: '1A5C', widgets: [
            { kind: 'slider', name: 'radius', minimum: 1, maximum: 50, value: 12 },
            { kind: 'number', name: 'corners', step: 1, value: 6 },
            { kind: 'choice', name: 'style', options: ['polygon', 'circle', 'rect'], value: 'polygon' },
            { kind: 'text', name: 'name', value: 'outline_a' },
            { kind: 'button', name: 'bake', label: 'Bake to Rhino' },
            { kind: 'button', name: 'reset', label: 'Reset', quiet: true }
          ]
        },
        {
          component: 'sample-b', title: 'Extrusion', mark: 'FC5D',
          problem: 'height exceeds layer clipping — geometry will be trimmed on bake',
          widgets: [
            { kind: 'slider', name: 'height', minimum: 0, maximum: 100, value: 93.32 },
            { kind: 'toggle', name: 'capped', value: false, note: 'close both ends' },
            { kind: 'vector', name: 'direction', value: { x: 0, y: 0, z: 1 } },
            { kind: 'colour', name: 'material', value: '#C9A227' },
            { kind: 'layer', name: 'layer', value: 'Default::Solids' }
          ]
        }
      ]
    });
    return;
  }

  host.addEventListener('message', function (event) {
    if (!event.data || event.data.type !== 'published') return;
    arrived(event.data);
  });

  say({ type: 'ready' });
})();
