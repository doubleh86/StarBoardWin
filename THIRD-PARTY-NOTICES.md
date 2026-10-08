# Third-party notices

Starboard bundles or builds with the following third-party components. Runtime
assets are local; the application does not fetch these packages from a CDN.

| Component | Version | Use | License |
|---|---:|---|---|
| Microsoft.Web.WebView2 | 1.0.4191.47 | WPF renderer host | BSD-style Microsoft license; see `licenses/Microsoft.Web.WebView2-LICENSE.txt` and `licenses/Microsoft.Web.WebView2-NOTICE.txt` |
| @xterm/xterm | 6.0.0 | terminal emulator | MIT; license copied beside the renderer bundle |
| @xterm/addon-fit | 0.11.0 | terminal sizing | MIT; license copied beside the renderer bundle |
| @xterm/addon-search | 0.16.0 | terminal output search | MIT; license copied beside the renderer bundle |
| @xterm/addon-web-links | 0.12.0 | terminal URL detection | MIT; license copied beside the renderer bundle |
| esbuild | 0.28.2 | build-time bundler only | MIT; see `licenses/esbuild-LICENSE.txt` |
| MSTest | 4.3.3 | test-time only | MIT |

Starboard for Windows independently reimplements the product concept and UX of
[palamim/starboard](https://github.com/palamim/starboard), a macOS project by
Leonardo Palamim Cardozo released under the
[MIT License](https://github.com/palamim/starboard/blob/main/LICENSE).
It is not an official Windows release of that project. Its Swift source and assets
are not copied into this implementation.

Portable packages include the Starboard for Windows MIT `LICENSE` at the archive
root. That product license does not replace the component-specific license files
listed above.
