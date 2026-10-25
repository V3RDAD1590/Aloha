# Third-party notices

Aloha itself is under the [PolyForm Noncommercial 1.0.0](LICENSE) license. It uses
third-party software in two different ways, with different licensing consequences:

1. **Compiled in** — NuGet packages linked into `Aloha.exe`. Their licenses apply to
   the built binary you distribute. Full texts are in [`licenses/`](licenses/).
2. **Driven as separate programs** — external engines Aloha launches as their own
   processes (Tor, OpenVPN, Xray, …). Aloha ships **none** of these; you install them
   yourself. They are not in this repository, so nothing here redistributes them. If you
   ever publish a **release that bundles their binaries**, that download must carry each
   engine's own license and honour its terms (notably OpenVPN is **GPLv2** — bundling its
   binary has obligations your own license can't override).

---

## Compiled into the build (NuGet)

| Package | License | Copyright | Text |
|---|---|---|---|
| Microsoft.Web.WebView2 | Microsoft Software License Terms | © Microsoft Corporation | <https://www.nuget.org/packages/Microsoft.Web.WebView2/1.0.4022.49/License> |
| Titanium.Web.Proxy | MIT | © Titanium (honfika, justcoding121) | [licenses/Titanium.Web.Proxy-MIT.txt](licenses/Titanium.Web.Proxy-MIT.txt) |
| Portable.BouncyCastle | MIT | © The Legion of the Bouncy Castle Inc. | [licenses/Portable.BouncyCastle-MIT.txt](licenses/Portable.BouncyCastle-MIT.txt) |
| System.IO.Hashing | MIT | © .NET Foundation and Contributors | [licenses/System.IO.Hashing-MIT.txt](licenses/System.IO.Hashing-MIT.txt) |

Running the built app also requires the **Microsoft Edge WebView2 Runtime** (installed
separately by the user, under Microsoft's terms).

## Driven as external programs (not bundled here)

| Component | Used by | License | Project |
|---|---|---|---|
| Tor (Expert Bundle) + lyrebird/obfs4, conjure | Tor, ADVOR, Sealed tor | BSD-3-Clause (Tor); components vary | <https://www.torproject.org> |
| OpenVPN (+ OpenSSL) | VPN Gate | **GPL-2.0** (OpenVPN); Apache-2.0 (OpenSSL 3) | <https://openvpn.net> |
| Xray-core | Xray | MPL-2.0 | <https://github.com/XTLS/Xray-core> |
| Wintun (ships with Xray) | Xray tunnelling | Prebuilt-binary license | <https://www.wintun.net> |
| v2ray-core | V2Ray | MIT | <https://github.com/v2fly/v2ray-core> |
| Frida | INSTR / Network Map | wxWindows / LGPL-like | <https://frida.re> |
| DEEP protocol tool | deep:// | Apache-2.0 | separate project (supply your own release) |

## Data

| Data | In repo? | License / source |
|---|---|---|
| `chromium_flags.json` (optional, not committed) | no | derived from Chromium source — BSD-3-Clause; © The Chromium Authors |
| VPN Gate server list | fetched at runtime | public VPN Gate API (<https://www.vpngate.net>) |
| v2ray/xray public share lists | fetched at runtime | the respective public GitHub lists |

---

Nothing above is an endorsement by those projects. Links are to the canonical sources;
check each for the authoritative, current terms.
