// Everything that crosses to the host: one-way notices, calls that expect an answer, and the
// routing of what comes back. Nothing else in the page talks to WebView2 directly.
(function () {
  'use strict';

  var SS = window.SS;
  var host = window.chrome && window.chrome.webview;

  var pending = {};
  var nextRequest = 1;

  // How long a call waits before it is abandoned. The host answers in milliseconds, so this only
  // stops a lost answer from leaving a promise pending for the rest of the session.
  var TIMEOUT = 10000;

  SS.hosted = !!host;

  SS.send = function (message) {
    if (host) host.postMessage(message);
  };

  SS.request = function (type, payload) {
    if (!host) return Promise.resolve(null);

    return new Promise(function (resolve) {
      var id = String(nextRequest++);
      pending[id] = resolve;

      var message = { type: type, id: id };
      for (var key in payload) message[key] = payload[key];

      SS.send(message);

      setTimeout(function () {
        if (!pending[id]) return;

        delete pending[id];
        resolve(null);
      }, TIMEOUT);
    });
  };

  // A failure in the page is reported to the host and printed in the editor's output pane.
  // Without this it would be visible only in the WebView2 console, which is not open.
  window.addEventListener('error', function (event) {
    SS.send({
      type: 'pageError',
      text: String(event.message) + ' at ' + event.filename + ':' + event.lineno
    });
  });

  window.addEventListener('unhandledrejection', function (event) {
    SS.send({ type: 'pageError', text: 'Unhandled rejection: ' + String(event.reason) });
  });

  if (!host) return;

  host.addEventListener('message', function (event) {
    var message = event.data;
    if (!message || !message.type) return;

    if (message.type === 'reply') {
      var resolve = pending[message.id];
      if (!resolve) return;

      delete pending[message.id];
      resolve(message.payload);
      return;
    }

    var handler = SS.handlers[message.type];
    if (handler) handler(message);
  });
})();
