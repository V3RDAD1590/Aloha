# Aloha Browser
[![DOI](https://zenodo.org/badge/1433687943.svg)](https://doi.org/10.5281/zenodo.23652656)

A retro-styled, freedom oriented Windows browser built on **WebView2** (Chromium) in
C# / WinForms. One borderless Win9x-flavoured window, but underneath it is a full
network-routing and inspection workbench: Tor, VPN, proxy cores, an HTTPS intercept
proxy, a CDP probe console, a local CGI server, and pluggable overlay protocols.

Aloha drives external engines (Tor, OpenVPN, Xray/v2ray, the DEEP tool, an IPv7
client) but ships none of their binaries — you drop those in yourself (see
[Runtime dependencies](#runtime-dependencies)). The repository is source only.

> Status: personal project, published as-is. Several subsystems are experimental;
> the [Honest limitations](#honest-limitations) section says which, so nothing here
> over-promises.

---

<img width="1591" height="805" alt="image" src="https://github.com/user-attachments/assets/39c5b17e-76ff-444a-945e-af4532cf9666" />


## Build

Requirements: **.NET Framework 4.8** developer pack, the **.NET SDK** (or Visual
Studio 2022+), and **x64** (the project is x64-only).

```
dotnet build Aloha.csproj -c Release -p:Platform=x64
```

Output lands in `bin\x64\Release\net48\`. NuGet restores the three packages
(`Microsoft.Web.WebView2`, `Titanium.Web.Proxy`, `System.IO.Hashing`;
Portable.BouncyCastle comes in transitively and lights up the extra hash
algorithms).

To run, the machine needs the **Microsoft Edge WebView2 Runtime**
(Evergreen) installed — Aloha checks for it at startup.

---

## Features

### Browsing & window
- Tabbed browsing, each tab its own WebView2 over a shared profile; hidden tabs are
  suspended. A **Tab Board** (`Ctrl+Shift+M`) shows every tab as a grid cell with its
  host, URL and the engine it's routed through (DIRECT / TOR / INTERCEPT / SOCKS5…).
- **Caravan windows**: hold `Shift` when a page opens a new window to spawn an
  isolated secondary window (its own WebView2 environment), up to 4 × 4 tabs.
- **Find in page** (`Ctrl+F`): a native modeless find bar with match-case, whole-word,
  direction and "n of m", replacing Chromium's own.
- **Save page**: `Ctrl+S` opens Aloha's own Save-As (complete / HTML-only / MHTML);
  `Ctrl+Shift+S` drops an instant MHTML snapshot.
- Re-skinned page **context menu**, custom scrollbars (optional), inline-wallpaper
  home page, and a drag-resizable retro frame with a live size + zoom readout.
  I made it so that you can zoom in 1% and zoom out 5000%.
- **Bookmarks** window (`Ctrl+B`): a domain-grouped tree with import/export of
  Firefox backups (`.json` and `.jsonlz4`, LZ4 decoded in-process).
- **Hotkeys**: every shortcut is re-bindable (`Edit > Hotkeys`), saved to
  `Data\hotkeys.cfg`.
- **Profiles**: save/load full settings snapshots; each carries its own WebView2
  profile name, so identities get separate cookies and cache.

Although the build is still in development.

### Privacy & fingerprinting
- **Browser Configuration** panel maps checkboxes to Chromium switches: force WebRTC
  through the proxy (or disable WebRTC entirely as an option), disable DNS prefetch,
  DoH (secure mode + template), no hyperlink auditing, disable domain-reliability
  / background networking / sync / QUIC, and strip referrers. Applying rebuilds the engine.
- **Anti-fingerprint grid snap**: the viewport is rounded down to fixed buckets (a
  "Tor letterbox" 200×100 preset is included).
- **URL hex strip**: a live hex/ASCII dump of the address, flagging look-alike and
  invisible Unicode (IDN-homograph / phishing check) in red.
- **Favicons off by default**, and "off" blocks the request itself (403) to kill the
  favicon supercookie.
- **Popup control**: every `window.open` is captured; the default verdict blocks
  gesture-less, off-scheme, sized and burst popups, and allowed ones open as tabs. A
  **Blocked popups** queue lets you Allow / Deny after the fact.
- **Security menu**: a bridge guard that pins the config page's origin; a "sealed
  egress" gate; and a renderer **firewall lock** that adds a Windows Firewall
  outbound-block rule for the WebView2 renderer (see limitations).

### Tor
- **Tor** window runs a bundled `tor.exe`: an editable `torrc` with syntax colouring,
  live circuit view, bootstrap log, NEWNYM, verify-config, and a one-click **Route**
  that points the engine at the torrc's SOCKS port.
- **ADVOR** ("Advanced Onion Router"): a controller for a running Tor over its control
  port — a consensus relay library with country/bandwidth, live circuit building
  (pick entry/exit/exclude nodes, StrictNodes, hand-built paths), auto-NEWNYM, bridges,
  FascistFirewall, tuning knobs, `MAPADDRESS`, **onion services** (`ADD_ONION`, keep
  the key), and a relay-mode panel. Plus a **striped fetch** that pulls one file in
  2–16 byte-range stripes, each over its own isolated circuit, and reassembles it.
- **Sealed tor**: a second, independent Tor instance (its own data dir, SOCKS 9070 with
  one-circuit-per-site isolation, control 9071, DNS 9073, optional exit country). When
  routed, Aloha reads back which circuit carried each page and shows the path and RTT
  as the address-bar tooltip.

<img width="938" height="466" alt="image" src="https://github.com/user-attachments/assets/df701bd5-b831-4c01-99b0-25a34227d126" />


### Proxies, DNS & VPN
- **Proxy** panel: single / per-scheme / PAC / system / auto / direct modes, bypass
  list, save/load profiles, and presets (Tor, Direct, Sealed tor). Each profile name
  becomes a separate WebView2 identity.
- **Network** panel: DoH endpoint, host-resolver rules (`MAP host ip`), user-agent
  override.
- **VPN Gate**: fetches the public VPN Gate relay list, filters/sorts it, and connects
  via a bundled `openvpn.exe` (elevated); live throughput read from the tunnel. A
  "Change Network IP" button cycles a requested tunnel IP (honoured only by cooperating
  servers). Child processes are tied to Aloha's lifetime.
- **V2Ray / Xray**: fetch subscription or curated public lists, parse
  vmess/vless/trojan/shadowsocks (ws/grpc/h2, TLS/REALITY), edit the JSON, and connect
  through a local SOCKS inbound with the engine pointed at it.

### Overlay protocols
- **IPv7**: Aloha is an HTTP client of an external IPv7 gateway on `localhost:8888`.
  The IPv7 window polls the gateway's `status.json` (session, address, uptime,
  pages served/failed, a sites list) and can start the client. `ipv7://name/path`
  rewrites to the gateway.
- **[DEEP protocol](https://github.com/flozdrelx/DEEP-protocol)**: `deep://authority/path` is served by running an external
  `deep.exe request`, with the request marshalled as JSON over stdin/stdout. One
  virtual origin per authority. (The DEEP tool itself is a separate project — supply
  your own `deep\bin\deep.exe`.)

### Inspection & developer tools

<img width="1745" height="740" alt="image" src="https://github.com/user-attachments/assets/09400998-f1c9-44fa-b987-7a467e838a0b" />

- **Live headers**: a passive feed of every request/response header.
- **Intercept proxy**: an HTTPS-intercepting MITM proxy (Titanium.Web.Proxy) that can
  chain to an upstream SOCKS/HTTP proxy; creates and trusts a local root CA on first use.
- **Request tamper**: a rule console — `block`, `redirect`, `setheader`, `rmheader`,
  `respond` on URL-substring matches.
- **Raw TCP listener**: binds a port and hex-dumps every byte received.
- **Site map** / **Network Map**: the hosts a page pulled from (resolved to IPs), and a
  live NORAD-style map of the real WebView2 process tree and per-PID TCP endpoints.
  Clicking a process can attach **Frida** to the renderer for threads/registers/backtraces
  (needs Frida and `--no-sandbox`).
- **DevTool probe console**: a real cmd.exe console (pure-stdlib Python, its own
  CDP/WebSocket client) with verbs like `probe`, `hidden`, `forms`, `recon`,
  `eval <js>`, `net`, `console`. Native Edge DevTools are also available.
- **Console**: a green man-page terminal with OSINT helpers — `archive`, `wayback`,
  `discover` (crt.sh / sitemap / robots), and an HTTP request crafter.

### Downloads & media
- Aloha's own download toast with pause/resume, auto-resume-with-backoff on transient
  failures, and per-download **VirusTotal** (local SHA-256 → opens the report; no upload)
  and **EXIF** buttons. A live "scope" graph opens a **chunk inspector** (byte range,
  magic bytes, Shannon entropy, histogram, hex) for any download.
- **Downloads Manager** (`Ctrl+J`): searches history and disk together with a plain-word
  query language (time/date ranges, sizes, types, states, `site:`), drag-out (including
  drag-as-zip), and EXIF/VirusTotal actions.
- **EXIF viewer/stripper** (no external libraries): reads JPEG/PNG/WebP metadata, flags
  identifying tags, and strips to a byte-identical copy (no re-encode); map GPS to
  OpenStreetMap.
- **Media card**: hover any image/video/file link for its real type (magic bytes), size,
  dimensions, entropy and an extension-mismatch warning; click for a **hashes sheet**
  computing anything from MD5/SHA-2 to SHA-3/Keccak, SHAKE, BLAKE2, RIPEMD-160,
  Whirlpool, Tiger, Skein, Streebog, SM3, xxHash and CRC variants, each on demand.

### Cutscenes (novelty)
- Native full-screen "scenes" triggered by host + local-clock rules.
  It is used for aesthetic choices per my intranet sites.

---

## Local service: CGI mounts

<img width="1280" height="612" alt="image" src="https://github.com/user-attachments/assets/390a5635-0f5e-494d-a040-0aa0a1bb9633" />

**Options › CGI mounts…** runs a tiny NCSA-style **cgi-bin server** so you can serve and
run scripts locally, without admin rights.

- **Mount any folder** (or `scan Data\cgi` to mount each subfolder; `demo` writes a
  sample). Mounts last for the session only.
- It's a raw `TcpListener` on **127.0.0.1 only** (no `http.sys`, so no admin). Each
  mount can be **isolated** (its own port from 8801, its own browser origin) or
  **shared** (one port from 8800 at `/<name>/`).
- A directory serves its `index.*` or a green autoindex; `?_ls` forces the listing,
  `?_src` shows a script's source instead of running it.
- **Interpreters by extension** (must be on `PATH`): `.py`→python, `.php`→php,
  `.pl`→perl, `.rb`→ruby, `.js`→node, `.jar`/`.class`→java, `.exe`/`.bat`/`.cmd` run
  directly.
- Full **CGI/1.1 environment** (`REQUEST_METHOD`, `QUERY_STRING`, `SCRIPT_NAME`,
  `PATH_INFO`, `HTTP_*`, `CONTENT_TYPE/LENGTH`), request body on stdin, CGI-header
  parsing on stdout.
- Guards: loopback-only, path-traversal check, 64 KiB header / 64 MiB body caps, 15 s
  socket / 30 s script timeouts, `Cache-Control: no-store`, child processes tied to
  Aloha's alive process.

> **Security note:** CGI scripts run as your user with **no authentication or sandbox**,
> and the loopback ports are reachable by any page open in the browser (which can POST to
> them). Mount only code you trust, and treat the ports as powerful.

---

## Local service: `aloha://config`

<img width="1593" height="810" alt="image" src="https://github.com/user-attachments/assets/ff228a01-747f-4eee-9757-04c30c01f17a" />


Type **`aloha://config`** (or `Edit › WebView config`) for a built-in Chromium **flag
explorer**. The exe folder is served over an internal `config.aloha` virtual host and
the page talks to the browser through a guarded message bridge.

- Search and browse Chromium **switches and features** by category (Networking, Privacy,
  Fingerprinting, Security, WebRTC, GPU, Media…), each with its switch, default,
  Chromium source reference and description, plus a caution on dangerous ones
  (`--no-sandbox`, `--disable-web-security`, `ignore-certificate-errors`…).
- Turn switches on / set a value, enable/disable features (collected into
  `--enable-features=` / `--disable-features=`), see the composed **Launch:** line, and
  **Apply** (saved to the config and appended to the engine's arguments on rebuild).
- **Export** your flag set to `Data\exports\`, reset, or favourite entries.

The full catalog ships as an optional `chromium_flags.json` next to the exe (~29k
entries harvested from Chromium source). **It is not committed** (it's large and
Chromium-derived); without it the page falls back to a small built-in sample. Drop your
own copy beside the exe for the full catalog.

---

## Internal URL schemes

<img width="1280" height="693" alt="image" src="https://github.com/user-attachments/assets/999ba87e-a035-4c9e-8c24-150e06eb757d" />


| Typed | Goes to |
|---|---|
| `aloha://config` | the flag explorer (`config.aloha`) |
| `about:blank` / `aloha://blank` / `aloha:home` | the home page |
| `ipv7://name/path` for TP/IX | `http://localhost:8888/IPv7:name/path` for the domain gateway) |
| `deep://authority/path` | `https://authority.deep.aloha/path` (served by `deep.exe`) |
| text with a dot | `http://…` · everything else is a search |

`about:` `view-source:` `data:` `file:` `blob:` `chrome:` `edge:` pass straight through.

---

## Key shortcuts

`Ctrl+T` new tab · `Ctrl+W` close · `Ctrl+Shift+M` tab board · `Ctrl+F` find ·
`Ctrl+B` bookmarks · `Ctrl+J` downloads · `Ctrl+L` focus address · `Ctrl+S` save ·
`Ctrl+Shift+S` MHTML snapshot · `Ctrl+ +/-/0` zoom · `F5` reload · `Alt+←/→` back/forward
· `F12` probe console · `F1` help. All rebindable in `Edit › Hotkeys`.

---

## Runtime dependencies

Aloha ships **no** third-party binaries. Install/drop in what you want to use:

| For | Provide |
|---|---|
| anything | **Microsoft Edge WebView2 Runtime** (Evergreen) |
| Tor / ADVOR | a Tor Expert Bundle in `tor\` (`tor\tor.exe`, geoip files) |
| Sealed tor | a second Tor bundle in `torrc7\tor\` + `torrc7\data\` geoip |
| VPN Gate | `openvpn\bin\openvpn.exe` (+ a TAP/Wintun adapter installed) |
| Xray / v2ray | `xray\xray.exe` / `v2ray\v2ray.exe` (+ geo `.dat`) |
| DEEP | a DEEP release in `deep\` (`deep\bin\deep.exe` + `config.json`) |
| IPv7 | an IPv7 client build in `ipv7\` |
| Frida inspection | `frida` on `PATH` (`pip install frida-tools`) + `--no-sandbox` |
| CGI scripts | the interpreters you use (python/php/node/…) on `PATH` |

The `.csproj` already has copy rules for `tor\`, `torrc7\`, `openvpn\`, `xray\`,
`v2ray\`, `deep\`, `ipv7\` — populate the folders and they ship next to the exe. All are
git-ignored.

---

## Files Aloha writes

Runtime state lives in `Data\` next to the exe (or `%LocalAppData%\Aloha\` if that's not
writable): `aloha.cfg` (settings), `profile\` (WebView2 data), `tor-data\`, `favicons\`,
`hotkeys.cfg`, `cutscenes\`, `exports\`, logs. Bookmarks, download history and presets
sit next to the exe. All of it is git-ignored.

---

## Honest limitations

Published as-is, so the sharp edges are documented rather than hidden:

- A few **Browser Configuration toggles are no-ops** ("disable speculative preconnect",
  "disable Safe Browsing", "spoof media devices" emit no switch; proxy-auth fields and
  the DNS-mode selector are stored but unused).
- **"Sealed egress" is narrower than it sounds** — it only constrains the intercept
  proxy / fetchers, not ordinary WebView2 browsing, unless the intercept proxy is
  running. The renderer firewall rule targets the shared WebView2 runtime, so it affects
  every WebView2 app on the machine.
- Some helpers **go direct even under Tor**: the Console's HTTP client with a SOCKS
  proxy, the VPN/core list fetches, and the Site/Network map DNS lookups (system
  resolver). The automatic `.onion` search only triggers for SOCKS on port 9050.
- The **media-card bridge** can be messaged by any page (by design), which means a page
  can ask for type/size/hash of **local file paths**.
- This is a hobbyist security tool, **not audited**. Don't rely on it to protect anyone
  at real risk.

---

## License

**PolyForm Noncommercial 1.0.0** — see [LICENSE](LICENSE). You may use, modify and
share Aloha for any non-commercial purpose (personal, hobby, research, education);
commercial use is not granted. It covers the Aloha source only — the third-party
components are listed in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md), and the
external engines you install carry their own licenses.
