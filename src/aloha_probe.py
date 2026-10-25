#!/usr/bin/env python3
# =====================================================================
# aloha_probe.py - Aloha's page prober, as a real console program.
#
# This replaces the in-app red inspector window. Everything it did is
# here, but running in an actual cmd.exe console: no WinForms imitation
# of a terminal, no bespoke chrome to keep in sync with the rest of the
# browser. A terminal that IS a terminal.
#
# It talks to the browser over the Chrome DevTools Protocol on the
# loopback port Aloha opens when "Embedded DevTools" is enabled
# (127.0.0.1:9333, origin-locked to https://devtools.aloha). Because it
# is an ordinary script attached over a socket rather than code welded
# into the app, you can edit it, run it standalone, pipe it, or point
# it at any Chromium that exposes CDP.
#
# STDLIB ONLY - no pip install. The WebSocket client below is ~90 lines
# of RFC 6455 because adding a dependency to a privacy browser's tool
# chain is a bigger cost than writing the frame codec.
#
# Usage:   python aloha_probe.py [--port 9333] [--once "forms"]
# =====================================================================

import argparse, base64, json, os, socket, ssl, struct, sys, textwrap, urllib.request

ORIGIN = "https://devtools.aloha"   # must match --remote-allow-origins
VERSION = "1.0"

# The prompt follows the theme: a console that calls itself DevTool in its
# masthead should not then ask you questions as something else.
PROMPT = ["probe> "]

# ---------------------------------------------------------------- colour
class C:
    if os.name == "nt":
        os.system("")            # enable ANSI on Windows consoles
    # default: the lime the rest of Aloha wears
    G   = "\033[92m"; GD = "\033[32m"; W = "\033[97m"
    Y   = "\033[93m"; R  = "\033[91m"; D = "\033[90m"; X = "\033[0m"

    @classmethod
    def devtool(cls):
        """The old inspector's palette: red on black. The window is gone but its
        identity isn't - this console is the same tool, wearing the same colours,
        in a terminal that is actually a terminal."""
        cls.G  = "\033[91m"      # prompt / primary  -> red
        cls.GD = "\033[31m"      # dim red
        cls.W  = "\033[97m"      # headings stay white
        cls.D  = "\033[31m"      # "dim" becomes dark red, not grey

def out(s=""):  print(s)
def ok(s):      print(C.G  + s + C.X)
def dim(s):     print(C.D  + s + C.X)
def warn(s):    print(C.Y  + s + C.X)
def err(s):     print(C.R  + s + C.X)
def head(s):    print(C.W  + s + C.X)

# ------------------------------------------------------- websocket client
class WS:
    """Minimal RFC 6455 client: text frames, masking, fragmentation, ping."""
    def __init__(self, url, origin=ORIGIN, timeout=20):
        if not url.startswith("ws://"):
            raise ValueError("only ws:// (loopback CDP) is supported")
        rest = url[5:]
        hostport, _, path = rest.partition("/")
        path = "/" + path
        host, _, port = hostport.partition(":")
        port = int(port or 80)

        self.sock = socket.create_connection((host, port), timeout=timeout)
        self.buf = b""

        key = base64.b64encode(os.urandom(16)).decode()
        req = (
            f"GET {path} HTTP/1.1\r\n"
            f"Host: {hostport}\r\n"
            "Upgrade: websocket\r\n"
            "Connection: Upgrade\r\n"
            f"Sec-WebSocket-Key: {key}\r\n"
            "Sec-WebSocket-Version: 13\r\n"
            f"Origin: {origin}\r\n\r\n"
        )
        self.sock.sendall(req.encode())

        # read the handshake response head
        while b"\r\n\r\n" not in self.buf:
            chunk = self.sock.recv(4096)
            if not chunk:
                raise IOError("server closed during handshake")
            self.buf += chunk
        head_, _, self.buf = self.buf.partition(b"\r\n\r\n")
        status = head_.split(b"\r\n")[0].decode(errors="replace")
        if "101" not in status:
            raise IOError(
                "CDP refused the upgrade: " + status +
                "\n(a 403 here means the Origin is not allow-listed - Aloha must be "
                "launched with --remote-allow-origins=" + origin + ")")

    def _recv_exact(self, n):
        while len(self.buf) < n:
            chunk = self.sock.recv(65536)
            if not chunk:
                raise IOError("connection closed")
            self.buf += chunk
        d, self.buf = self.buf[:n], self.buf[n:]
        return d

    def send(self, text):
        payload = text.encode()
        n = len(payload)
        frame = bytearray([0x81])                      # FIN + text
        mask = os.urandom(4)
        if n < 126:      frame.append(0x80 | n)
        elif n < 65536:  frame.append(0x80 | 126); frame += struct.pack(">H", n)
        else:            frame.append(0x80 | 127); frame += struct.pack(">Q", n)
        frame += mask
        frame += bytes(b ^ mask[i % 4] for i, b in enumerate(payload))
        self.sock.sendall(bytes(frame))

    def recv(self):
        """Return one complete text message (reassembles continuations)."""
        chunks = []
        while True:
            b0, b1 = self._recv_exact(2)
            fin, opcode = b0 & 0x80, b0 & 0x0F
            ln = b1 & 0x7F
            if   ln == 126: ln = struct.unpack(">H", self._recv_exact(2))[0]
            elif ln == 127: ln = struct.unpack(">Q", self._recv_exact(8))[0]
            data = self._recv_exact(ln) if ln else b""

            if opcode == 0x8:                       # close
                raise IOError("server closed the websocket")
            if opcode == 0x9:                       # ping -> pong
                self.sock.sendall(bytes([0x8A, 0x80]) + os.urandom(4))
                continue
            if opcode == 0xA:                       # pong
                continue
            chunks.append(data)
            if fin:
                return b"".join(chunks).decode("utf-8", "replace")

    def settimeout(self, t): self.sock.settimeout(t)
    def close(self):
        try: self.sock.close()
        except Exception: pass

# --------------------------------------------------------------- CDP glue
class CDP:
    def __init__(self, ws):
        self.ws = ws
        self.n = 0
        self.events = []

    def call(self, method, params=None, timeout=25):
        self.n += 1
        mid = self.n
        self.ws.send(json.dumps({"id": mid, "method": method, "params": params or {}}))
        self.ws.settimeout(timeout)
        while True:
            msg = json.loads(self.ws.recv())
            if msg.get("id") == mid:
                if "error" in msg:
                    raise RuntimeError(msg["error"].get("message", "cdp error"))
                return msg.get("result", {})
            if "method" in msg:
                self.events.append(msg)

    def evaluate(self, expr, timeout=25):
        r = self.call("Runtime.evaluate", {
            "expression": expr, "returnByValue": True,
            "awaitPromise": True, "userGesture": True}, timeout)
        if r.get("exceptionDetails"):
            d = r["exceptionDetails"]
            raise RuntimeError(d.get("exception", {}).get("description") or d.get("text", "js error"))
        return r.get("result", {}).get("value")

    def pump(self, seconds):
        """Drain events for N seconds, yielding each."""
        import time
        end = time.time() + seconds
        while time.time() < end:
            left = end - time.time()
            if left <= 0: break
            self.ws.settimeout(left)
            try:
                msg = json.loads(self.ws.recv())
            except (socket.timeout, TimeoutError):
                break
            except IOError:
                break
            if "method" in msg:
                yield msg

# ------------------------------------------------------------- JS payloads
JS_FIELDS = r"""
(() => {
  const vis = el => {
    const s = getComputedStyle(el);
    if (el.type === 'hidden') return 'hidden-input';
    if (s.display === 'none') return 'display:none';
    if (s.visibility === 'hidden') return 'visibility:hidden';
    if (s.opacity === '0') return 'opacity:0';
    const r = el.getBoundingClientRect();
    // an input styled width:0;height:0 still measures a few px because of its
    // border/padding, so an exact ===0 test misses the classic squashed field.
    if (r.width < 12 || r.height < 12) return 'tiny(' + Math.round(r.width) + 'x' + Math.round(r.height) + ')';
    if (r.bottom < 0 || r.right < 0) return 'offscreen';
    if (el.closest('[hidden]')) return 'in-[hidden]';
    if (s.clipPath === 'inset(50%)' || s.clip === 'rect(0px, 0px, 0px, 0px)') return 'clipped';
    return 'visible';
  };
  const desc = (el, i) => ({
    n: i,
    tag: el.tagName.toLowerCase(),
    type: (el.type || '').toLowerCase(),
    name: el.name || '',
    id: el.id || '',
    value: (el.value == null ? '' : String(el.value)).slice(0, 200),
    placeholder: el.placeholder || '',
    required: !!el.required,
    disabled: !!el.disabled,
    readonly: !!el.readOnly,
    maxlength: el.maxLength > 0 ? el.maxLength : null,
    pattern: el.pattern || '',
    autocomplete: el.getAttribute('autocomplete') || '',
    state: vis(el),
    form: el.form ? (el.form.name || el.form.id || '#' + [...document.forms].indexOf(el.form)) : null
  });
  const all = [...document.querySelectorAll('input,textarea,select,button')];
  return { url: location.href, title: document.title,
           fields: all.map(desc),
           forms: [...document.forms].map((f, i) => ({
             n: i, name: f.name || '', id: f.id || '',
             action: f.action || '', method: (f.method || 'get').toUpperCase(),
             enctype: f.enctype || '', count: f.elements.length }))
         };
})()
"""

JS_RECON = r"""
(async () => {
  const out = { url: location.href, https: location.protocol === 'https:' };

  // security response headers, read from a same-origin GET of the current URL
  out.headers = {};
  try {
    const r = await fetch(location.href, { credentials: 'include', cache: 'no-store' });
    for (const [k, v] of r.headers) out.headers[k.toLowerCase()] = v;
    out.status = r.status;
  } catch (e) { out.headersError = String(e); }

  // forms: where each posts, over what scheme, and whether a CSRF-ish token rides along
  out.forms = [...document.forms].map((f, i) => {
    const els = [...f.elements];
    let scheme = '';
    try { scheme = new URL(f.action, location.href).protocol; } catch (e) {}
    return {
      n: i, name: f.name || f.id || '',
      method: (f.method || 'get').toUpperCase(),
      action: f.action || '',
      scheme,
      hasToken: els.some(el => /csrf|token|nonce|authenticity|__requestverification/i.test((el.name||'') + '|' + (el.id||''))),
      hasPassword: els.some(el => (el.type||'') === 'password')
    };
  });

  // mixed content: http:// subresources loaded by an https page
  out.mixed = [];
  if (out.https) {
    for (const el of document.querySelectorAll('[src],[href]')) {
      const u = el.getAttribute('src') || el.getAttribute('href') || '';
      if (/^http:\/\//i.test(u)) out.mixed.push(u);
    }
    out.mixed = [...new Set(out.mixed)].slice(0, 25);
  }

  // rough surface signals
  out.inlineHandlers = document.querySelectorAll('[onclick],[onload],[onerror],[onmouseover],[onsubmit]').length;
  out.passwordFields = document.querySelectorAll('input[type=password]').length;
  out.iframes = document.querySelectorAll('iframe').length;
  out.extScripts = [...document.querySelectorAll('script[src]')]
    .map(s => { try { return new URL(s.src, location.href).host; } catch (e) { return ''; } })
    .filter((h, i, a) => h && h !== location.host && a.indexOf(h) === i).slice(0, 20);
  return out;
})()
"""

def _fields(cdp):
    return cdp.evaluate(JS_FIELDS)

def _row(f):
    flags = []
    if f["required"]: flags.append("req")
    if f["disabled"]: flags.append("disabled")
    if f["readonly"]: flags.append("ro")
    if f["maxlength"]: flags.append("max=%d" % f["maxlength"])
    tag = f["tag"] + (("/" + f["type"]) if f["type"] else "")
    ident = f["name"] or f["id"] or "-"
    val = f["value"]
    if len(val) > 48: val = val[:45] + "..."
    return "  %-4s %-22s %-16s %-14s %-11s %s" % (
        "<%d>" % f["n"], ident[:22], tag[:16], f["state"][:14],
        ",".join(flags)[:11], val)

HDR = "  %-4s %-22s %-16s %-14s %-11s %s" % ("#", "name/id", "tag/type", "state", "flags", "value")

# ------------------------------------------------------------------ verbs
def v_forms(cdp, arg):
    d = _fields(cdp)
    head("\n%s" % d["title"]); dim(d["url"])
    if not d["forms"]:
        warn("\nno <form> elements on this page")
    for f in d["forms"]:
        head("\nform <%d>  %s" % (f["n"], f["name"] or f["id"] or "(unnamed)"))
        dim("  %s %s   enctype=%s   %d elements" % (f["method"], f["action"] or "(no action)", f["enctype"], f["count"]))
        mine = [x for x in d["fields"] if x["form"] is not None and
                (x["form"] == (f["name"] or f["id"] or "#%d" % f["n"]))]
        if mine:
            dim(HDR)
            for x in mine: out(_row(x))
    loose = [x for x in d["fields"] if x["form"] is None]
    if loose:
        head("\nloose fields (outside any form)  -- %d" % len(loose))
        dim(HDR)
        for x in loose: out(_row(x))

def v_hidden(cdp, arg):
    """The headline: everything the page is carrying that you cannot see."""
    d = _fields(cdp)
    hid = [x for x in d["fields"] if x["state"] != "visible"]
    head("\nhidden / non-visible fields on %s" % d["url"])
    if not hid:
        ok("  none - every field on this page is visible")
        return
    dim(HDR)
    for x in hid: out(_row(x))
    ok("\n%d hidden of %d total fields" % (len(hid), len(d["fields"])))

def v_probe(cdp, arg):
    d = _fields(cdp)
    head("\nall interactive fields on %s" % d["url"])
    dim(HDR)
    for x in d["fields"]: out(_row(x))
    ok("\n%d fields, %d forms" % (len(d["fields"]), len(d["forms"])))

def v_inspect(cdp, arg):
    if not arg:
        err("usage: inspect <css-selector>   e.g.  inspect input[name=csrf]")
        return
    sel = json.dumps(arg)
    js = r"""
    (() => {
      const el = document.querySelector(%s);
      if (!el) return null;
      const a = {}; for (const at of el.attributes) a[at.name] = at.value;
      const s = getComputedStyle(el), r = el.getBoundingClientRect();
      return { tag: el.tagName.toLowerCase(), attrs: a,
               text: (el.textContent||'').trim().slice(0,300),
               html: el.outerHTML.slice(0, 600),
               box: {x: Math.round(r.x), y: Math.round(r.y),
                     w: Math.round(r.width), h: Math.round(r.height)},
               css: {display: s.display, visibility: s.visibility,
                     opacity: s.opacity, position: s.position, zIndex: s.zIndex} };
    })()""" % sel
    r = cdp.evaluate(js)
    if not r:
        warn("no element matches " + arg); return
    head("\n<%s>" % r["tag"])
    dim("  box   %(x)d,%(y)d  %(w)dx%(h)d" % r["box"])
    dim("  css   " + "  ".join("%s=%s" % kv for kv in r["css"].items()))
    head("  attributes")
    for k, v in r["attrs"].items():
        out("    %-20s %s" % (k, v[:120]))
    if r["text"]:
        head("  text"); out("    " + r["text"])
    head("  html"); dim("    " + r["html"])

def v_fill(cdp, arg):
    parts = arg.split(" ", 1)
    if len(parts) < 2:
        err('usage: fill <css-selector> <value>   e.g.  fill input[name=q] hello')
        return
    sel, val = parts[0], parts[1]
    js = r"""
    (() => {
      const el = document.querySelector(%s);
      if (!el) return 'no-match';
      const set = Object.getOwnPropertyDescriptor(
        el.constructor.prototype, 'value')?.set;
      if (set) set.call(el, %s); else el.value = %s;
      el.dispatchEvent(new Event('input',  {bubbles:true}));
      el.dispatchEvent(new Event('change', {bubbles:true}));
      return 'ok:' + el.value;
    })()""" % (json.dumps(sel), json.dumps(val), json.dumps(val))
    r = cdp.evaluate(js)
    if r == "no-match": warn("no element matches " + sel)
    else: ok(r)

def v_eval(cdp, arg):
    if not arg:
        err("usage: eval <javascript>"); return
    try:
        r = cdp.evaluate(arg)
        out(json.dumps(r, indent=2, ensure_ascii=False) if not isinstance(r, str) else r)
    except RuntimeError as e:
        err(str(e))

def v_url(cdp, arg):
    ok(cdp.evaluate("location.href"))

def v_net(cdp, arg):
    secs = int(arg) if arg.strip().isdigit() else 15
    cdp.call("Network.enable")
    head("\nstreaming network for %ds  (ctrl-c to stop early)" % secs)
    n = 0
    try:
        for msg in cdp.pump(secs):
            m = msg["method"]
            p = msg.get("params", {})
            if m == "Network.requestWillBeSent":
                r = p.get("request", {})
                out("  %s%-6s%s %s" % (C.G, r.get("method", ""), C.X, r.get("url", "")[:150]))
                n += 1
            elif m == "Network.responseReceived":
                r = p.get("response", {})
                dim("      <- %s  %s" % (r.get("status"), r.get("mimeType", "")))
    except KeyboardInterrupt:
        pass
    finally:
        try: cdp.call("Network.disable")
        except Exception: pass
    ok("\n%d requests seen" % n)

def v_console(cdp, arg):
    secs = int(arg) if arg.strip().isdigit() else 15
    cdp.call("Runtime.enable")
    head("\nstreaming console for %ds  (ctrl-c to stop early)" % secs)
    try:
        for msg in cdp.pump(secs):
            if msg["method"] == "Runtime.consoleAPICalled":
                p = msg["params"]
                vals = " ".join(str(a.get("value", a.get("description", "")))
                                for a in p.get("args", []))
                lvl = p.get("type", "log")
                col = C.R if lvl in ("error", "assert") else (C.Y if lvl == "warning" else C.G)
                out("  %s%-7s%s %s" % (col, lvl, C.X, vals[:200]))
            elif msg["method"] == "Runtime.exceptionThrown":
                d = msg["params"]["exceptionDetails"]
                err("  exception  " + (d.get("exception", {}).get("description") or d.get("text", ""))[:300])
    except KeyboardInterrupt:
        pass
    ok("\ndone")

def v_targets(cdp, arg, port=None):
    for i, t in enumerate(list_targets(port)):
        mark = C.G + "*" + C.X if i == 0 else " "
        out(" %s <%d> %-9s %s" % (mark, i, t.get("type", ""), (t.get("title") or "")[:40]))
        dim("        " + (t.get("url") or "")[:110])

def v_recon(cdp, arg):
    """Security posture of the current page: headers, cookie flags, form
    exposure, mixed content. The audit view a defender runs on their own app.
    (Reads the page with one same-origin GET, and cookies over CDP.)"""
    d = cdp.evaluate(JS_RECON)
    head("\nsecurity posture  \u2014  " + d.get("url", ""))
    dim("  scheme: " + ("https" if d.get("https") else "HTTP (cleartext)") +
        ("   status " + str(d["status"]) if d.get("status") else ""))

    # --- security response headers: present vs missing ---
    h = {k.lower(): v for k, v in (d.get("headers") or {}).items()}
    checks = [
        ("content-security-policy", "CSP", "controls what can load/run"),
        ("strict-transport-security", "HSTS", "forces https"),
        ("x-frame-options", "X-Frame-Options", "clickjacking guard"),
        ("x-content-type-options", "X-Content-Type", "MIME-sniff guard"),
        ("referrer-policy", "Referrer-Policy", "referrer leak guard"),
        ("permissions-policy", "Permissions-Policy", "feature gate"),
    ]
    head("\n  response headers")
    if d.get("headersError"):
        warn("    could not read headers: " + d["headersError"])
    for key, label, why in checks:
        if key in h:
            ok("    [+] %-20s %s" % (label, h[key][:70]))
        else:
            warn("    [-] %-20s missing  (%s)" % (label, why))
    srv = h.get("server") or h.get("x-powered-by")
    if srv:
        dim("    server banner: " + srv)

    # --- cookies with real flags, via CDP (sees HttpOnly, unlike document.cookie) ---
    try:
        cdp.call("Network.enable")
        cookies = cdp.call("Network.getCookies").get("cookies", [])
    except Exception as ex:
        cookies = []
    head("\n  cookies (%d)" % len(cookies))
    for c in cookies[:40]:
        flags = []
        if c.get("secure"): flags.append("Secure")
        else: flags.append("!Secure")
        if c.get("httpOnly"): flags.append("HttpOnly")
        else: flags.append("!HttpOnly")
        ss = c.get("sameSite")
        flags.append("SameSite=" + ss if ss else "!SameSite")
        weak = (not c.get("secure")) or (not c.get("httpOnly")) or (not ss)
        line = "    %-24s %s" % (c.get("name", "?")[:24], " ".join(flags))
        (warn if weak else ok)(line)

    # --- forms: scheme + token + password exposure ---
    forms = d.get("forms") or []
    head("\n  forms (%d)" % len(forms))
    for f in forms:
        bits = [f["method"], f.get("scheme", "").rstrip(":") or "?"]
        if f.get("hasPassword"): bits.append("password")
        bits.append("token" if f.get("hasToken") else "NO-token")
        cleartext = f.get("scheme") == "http:"
        risky = cleartext or (f.get("hasPassword") and not f.get("hasToken"))
        label = "    <%d> %-18s %s -> %s" % (
            f["n"], (f.get("name") or "(unnamed)")[:18], " ".join(bits), (f.get("action") or "")[:50])
        (warn if risky else ok)(label)

    # --- surface signals ---
    head("\n  surface")
    dim("    inline event handlers : %d" % d.get("inlineHandlers", 0))
    dim("    password fields       : %d" % d.get("passwordFields", 0))
    dim("    iframes               : %d" % d.get("iframes", 0))
    ext = d.get("extScripts") or []
    if ext:
        dim("    third-party scripts   :")
        for hst in ext: dim("      " + hst)
    if d.get("mixed"):
        warn("\n  MIXED CONTENT (%d http resources on an https page):" % len(d["mixed"]))
        for u in d["mixed"]: warn("    " + u[:90])
    out("")

def v_help(cdp=None, arg=None):
    head("\naloha probe %s  -  page prober over CDP\n" % VERSION)
    out(textwrap.dedent("""\
      page state
        probe                 every input/textarea/select/button on the page
        hidden                only the fields you CANNOT see  (hidden inputs, 0-size,
                              display:none, opacity:0, offscreen)
        forms                 forms with action/method/enctype + their fields,
                              plus fields that belong to no form at all
        inspect <selector>    one element: attributes, box, computed css, outerHTML
        url                   current page url
        recon                 security posture: headers, cookie flags, form
                              exposure, mixed content  (audit your own app)

      acting on the page
        fill <selector> <v>   set a value the way a real user would (native setter +
                              input/change events, so React/Vue notice)
        eval <js>             evaluate javascript, print the result

      streams
        net [secs]            live network requests   (default 15s)
        console [secs]        live console + exceptions (default 15s)

      session
        targets               list CDP targets (tabs)
        attach <n>            attach to another target
        clear                 clear the screen
        quit                  exit
    """))

VERBS = {
    "probe": v_probe, "p": v_probe,
    "hidden": v_hidden, "h": v_hidden,
    "forms": v_forms, "f": v_forms,
    "inspect": v_inspect, "i": v_inspect,
    "recon": v_recon, "r": v_recon, "audit": v_recon,
    "fill": v_fill,
    "eval": v_eval, "e": v_eval,
    "url": v_url,
    "net": v_net,
    "console": v_console,
    "help": v_help, "?": v_help,
}

# ----------------------------------------------------------------- attach
def list_targets(port):
    url = "http://127.0.0.1:%d/json/list" % port
    with urllib.request.urlopen(url, timeout=3) as r:
        data = json.loads(r.read().decode())
    pages = [t for t in data if t.get("type") == "page" and t.get("webSocketDebuggerUrl")]
    return pages or [t for t in data if t.get("webSocketDebuggerUrl")]

def banner(port, target, app="0.5.1", theme="lime"):
    if theme == "devtool":
        # the exact masthead the in-app inspector used to print
        head("Aloha browser %s DevTool" % app)
        err("    PolyForm Noncommercial 1.0.0 -- see LICENSE")
        out()
        ok("console is live \u2014 type JS, or 'forms' / 'help'.")
        dim("attached to 127.0.0.1:%d  \u2014  %s" % (port, (target.get("url") or "")[:70]))
        out()
        return
    head("\n" + "=" * 66)
    head(" aloha probe %s   -   attached to 127.0.0.1:%d" % (VERSION, port))
    out(" " + C.GD + (target.get("title") or "(untitled)")[:60] + C.X)
    dim(" " + (target.get("url") or "")[:64])
    head("=" * 66)
    dim(" type 'help' for verbs, 'hidden' for the thing you came for\n")

def main():
    ap = argparse.ArgumentParser(description="Aloha page prober (CDP console)")
    ap.add_argument("--port", type=int, default=9333)
    ap.add_argument("--once", default=None, help="run one command and exit")
    ap.add_argument("--theme", default="lime", choices=["lime", "devtool"],
                    help="devtool = the old inspector's red-on-black identity")
    ap.add_argument("--app-version", default="0.5.1", dest="app")
    a = ap.parse_args()
    if a.theme == "devtool":
        C.devtool()
        PROMPT[0] = "DevTool> "

    try:
        targets = list_targets(a.port)
    except Exception as e:
        err("\nno CDP endpoint on 127.0.0.1:%d" % a.port)
        dim("  %s" % e)
        warn("\n  Enable \"Embedded DevTools\" in Aloha's Browser Configuration,")
        warn("  then reload a page and start this again.")
        if not a.once: input("\npress enter to close...")
        return 1

    if not targets:
        err("CDP is up but no page target is open. Load a page first.")
        if not a.once: input("\npress enter to close...")
        return 1

    idx = 0
    ws = WS(targets[idx]["webSocketDebuggerUrl"])
    cdp = CDP(ws)

    if a.once:
        run_line(cdp, a.once, a.port)
        return 0

    banner(a.port, targets[idx], a.app, a.theme)
    while True:
        try:
            line = input(C.G + PROMPT[0] + C.X).strip()
        except (EOFError, KeyboardInterrupt):
            out(); break
        if not line:
            continue
        if line in ("quit", "exit", "q"):
            break
        if line == "clear":
            os.system("cls" if os.name == "nt" else "clear"); banner(a.port, targets[idx], a.app, a.theme); continue
        if line == "targets":
            v_targets(cdp, "", a.port); continue
        if line.startswith("attach"):
            try:
                n = int(line.split()[1]); targets = list_targets(a.port)
                ws.close(); ws = WS(targets[n]["webSocketDebuggerUrl"]); cdp = CDP(ws)
                idx = n; banner(a.port, targets[n], a.app, a.theme)
            except Exception as e:
                err("attach failed: %s" % e)
            continue
        run_line(cdp, line, a.port)
    ws.close()
    return 0

def run_line(cdp, line, port):
    verb, _, arg = line.partition(" ")
    fn = VERBS.get(verb.lower())
    if not fn:
        # Not a verb -> it is JavaScript. This is what "type JS, or 'forms' /
        # 'help'" meant in the old console, and it is the behaviour that makes
        # the thing feel like a console rather than a menu.
        return v_eval(cdp, line)
    try:
        fn(cdp, arg.strip())
    except RuntimeError as e:
        err("js: %s" % e)
    except (IOError, OSError) as e:
        err("connection lost: %s" % e)
        raise SystemExit(1)
    except Exception as e:
        err("%s: %s" % (type(e).__name__, e))

if __name__ == "__main__":
    sys.exit(main())
