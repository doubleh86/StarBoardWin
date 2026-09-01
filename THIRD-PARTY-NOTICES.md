# Third-party notices

Starboard bundles or builds with the following third-party components. Runtime
assets are local; the application does not fetch these packages from a CDN.

| Component | Version | Use | License |
|---|---:|---|---|
| Microsoft.Web.WebView2 | 1.0.4191.47 | WPF renderer host | BSD-style Microsoft license; see `licenses/Microsoft.Web.WebView2-LICENSE.txt` and `licenses/Microsoft.Web.WebView2-NOTICE.txt` |
| @xterm/xterm | 6.0.0 | terminal emulator | MIT; license copied beside the renderer bundle |
| @xterm/addon-fit | 0.11.0 | terminal sizing | MIT; license copied beside the renderer bundle |
| esbuild | 0.28.2 | build-time bundler only | MIT; see `licenses/esbuild-LICENSE.txt` |
| MSTest | 4.3.3 | test-time only | MIT |

Starboard analyzes the MIT-licensed `palamim/starboard` project as a product
reference. Its Swift source and assets are not copied into this implementation.
