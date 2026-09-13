// The pieces every control is built from, and the controls that carry a number. Each builder
// returns a node and a patch. The patch takes a new value and shows it without rebuilding, so a
// solve arriving mid-drag does not pull the control out from under the pointer. Nothing here talks
// to the host: panel.js passes each builder the function to send with.
(function () {
  'use strict';

  var SP = window.SP = window.SP || {};

  SP.el = function (tag, className, text) {
    var node = document.createElement(tag);
    if (className) node.className = className;
    if (text !== undefined && text !== null) node.textContent = text;
    return node;
  };

  SP.glyph = function (d, width) {
    var svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
    svg.setAttribute('viewBox', '0 0 24 24');
    svg.setAttribute('fill', 'none');
    svg.setAttribute('stroke', 'currentColor');
    svg.setAttribute('stroke-width', width || 2);
    svg.setAttribute('stroke-linecap', 'round');

    var path = document.createElementNS('http://www.w3.org/2000/svg', 'path');
    path.setAttribute('d', d);
    svg.appendChild(path);
    return svg;
  };

  // A row: the label column, the control, and for numbers the reading on the right.
  SP.row = function (w, control, reading) {
    var node = SP.el('div', reading ? 'widget measured' : 'widget');
    node.appendChild(SP.el('label', null, w.label || w.name));
    node.appendChild(control);
    if (reading) node.appendChild(reading);
    return node;
  };

  SP.shown = function (value, step) {
    return step >= 1 ? String(Math.round(value)) : Number(value).toFixed(2);
  };

  SP.field = function (cls) {
    var box = SP.el('div', 'field' + (cls ? ' ' + cls : ''));
    var input = document.createElement('input');
    input.type = 'text';
    input.spellcheck = false;
    box.appendChild(input);
    return { box: box, input: input };
  };

  SP.number = function (value, fallback) {
    var parsed = parseFloat(String(value).replace(',', '.'));
    return isFinite(parsed) ? parsed : fallback;
  };

  SP.builders = {
    slider: function (w, send) {
      var track = SP.el('div', 'track');
      var fill = SP.el('div', 'fill');
      var grip = SP.el('div', 'grip');
      track.appendChild(fill);
      track.appendChild(grip);

      var slider = SP.el('div', 'slider');
      slider.appendChild(track);

      var reading = SP.el('div', 'reading');
      var span = (w.maximum - w.minimum) || 1;
      var step = w.step || 0;
      var held = SP.number(w.value, w.minimum);

      function show(value) {
        var part = Math.min(1, Math.max(0, (value - w.minimum) / span));
        fill.style.width = (part * 100) + '%';
        grip.style.left = (part * 100) + '%';
        reading.textContent = SP.shown(value, step);
      }

      function at(event) {
        var box = track.getBoundingClientRect();
        var part = box.width ? (event.clientX - box.left) / box.width : 0;
        var value = w.minimum + Math.min(1, Math.max(0, part)) * span;
        return step ? Math.round(value / step) * step : value;
      }

      slider.onpointerdown = function (event) {
        slider.setPointerCapture(event.pointerId);
        slider.dataset.busy = '1';
        held = at(event);
        show(held);
        send(w.name, held);
      };

      slider.onpointermove = function (event) {
        if (!slider.dataset.busy) return;
        held = at(event);
        show(held);
        send(w.name, held);
      };

      slider.onpointerup = function () { delete slider.dataset.busy; };

      show(held);

      return {
        node: SP.row(w, slider, reading),
        busy: function () { return !!slider.dataset.busy; },
        patch: function (value) { show(SP.number(value, held)); }
      };
    },

    number: function (w, send) {
      var made = SP.field();
      made.input.value = SP.shown(SP.number(w.value, 0), w.step || 0);

      var step = w.step || 1;

      function commit(value) {
        made.input.value = SP.shown(value, w.step || 0);
        send(w.name, value);
      }

      made.input.onchange = function () { commit(SP.number(made.input.value, 0)); };

      var spin = SP.el('div', 'spin');
      var up = SP.el('div');
      up.appendChild(SP.glyph('M6 15l6-6 6 6', 2.4));
      var down = SP.el('div');
      down.appendChild(SP.glyph('M6 9l6 6 6-6', 2.4));

      up.onclick = function () { commit(SP.number(made.input.value, 0) + step); };
      down.onclick = function () { commit(SP.number(made.input.value, 0) - step); };

      spin.appendChild(up);
      spin.appendChild(down);
      made.box.appendChild(spin);

      return {
        node: SP.row(w, made.box),
        busy: function () { return document.activeElement === made.input; },
        patch: function (value) { made.input.value = SP.shown(SP.number(value, 0), w.step || 0); }
      };
    },

    vector: function (w, send) {
      var trio = SP.el('div', 'trio');
      var value = w.value || { x: 0, y: 0, z: 0 };
      var inputs = {};

      ['x', 'y', 'z'].forEach(function (axis) {
        var made = SP.field(axis);
        made.input.value = Number(value[axis] || 0).toFixed(1);
        made.box.insertBefore(SP.el('span', 'prefix', axis), made.input);

        made.input.onchange = function () {
          send(w.name, {
            x: SP.number(inputs.x.value, 0),
            y: SP.number(inputs.y.value, 0),
            z: SP.number(inputs.z.value, 0)
          });
        };

        inputs[axis] = made.input;
        trio.appendChild(made.box);
      });

      return {
        node: SP.row(w, trio),
        busy: function () {
          return ['x', 'y', 'z'].some(function (a) { return document.activeElement === inputs[a]; });
        },
        patch: function (fresh) {
          if (!fresh) return;
          ['x', 'y', 'z'].forEach(function (a) { inputs[a].value = Number(fresh[a] || 0).toFixed(1); });
        }
      };
    },

    text: function (w, send) {
      var made = SP.field();
      made.input.value = w.value === null || w.value === undefined ? '' : String(w.value);
      made.input.onchange = function () { send(w.name, made.input.value); };

      return {
        node: SP.row(w, made.box),
        busy: function () { return document.activeElement === made.input; },
        patch: function (value) { made.input.value = value === null ? '' : String(value); }
      };
    }
  };
})();
