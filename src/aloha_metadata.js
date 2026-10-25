/* ============================================================================
   aloha_metadata.js  —  Aloha media-metadata hover hook
   ----------------------------------------------------------------------------
   Injected into every document via:
       await core.AddScriptToExecuteOnDocumentCreatedAsync(File.ReadAllText("aloha_metadata.js"));

   It draws the on-hover card and the click-to-open hashes sheet, and it asks
   the C# host for everything data-related. It NEVER reads file bytes itself.

   Wire protocol (JSON both ways):
     page -> host   window.chrome.webview.postMessage(JSON.stringify(msg))
        { t:"sniff", id, url, kind }          request metadata for a resource
        { t:"open",  url, kind }              open the native hashes sheet (MediaSheetWindow)
     host -> page   fires as a 'message' event; e.data is the object (or a JSON string)
        { t:"meta", id, meta }                meta below
        { t:"meta", id, err }                 the host could not read it; err is shown in the card
        { t:"off" }                           the card was switched off in Browser Style: tear down
        { t:"hash", id, algo, hex, err? }     one finished digest
        { t:"progress", id, algo, pct }       optional, 0..100

     meta = { name, type, ext, decl, magic, sig, rows:[[label,value],...], sha, note }
        decl : declared extension when it disagrees with the bytes (else null/omit)
        sha  : primary SHA-256 shown on the card, or null to show "click to compute"

   With no host (opened in a plain browser) a tiny demo responder answers locally
   so the file still previews. Delete DEMO + its two calls for production if you
   prefer a hard dependency on the host.
   ========================================================================== */
(function () {
  if (window.__alohaMX) return;
  window.__alohaMX = true;
  var HOST = (window.chrome && window.chrome.webview) || null;

  /* The hashes sheet is a native Aloha window (MediaSheetWindow); a click only asks the host to open it. */
  var FILE_EXT = /\.(png|jpe?g|jfif|gif|bmp|webp|avif|heic|heif|tiff?|ico|svg|psd|raw|cr2|nef|arw|dng|exr|hdr|pdf|zip|7z|rar|tar|gz|bz2|xz|zst|epub|mobi|djvu|cbz|cbr|doc[xm]?|xls[xmb]?|ppt[xm]?|odt|ods|odp|rtf|csv|tsv|json|xml|yaml|yml|toml|md|txt|log|mp[34]|m4[av]|aac|flac|opus|ogg|wav|mid|webm|mkv|avi|mov|flv|wmv|obj|stl|ply|gltf|glb|fbx|fits|ipynb|sqlite|db|parquet|wasm|exe|dll|so|bin|iso|dmg|apk|ttf|otf|woff2?)($|\?|#)/i;

  /* ---------------------------------------------------------------- styles */
  var CSS = [
    "#amx-root{all:initial}",
    "#amx-root .amx-neon,#amx-root .amx-menu{color:#b4dcba;font:12.5px/1.55 Consolas,'Courier New',monospace;background:#000;border:1px solid rgba(127,174,134,.20);box-shadow:0 0 0 1px #000,0 20px 60px -20px #000,0 0 22px -6px rgba(127,174,134,.28),0 0 40px -10px rgba(127,174,134,.16)}",
    "#amx-root .amx-fname{padding:6px 12px 5px;color:#1cee36;font-weight:700;letter-spacing:.3px;text-align:center;text-shadow:0 0 10px rgba(28,110,54,.6);word-break:break-all}",
    "#amx-root .amx-hd{background:#ffb62e;color:#241800;padding:5px 12px;border-bottom:1px solid rgba(0,0,0,.18)}",
    "#amx-root .amx-hd .amx-type{display:flex;justify-content:space-between;gap:12px}",
    "#amx-root .amx-hd .amx-t{color:#241800;font-weight:700}",
    "#amx-root .amx-hd .amx-bang{margin-right:5px}",
    "#amx-root .amx-hd .amx-mg{color:#4a3300;letter-spacing:.5px;white-space:nowrap}",
    "#amx-root .amx-warn{background:#ffb62e;color:#241800;font-weight:700;letter-spacing:.4px;padding:4px 12px}",
    "#amx-root .amx-pad{padding:8px 12px 10px}",
    /* field rows are DIVs, not a <table>: a page's own table/td rules (borders, white cells)
       leaked into the card on Startpage; divs with every property pinned cannot inherit them */
    "#amx-root .amx-tbl{display:block;width:100%;margin:0;padding:0;border:0;background:transparent}",
    "#amx-root .amx-tr{display:flex;align-items:flex-start;padding:1.5px 0;margin:0;border:0;background:transparent}",
    "#amx-root .amx-l{color:#7fae86;text-transform:uppercase;letter-spacing:.6px;width:104px;flex:none;white-space:nowrap;background:transparent;border:0}",
    "#amx-root .amx-v{color:#b4dcba;flex:1;min-width:0;word-break:break-all;background:transparent;border:0}",
    "#amx-root .amx-v.amx-dim{color:#7fae86}",
    "#amx-root .amx-sec{color:#7fae86;text-transform:uppercase;letter-spacing:1.4px;font-size:10px;margin:9px 0 5px;border-top:1px dashed rgba(127,174,134,.2);padding-top:7px}",
    "#amx-root .amx-hex{color:#b4dcba;word-break:break-all;font-size:11.5px;opacity:.95}",
    "#amx-root .amx-fp{display:flex;gap:2px;height:16px}",
    "#amx-root .amx-fp i{flex:1;border-radius:1px}",
    "#amx-root .amx-reading{color:#7fae86;font-style:italic;padding:10px 12px}",
    "#amx-root .amx-fail{color:#d9a400;padding:10px 12px;font-size:12px}",
    "#amx-root .amx-fail b{color:#ffd23f;font-weight:400}",
    "#amx-root .amx-ov{position:fixed;z-index:2147483600;width:344px;pointer-events:none;opacity:0;transform:translateY(5px);transition:opacity .1s,transform .1s;border-radius:4px;overflow:hidden}",
    "#amx-root .amx-ov.amx-on{opacity:1;transform:none}",
    "#amx-root .amx-cta{padding:5px 12px;border-top:1px solid rgba(127,174,134,.2);color:#7fae86;letter-spacing:.4px;background:rgba(127,174,134,.06);text-align:center}",
    "#amx-root .amx-cta b{color:#7fae86}",
  ].join("");

  var st = document.createElement("style"); st.textContent = CSS;
  var root = document.createElement("div"); root.id = "amx-root";
  var attach = function () {
    (document.head || document.documentElement).appendChild(st);
    document.documentElement.appendChild(root);
    root.innerHTML = '<div class="amx-ov amx-neon" id="amx-ov"></div>';
    ov = root.querySelector("#amx-ov");
  };
  var ov;

  /* ---------------------------------------------------------------- host IO */
  var reqId = 0, pending = {};
  /* Every request gets an answer or a timeout — "reading file…" must never be a terminal state.
     The host answers a failed sniff with {t:"meta", err}; a host that is wedged (or gone) is
     caught by the timer. 30 s covers a slow Tor circuit; a hash keeps the timer alive on progress. */
  var TIMEOUT_MS = 30000;
  function request(msg, handlers) {
    msg.id = ++reqId; pending[msg.id] = handlers;
    arm(msg.id, msg.t);
    if (HOST) HOST.postMessage(JSON.stringify(msg)); else DEMO(msg);
  }
  function arm(id, t) {
    var h = pending[id]; if (!h) return;
    clearTimeout(h.timer);
    h.timer = setTimeout(function () {
      if (!pending[id]) return; delete pending[id];
      if (t === "sniff" && h.meta) h.meta(null, "no answer from Aloha (timed out)");
      else if (h.hash) h.hash(null, "timed out");
    }, TIMEOUT_MS);
  }
  function onMessage(data) {
    var m; try { m = (typeof data === "string") ? JSON.parse(data) : data; } catch (e) { return; }
    if (!m) return;
    if (m.t === "off") { teardown(); return; }
    var h = pending[m.id]; if (!h) return;
    if (m.t === "progress") { if (h.progress) h.progress(m.pct); arm(m.id, "hash"); return; }
    delete pending[m.id]; clearTimeout(h.timer);
    if (m.t === "meta" && h.meta) h.meta(m.err ? null : m.meta, m.err);
    else if (m.t === "hash" && h.hash) h.hash(m.hex, m.err);
  }
  /* Browser Style switched the card off: take it down and stop listening. */
  function teardown() {
    try { hideOv(); } catch (e) {}
    try { root.remove(); } catch (e) {}
    pending = {}; dead = true;
  }
  var dead = false;
  if (HOST) HOST.addEventListener("message", function (e) { onMessage(e.data); });
  function requestMeta(url, kind, cb) { request({ t: "sniff", url: url, kind: kind }, { meta: cb }); }
  function failHTML(name, why) { return '<div class="amx-fname">' + esc(name) + '</div><div class="amx-fail">could not read this file<br><b>' + esc(why) + "</b></div>"; }

  /* ---------------------------------------------------------------- render */
  function esc(s) { return String(s == null ? "" : s).replace(/[&<>]/g, function (c) { return { "&": "&amp;", "<": "&lt;", ">": "&gt;" }[c]; }); }
  function fpFrom(str) { var h = 0, i; for (i = 0; i < str.length; i++) h = (h * 131 + str.charCodeAt(i)) >>> 0; var rnd = function () { h = (h * 1103515245 + 12345) >>> 0; return h / 4294967296; }; var c = ""; for (i = 0; i < 10; i++) { var hue = 95 + Math.floor(rnd() * 45), l = 42 + Math.floor(rnd() * 26); c += '<i style="background:hsl(' + hue + ' 90% ' + l + '%);box-shadow:0 0 6px hsl(' + hue + ' 90% ' + l + '% / .6)"></i>'; } return '<div class="amx-fp">' + c + "</div>"; }
  function warnBar(m) { return m.decl ? '<div class="amx-warn">⚠ EXTENSION SAYS .' + esc(String(m.decl).toUpperCase()) + " · BYTES SAY " + esc(m.ext) + "</div>" : ""; }
  function typeBand(m) { return '<div class="amx-hd"><div class="amx-type"><span class="amx-t"><span class="amx-bang">⚠</span>' + esc(m.type) + '</span><span class="amx-mg">' + esc(m.magic) + "</span></div></div>"; }
  function fieldRows(m) {
    var rows = (m.rows || []).map(function (r) { var dim = String(r[1]).charAt(0) === "—" ? " amx-dim" : ""; return '<div class="amx-tr"><span class="amx-l">' + esc(r[0]) + '</span><span class="amx-v' + dim + '">' + esc(r[1]) + "</span></div>"; }).join("");
    var sha = m.sha ? '<div class="amx-tr"><span class="amx-l">SHA-256</span><span class="amx-v">' + esc(m.sha) + "</span></div>" : '<div class="amx-tr"><span class="amx-l">SHA-256</span><span class="amx-v amx-dim">— click to compute</span></div>';
    return rows + sha;
  }
  function overlayHTML(m) { return '<div class="amx-fname">' + esc(m.name) + "</div>" + typeBand(m) + warnBar(m) + '<div class="amx-pad"><div class="amx-tbl">' + fieldRows(m) + '</div><div class="amx-sec">signature</div><div class="amx-hex">' + esc(m.sig) + "</div></div>" + '<div class="amx-cta">▸ click for hashes &amp; full sheet</div>'; }

  /* ---------------------------------------------------------------- overlay */
  var timer = null, curUrl = null;
  function place(x, y) { var w = ov.offsetWidth, h = ov.offsetHeight, mg = 14, vw = innerWidth, vh = innerHeight, nx = x + 18, ny = y + 18; if (nx + w + mg > vw) nx = x - w - 18; if (ny + h + mg > vh) ny = y - h - 18; ov.style.left = Math.max(mg, nx) + "px"; ov.style.top = Math.max(mg, ny) + "px"; }
  function showOv(res, x, y) {
    curUrl = res.url; var name = res.url.split(/[\\/]/).pop().split(/[?#]/)[0] || res.url;
    ov.innerHTML = '<div class="amx-fname">' + esc(name) + '</div><div class="amx-reading">reading file…</div>'; place(x, y); ov.classList.add("amx-on");
    requestMeta(res.url, res.kind, function (m, err) { if (curUrl !== res.url) return; ov.innerHTML = m ? overlayHTML(m) : failHTML(name, err); place(x, y); });
  }
  function hideOv() { ov.classList.remove("amx-on"); curUrl = null; }

  /* ---------------------------------------------------------------- the sheet (native) */
  /* A click hands the resource to Aloha, which opens MediaSheetWindow — a real Aloha window:
     draggable, min/max/close in the footer, the gray corner resize square. Nothing is drawn
     in the page for it, so no page style can touch it and no scrim dims the site. */
  function openSheet(res) { if (HOST) HOST.postMessage(JSON.stringify({ t: "open", url: res.url, kind: res.kind })); }

  /* ---------------------------------------------------------------- hover wiring */
  function resourceFor(el) {
    for (var n = el; n && n !== document.documentElement; n = n.parentElement) {
      var t = n.tagName;
      if (t === "IMG" && (n.currentSrc || n.src)) return { url: n.currentSrc || n.src, kind: "img" };
      if (t === "A" && n.href && FILE_EXT.test(n.getAttribute("href") || n.href)) return { url: n.href, kind: "link" };
      if ((t === "VIDEO" || t === "AUDIO") && (n.currentSrc || n.src)) return { url: n.currentSrc || n.src, kind: "media" };
      if (t === "SOURCE" && n.src) return { url: n.src, kind: "media" };
      if (t === "OBJECT" && n.data) return { url: n.data, kind: "object" };
      if (t === "EMBED" && n.src) return { url: n.src, kind: "object" };
      try { var bg = getComputedStyle(n).backgroundImage; if (bg && bg.indexOf("url(") === 0) return { url: bg.slice(4, -1).replace(/^["']|["']$/g, ""), kind: "bg" }; } catch (e) {}
    }
    return null;
  }
  document.addEventListener("mousemove", function (e) {
    if (dead) return;
    if (root.contains(e.target)) return;
    var res = resourceFor(e.target);
    if (!res) { clearTimeout(timer); if (curUrl) hideOv(); return; }
    if (res.url === curUrl) { place(e.clientX, e.clientY); return; }
    clearTimeout(timer); var x = e.clientX, y = e.clientY; timer = setTimeout(function () { showOv(res, x, y); }, 150);
  }, true);
  document.addEventListener("click", function (e) {
    if (dead) return;
    var res = resourceFor(e.target); if (!res) return;
    e.preventDefault(); e.stopPropagation(); hideOv(); openSheet(res);
  }, true);

  /* ---------------------------------------------------------------- demo fallback (no host) */
  function DEMO(msg) {
    setTimeout(function () {
      if (msg.t === "sniff") {
        var name = msg.url.split(/[\\/]/).pop().split(/[?#]/)[0] || "file";
        var ext = (name.split(".").pop() || "").toUpperCase();
        onMessage({ t: "meta", id: msg.id, meta: { name: name, type: ext + " file", ext: ext, decl: null, magic: "— (host offline)", sig: "— host reads the bytes", rows: [["Size", "—"], ["Source", msg.kind]], sha: null, note: "demo fallback — no Aloha host attached" } });
      } else if (msg.t === "hash") {
        var h = 0, s = msg.url + msg.algo, i; for (i = 0; i < s.length; i++) h = (h * 131 + s.charCodeAt(i)) >>> 0; var out = ""; for (i = 0; i < 16; i++) { h = (h * 1103515245 + 12345) >>> 0; out += "0123456789abcdef"[h & 15]; }
        onMessage({ t: "hash", id: msg.id, algo: msg.algo, hex: out.replace(/(.{6}).+(.{4})/, "$1…$2") });
      }
    }, 200 + Math.random() * 250);
  }

  if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", attach); else attach();
})();