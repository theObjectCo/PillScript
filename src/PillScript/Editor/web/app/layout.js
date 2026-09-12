// Where the panes sit, and where the window sits. The window has no frame of its own, so moving
// and resizing it is reported to the host, which hands the press to the system.
(function () {
  'use strict';

  var SS = window.SS;
  var el = SS.el;

  var layout = { sidebar: true, minimap: true, panel: true, toolbar: true };

  SS.applyLayout = function () {
    el.sidebar.classList.toggle('hidden', !layout.sidebar);
    el.panel.classList.toggle('hidden', !layout.panel);
    el.toolbar.classList.toggle('hidden', !layout.toolbar);

    document.body.classList.toggle('no-sidebar', !layout.sidebar);
    document.body.classList.toggle('no-panel', !layout.panel);

    el['t-sidebar'].classList.toggle('on', layout.sidebar);
    el['t-minimap'].classList.toggle('on', layout.minimap);
    el['t-panel'].classList.toggle('on', layout.panel);
    el['t-toolbar'].classList.toggle('on', layout.toolbar);

    if (SS.state.editor) {
      SS.state.editor.updateOptions({
        minimap: { enabled: layout.minimap, renderCharacters: false }
      });
    }
  };

  SS.minimapEnabled = function () { return layout.minimap; };

  function toggle(name) {
    layout[name] = !layout[name];
    SS.applyLayout();
  }

  el['t-sidebar'].onclick = function () { toggle('sidebar'); };
  el['t-minimap'].onclick = function () { toggle('minimap'); };
  el['t-panel'].onclick = function () { toggle('panel'); };
  el['t-toolbar'].onclick = function () { toggle('toolbar'); };

  // Zen turns everything off, or back on again once there is nothing left to turn off.
  el['t-zen'].onclick = function () {
    var anyOn = layout.sidebar || layout.minimap || layout.panel || layout.toolbar;
    layout = { sidebar: !anyOn, minimap: !anyOn, panel: !anyOn, toolbar: !anyOn };

    SS.applyLayout();
  };

  // ----- the window -----------------------------------------------------------------------------

  el['dock-left'].onclick = function () { SS.send({ type: 'dock', action: 'left' }); };
  el['dock-right'].onclick = function () { SS.send({ type: 'dock', action: 'right' }); };
  el['dock-close'].onclick = function () { SS.send({ type: 'window', action: 'close' }); };
  el.locate.onclick = function () { SS.send({ type: 'locate' }); };

  el['win-min'].onclick = function () { SS.send({ type: 'window', action: 'minimize' }); };
  el['win-max'].onclick = function () { SS.send({ type: 'window', action: 'maximize' }); };
  el['win-close'].onclick = function () { SS.send({ type: 'window', action: 'close' }); };

  Array.prototype.forEach.call(document.querySelectorAll('.resize'), function (zone) {
    zone.addEventListener('mousedown', function (event) {
      if (event.button !== 0) return;

      SS.send({ type: 'window', action: 'resize-' + zone.getAttribute('data-edge') });
    });
  });

  // WebView2 at the version Rhino ships has no non-client regions, so dragging the window is
  // relayed to the host instead of being declared in CSS.
  el.titlebar.addEventListener('mousedown', function (event) {
    if (event.button !== 0) return;
    if (event.target.closest('.window-buttons')) return;
    if (document.body.classList.contains('docked')) return;

    SS.send({ type: 'window', action: 'drag' });
  });

  el.titlebar.addEventListener('dblclick', function (event) {
    if (event.target.closest('.window-buttons')) return;
    if (document.body.classList.contains('docked')) return;

    SS.send({ type: 'window', action: 'maximize' });
  });

  // ----- splitters --------------------------------------------------------------------------------

  // Dragging the dock splitter reports how far it has moved from where it was grabbed; the host
  // turns that into a share of the Grasshopper window, so the split survives a resize.
  el.splitter.addEventListener('pointerdown', function (event) {
    if (event.button !== 0) return;

    var start = event.screenX;
    var ratio = window.devicePixelRatio || 1;

    // Widening means dragging away from the canvas, which is leftwards on the right side and
    // rightwards on the left one, so the sign follows the side the editor is docked to.
    var sign = document.body.classList.contains('dock-left') ? 1 : -1;

    el.splitter.setPointerCapture(event.pointerId);
    el.splitter.classList.add('dragging');
    SS.send({ type: 'dockSplitStart' });

    drag(el.splitter, function (moved) {
      SS.send({ type: 'dockSplit', offset: Math.round((moved.screenX - start) * ratio * sign) });
    });
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

      drag(handle, function (moved) {
        var now = vertical ? moved.clientX : moved.clientY;
        var size = from + (invert ? start - now : now - start);
        var most = (vertical ? window.innerWidth : window.innerHeight) - 220;

        size = Math.max(140, Math.min(most, size));

        if (vertical) pane.style.width = size + 'px';
        else pane.style.height = size + 'px';
      });
    });
  }

  /// Follows the pointer until it is let go, then tidies up after itself.
  function drag(handle, move) {
    function stop() {
      handle.classList.remove('dragging');
      handle.removeEventListener('pointermove', move);
      handle.removeEventListener('pointerup', stop);
      handle.removeEventListener('pointercancel', stop);
    }

    handle.addEventListener('pointermove', move);
    handle.addEventListener('pointerup', stop);
    handle.addEventListener('pointercancel', stop);
  }

  paneSplitter(el['split-sidebar'], el.sidebar, true, false);
  paneSplitter(el['split-panel'], el.panel, false, true);

  SS.applyLayout();
})();
