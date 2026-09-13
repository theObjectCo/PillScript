// The controls that pick something rather than carry a number, and the row a run of buttons
// shares. They follow the same contract as the ones in controls.js: a node and a patch.
(function () {
  'use strict';

  var SP = window.SP;

  SP.builders.toggle = function (w, send) {
    var check = SP.el('div', 'check');
    var box = SP.el('div', 'box');
    box.appendChild(SP.el('div', 'knob'));
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

  // A list of the panel's own rather than a select element. The dropdown a select opens is a
  // window of its own, and a window opened from a docked panel is at the mercy of whatever Rhino
  // does with focus; this one is drawn in the page and cannot be taken away.
  function dropdown(w, options, send, show) {
    var read = show || function (value) { return value; };

    var box = SP.el('div', 'field picker');
    var picked = SP.el('span', 'picked', read(w.value || ''));
    picked.dataset.value = w.value || '';
    var chevron = SP.glyph('M6 9l6 6 6-6', 2);
    chevron.setAttribute('class', 'chev');

    box.appendChild(picked);
    box.appendChild(SP.el('div', 'spacer'));
    box.appendChild(chevron);

    var list = null;

    function shut() {
      if (!list) return;
      list.remove();
      list = null;
      document.removeEventListener('pointerdown', away, true);
      window.removeEventListener('keydown', escaped, true);
    }

    function away(event) { if (!list.contains(event.target)) shut(); }
    function escaped(event) { if (event.key === 'Escape') shut(); }

    function open() {
      if (list) { shut(); return; }

      list = SP.el('div', 'list');

      options.forEach(function (option) {
        var item = SP.el('div', option === picked.dataset.value ? 'on' : null, read(option));
        item.onpointerdown = function (event) {
          event.stopPropagation();
          picked.textContent = read(option);
          picked.dataset.value = option;
          shut();
          send(w.name, option);
        };
        list.appendChild(item);
      });

      // Fixed, so the list is not clipped by the section it was opened from. Above the box when
      // there is no room below, which is most of the time for a section near the foot.
      var box2 = box.getBoundingClientRect();
      list.style.left = box2.left + 'px';
      list.style.width = box2.width + 'px';

      document.body.appendChild(list);

      var height = list.getBoundingClientRect().height;
      var below = window.innerHeight - box2.bottom;

      list.style.top = (height > below && box2.top > height ? box2.top - height : box2.bottom) + 'px';

      document.addEventListener('pointerdown', away, true);
      window.addEventListener('keydown', escaped, true);
    }

    box.onclick = open;

    return {
      node: SP.row(w, box),
      busy: function () { return !!list; },
      patch: function (value) {
        if (!value) return;
        picked.textContent = read(value);
        picked.dataset.value = value;
      }
    };
  }

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

    return dropdown(w, options, send);
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
    var layers = state.layers || [];

    // A layer since renamed or deleted would leave the box blank, which reads as a missing value
    // rather than a stale one, so it is kept in the list.
    if (w.value && layers.indexOf(w.value) < 0) layers = [w.value].concat(layers);

    // Rhino writes a nested layer as Parent::Child; the panel gives the colons room to breathe,
    // and sends back the path as the document has it.
    return dropdown(
      { name: w.name, label: w.label, value: w.value || layers[0] || '' }, layers, send,
      function (path) { return path.split('::').join(' :: '); });
  };

  SP.builders.caption = function (w) {
    return { node: SP.el('div', 'caption', w.label || ''), patch: function () { } };
  };

  // The glyphs a button may ask for by name. Anything else is drawn without one.
  var icons = {
    bake: 'M12 4v10M8 11l4 4 4-4M5 19h14',
    run: 'M7 4.5l12 7.5-12 7.5z',
    refresh: 'M19 12a7 7 0 1 1-2.05-4.95M19 4v4h-4',
    add: 'M12 6v12M6 12h12',
    remove: 'M6 12h12'
  };

  // A run of buttons becomes one row across the card, the way a dialog puts its actions.
  SP.buttons = function (widgets, send) {
    var holder = SP.el('div', 'buttons');

    widgets.forEach(function (w) {
      var press = SP.el('div', 'press' + (w.quiet ? ' quiet' : ''));

      if (icons[w.icon]) {
        var glyph = SP.glyph(icons[w.icon], 1.8);
        glyph.setAttribute('stroke-linejoin', 'round');
        glyph.setAttribute('class', 'mark');
        press.appendChild(glyph);
      }

      press.appendChild(SP.el('span', null, w.label || w.name));
      press.onclick = function () { send(w.name, true); };
      holder.appendChild(press);
    });

    return { node: holder, patch: function () { } };
  };
})();
