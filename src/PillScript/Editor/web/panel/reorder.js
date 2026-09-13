// Dragging a section's heading moves it among the others. The order is the panel's own, not the
// document's: scripts land on the canvas in whatever order they were written, and the order they
// want to be read in is a different question. The host keeps it with the document.
//
// The same heading is also the thing that rolls a section up, so a press that never moved is left
// alone for the click handler to deal with.
(function () {
  'use strict';

  var SP = window.SP;

  SP.reorder = function (sections, say) {
    var carried = null;
    var from = 0;
    var moved = false;

    function order() {
      return Array.prototype.map.call(sections.querySelectorAll('.section'), function (block) {
        return block.dataset.component;
      });
    }

    /// The section the pointer is over, and which half of it, so a swap happens once per crossing.
    function settle(y) {
      var blocks = Array.prototype.slice.call(sections.querySelectorAll('.section'));

      for (var i = 0; i < blocks.length; i++) {
        var block = blocks[i];
        if (block === carried) continue;

        var box = block.getBoundingClientRect();
        var middle = box.top + box.height / 2;

        if (y < middle && block.compareDocumentPosition(carried) & Node.DOCUMENT_POSITION_FOLLOWING) {
          sections.insertBefore(carried, block);
          return;
        }

        if (y > middle && block.compareDocumentPosition(carried) & Node.DOCUMENT_POSITION_PRECEDING) {
          sections.insertBefore(carried, block.nextSibling);
          return;
        }
      }
    }

    sections.addEventListener('pointerdown', function (event) {
      var head = event.target.closest && event.target.closest('.head');
      if (!head || event.button !== 0) return;

      carried = head.parentElement;
      from = event.clientY;
      moved = false;
      sections.setPointerCapture(event.pointerId);
    });

    sections.addEventListener('pointermove', function (event) {
      if (!carried) return;

      // A few pixels of slack, so a plain click on the heading still rolls the section up.
      if (!moved && Math.abs(event.clientY - from) < 4) return;

      if (!moved) {
        moved = true;
        carried.classList.add('carried');
      }

      settle(event.clientY);
    });

    sections.addEventListener('pointerup', function (event) {
      if (!carried) return;

      sections.releasePointerCapture(event.pointerId);
      carried.classList.remove('carried');
      carried = null;

      if (!moved) return;

      // Tells the click handler on the heading that this press was a drag.
      sections.dataset.dragged = '1';
      say({ type: 'order', components: order() });
    });
  };
})();
