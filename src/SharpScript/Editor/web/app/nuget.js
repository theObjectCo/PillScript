// Searching nuget.org from the references window, and adding what comes back. The rest of that
// window is in references.js, which owns the lists and the note under them.
(function () {
  'use strict';

  var SS = window.SS;
  var el = SS.el;

  // Long enough to finish typing a package name, short enough that the list feels live.
  var SEARCH_DELAY = 350;

  var timer = null;

  function note(text, kind) { SS.refs.note(text, kind); }

  SS.nuget = { hideResults: hideResults };

  function hideResults() {
    el['ref-results'].classList.add('hidden');
    el['ref-results'].innerHTML = '';
  }

  el['ref-search'].oninput = function () {
    if (timer) clearTimeout(timer);

    var term = el['ref-search'].value.trim();
    if (term.length < 2) { hideResults(); note(''); return; }

    timer = setTimeout(function () { search(term); }, SEARCH_DELAY);
  };

  el['ref-search'].onkeydown = function (event) {
    if (event.key === 'Escape') { hideResults(); event.stopPropagation(); return; }
    if (event.key !== 'Enter') return;

    var term = el['ref-search'].value.trim();
    if (term) add(term, '');
  };

  function add(id, version) {
    return SS.refs.change('addPackage', { name: id, content: version }).then(function () {
      el['ref-search'].value = '';
      hideResults();
    });
  }

  function search(term) {
    note('Searching nuget.org...');

    SS.request('searchPackages', { name: term }).then(function (hits) {
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

      item.appendChild(SS.span('id', hit.id));
      item.appendChild(SS.span('version', hit.version));
      item.appendChild(SS.span('blurb', hit.description || ''));

      item.onclick = function () { add(hit.id, hit.version); };
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

    literal.onclick = function () { add(term, ''); };
    el['ref-results'].appendChild(literal);
  }
})();
