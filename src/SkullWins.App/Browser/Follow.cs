namespace SkullWins.App.Browser;

/// <summary>
/// Link hints: label every visible link, type the label, go there.
///
/// In the Linux version this lives inside the render process with synchronous
/// DOM access, across 630 lines that walk the frame tree. Chromium offers no
/// such thing, so the whole algorithm moves into injected JavaScript. That turns
/// out to suit it: getClientRects plus an overlay is a natural fit for the page
/// side, and it is roughly how Vimium has done it for a decade.
///
/// Scoped down for 0.01: anchors in the top document, one label alphabet, no
/// cross-frame walking. Parity with the Linux module comes later.
/// </summary>
public static class Follow
{
    private const string Alphabet = "asdfghjkl";

    public static void Start(MainWindow w, bool newTab)
    {
        w.Eval(Script(newTab));
        w.UseMode("follow");
    }

    public static void Cancel(MainWindow w)
    {
        w.Eval("if (window.__skull_follow__) { window.__skull_follow__.stop(); }");
        w.UseMode("normal");
    }

    private static string Script(bool newTab) => $$"""
        (function () {
          if (window.__skull_follow__) { window.__skull_follow__.stop(); }

          var ALPHABET = {{Quote(Alphabet)}};
          var NEW_TAB  = {{(newTab ? "true" : "false")}};

          var links = [];
          var all = document.querySelectorAll('a[href], button, [role="link"], [onclick]');
          for (var i = 0; i < all.length; i++) {
            var el = all[i];
            var r = el.getBoundingClientRect();
            if (r.width <= 0 || r.height <= 0) { continue; }
            if (r.bottom < 0 || r.top > window.innerHeight) { continue; }
            if (r.right < 0 || r.left > window.innerWidth) { continue; }
            var style = window.getComputedStyle(el);
            if (style.visibility === 'hidden' || style.display === 'none') { continue; }
            links.push({ el: el, rect: r });
          }

          // Top-left first, so the labels read in the order the eye scans.
          links.sort(function (a, b) {
            var dy = a.rect.top - b.rect.top;
            return Math.abs(dy) > 8 ? dy : a.rect.left - b.rect.left;
          });

          function label(n) {
            var width = links.length <= ALPHABET.length ? 1 : 2;
            var out = '';
            for (var i = 0; i < width; i++) {
              out = ALPHABET[n % ALPHABET.length] + out;
              n = Math.floor(n / ALPHABET.length);
            }
            return out;
          }

          var layer = document.createElement('div');
          layer.id = 'skull-hints';
          layer.style.cssText = 'position:fixed;top:0;left:0;width:0;height:0;z-index:2147483647';

          var hints = [];
          for (var i = 0; i < links.length; i++) {
            var tag = label(i);
            var span = document.createElement('span');
            span.textContent = tag;
            span.style.cssText =
              'position:fixed;z-index:2147483647;background:#9fd18a;color:#0d0d0d;' +
              'font:bold 11px/1 Consolas,monospace;padding:2px 4px;border-radius:2px;' +
              'box-shadow:0 1px 3px rgba(0,0,0,.6);pointer-events:none;' +
              'left:' + Math.max(0, links[i].rect.left) + 'px;' +
              'top:'  + Math.max(0, links[i].rect.top)  + 'px;';
            layer.appendChild(span);
            hints.push({ tag: tag, el: links[i].el, span: span });
          }
          document.body.appendChild(layer);

          var typed = '';

          function stop() {
            window.removeEventListener('keydown', onKey, true);
            if (layer.parentNode) { layer.parentNode.removeChild(layer); }
            window.__skull_follow__ = null;
          }

          function activate(hint) {
            stop();
            var el = hint.el;
            var href = el.getAttribute && el.getAttribute('href');
            if (NEW_TAB && href) {
              window.chrome.webview.postMessage({ type: 'follow', uri: el.href });
            } else {
              el.click();
            }
            window.chrome.webview.postMessage({ type: 'mode-request', mode: 'normal' });
          }

          function onKey(ev) {
            ev.preventDefault();
            ev.stopPropagation();

            if (ev.key === 'Escape') {
              stop();
              window.chrome.webview.postMessage({ type: 'mode-request', mode: 'normal' });
              return;
            }

            if (ev.key === 'Backspace') { typed = typed.slice(0, -1); }
            else if (ev.key.length === 1) { typed += ev.key.toLowerCase(); }

            var matches = [];
            for (var i = 0; i < hints.length; i++) {
              var on = hints[i].tag.indexOf(typed) === 0;
              hints[i].span.style.opacity = on ? '1' : '0.25';
              if (on) { matches.push(hints[i]); }
            }

            if (matches.length === 1 && matches[0].tag === typed) { activate(matches[0]); }
            else if (matches.length === 0) {
              stop();
              window.chrome.webview.postMessage({ type: 'mode-request', mode: 'normal' });
            }
          }

          window.addEventListener('keydown', onKey, true);
          window.__skull_follow__ = { stop: stop };
        })();
        """;

    private static string Quote(string s) => "\"" + s.Replace("\"", "\\\"") + "\"";
}
