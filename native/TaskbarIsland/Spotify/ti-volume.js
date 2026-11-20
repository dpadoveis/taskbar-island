// Taskbar Island — Spotify volume bridge.
// Injected into Spotify's own UI (xpui.spa) by Taskbar Island when "Spotify volume" is switched on, and removed
// when it is switched off. It only reads and moves Spotify's own volume slider, and talks to nothing but the
// Taskbar Island widget on this computer (ws://127.0.0.1:47853). Nothing else is read or sent.
(function () {
  "use strict";
  if (window.__taskbarIslandVolume) return;
  window.__taskbarIslandVolume = true;

  var PORT = 47853;
  var ws = null, last = -1;

  function slider() { return document.querySelector('[data-testid="volume-bar"] input[type="range"]'); }

  // The range input is rounded to 0.1 steps; the bar's CSS transform carries the exact value.
  function readVolume() {
    var bar = document.querySelector('[data-testid="volume-bar"] [style*="--progress-bar-transform"]');
    if (bar) {
      var m = /--progress-bar-transform:\s*([\d.]+)%/.exec(bar.getAttribute("style") || "");
      if (m) return Math.max(0, Math.min(1, parseFloat(m[1]) / 100));
    }
    var i = slider();
    return i ? parseFloat(i.value) : -1;
  }

  function setVolume(v) {
    var i = slider();
    if (!i || !(v >= 0 && v <= 1)) return;
    var setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value").set;
    i.step = "any";
    setter.call(i, String(v));
    i.dispatchEvent(new Event("input", { bubbles: true }));
    i.dispatchEvent(new Event("change", { bubbles: true }));
  }

  function send(force) {
    var v = readVolume();
    if (v < 0 || !ws || ws.readyState !== 1) return;
    if (!force && Math.abs(v - last) < 0.001) return;
    last = v;
    ws.send(JSON.stringify({ v: Math.round(v * 1000) / 1000 }));
  }

  function connect() {
    try {
      ws = new WebSocket("ws://127.0.0.1:" + PORT + "/spotify-volume");
      ws.onopen = function () { send(true); };
      ws.onmessage = function (e) {
        try {
          var m = JSON.parse(e.data);
          if (typeof m.set === "number") { setVolume(m.set); setTimeout(function () { send(true); }, 60); }
          if (m.get) send(true);
        } catch (_) { }
      };
      ws.onclose = function () { ws = null; setTimeout(connect, 3000); };
      ws.onerror = function () { try { ws.close(); } catch (_) { } };
    } catch (_) { setTimeout(connect, 3000); }
  }

  setInterval(function () { try { send(false); } catch (_) { } }, 400);
  connect();
})();
