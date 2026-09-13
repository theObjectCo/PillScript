// The controls that pick something rather than carry a number, and the row a run of buttons
// shares. They follow the same contract as the ones in controls.js: a node and a patch.
(function () {
  'use strict';

  var SP = window.SP;

  SP.builders.toggle = function (w, send) {
    var check = SP.el('div', 'check');
    var box = SP.el('div', 'box');
    box.appendChild(SP.glyph('M5 12.5l4.5 4.5L19 7.5', 2.6));
    check.appendChild(box);
    if (w.note) check.appendChild(SP.el('span', 'note', w.note));

    var held = !!w.value;

    function show(value) { check.classList.toggle('on', !!value); }

    check.onclick = function () {
      held = !held;
      show(held);
      send(w.name, held);
    };

    show(held);

    return {
      node: SP.row(w, check),
      patch: function (value) { held = !!value; show(held); }
    };
  };

  // Up to three short options are worth showing all at once; anything longer is a list.
  SP.builders.choice = function (w, send) {
    var options = w.options || [];

    if (options.length <= 3 && options.join('').length <= 24) {
      var bar = SP.el('div', 'segmented');

      var show = function (value) {
        cells.forEach(function (cell, i) { cell.classList.toggle('on', options[i] === value); });
      };

      var cells = options.map(function (option) {
        var cell = SP.el('div', null, option);
        cell.onclick = function () { send(w.name, option); show(option); };
        bar.appendChild(cell);
        return cell;
      });

      show(w.value);
      return { node: SP.row(w, bar), patch: show };
    }

    var select = document.createElement('select');
    options.forEach(function (option) {
      var item = document.createElement('option');
      item.value = option;
      item.textContent = option;
      select.appendChild(item);
    });

    select.value = w.value;
    select.onchange = function () { send(w.name, select.value); };

    return {
      node: SP.row(w, select),
      patch: function (value) { select.value = value; }
    };
  };

  SP.builders.colour = function (w, send, ask) {
    var box = SP.el('div', 'field swatch');
    var chip = SP.el('div', 'chip');
    var hex = SP.el('span', 'hex');
    box.appendChild(chip);
    box.appendChild(hex);

    function show(value) {
      hex.textContent = value;
      chip.style.background = value;
    }

    // The host owns the picker, so the panel opens the same dialog as the rest of Rhino.
    box.onclick = function () { ask('colour', w.name, hex.textContent); };
    show(w.value || '#808080');

    return { node: SP.row(w, box), patch: show };
  };

  SP.builders.layer = function (w, send, ask, state) {
    var select = document.createElement('select');
    var layers = state.layers || [];

    if (w.value && layers.indexOf(w.value) < 0) layers = [w.value].concat(layers);

    layers.forEach(function (layer) {
      var item = document.createElement('option');
      item.value = layer;
      item.textContent = layer;
      select.appendChild(item);
    });

    select.value = w.value || '';

    // A layer since renamed or deleted would leave the box blank, which reads as a missing value
    // rather than a stale one.
    if (!select.value && layers.length) select.value = layers[0];

    select.onchange = function () { send(w.name, select.value); };

    return {
      node: SP.row(w, select),
      patch: function (value) { if (value) select.value = value; }
    };
  };

  SP.builders.caption = function (w) {
    return { node: SP.el('div', 'caption', w.label || ''), patch: function () { } };
  };

  // A run of buttons becomes one row, under an empty label, the way a dialog puts its actions.
  SP.buttons = function (widgets, send) {
    var node = SP.el('div', 'widget');
    node.appendChild(SP.el('label', null, ''));

    var holder = SP.el('div', 'buttons');
    widgets.forEach(function (w) {
      var press = SP.el('div', 'press' + (w.quiet ? ' quiet' : ''), w.label || w.name);
      press.onclick = function () { send(w.name, true); };
      holder.appendChild(press);
    });

    node.appendChild(holder);
    return { node: node, patch: function () { } };
  };
})();
