# Third-party notices

Wallpepper's own code and shaders are MIT licensed (see `LICENSE`). The bundled library also contains shaders
that were not written for this project:

## Hammster / windows-terminal-shaders (CC0 1.0)

`apollian`, `cloudface`, `complex`, `crt`, `damask`, `dawn`, `demonstration`, `grid`, `happy`, `hardwork`,
`hueshift`, `kodelife`, `neonroad`, `onnet`, `reactor`, `sphere4`, `stars`, `torus`, `transparent`, `twist`,
`velvet-swirl`

come from <https://github.com/Hammster/windows-terminal-shaders>, a collection published under CC0 1.0
(`shaders/LICENSE-hammster-shaders.txt`). Many of them are HLSL ports of shaders by other authors; the
original author, source and licence of each shader and of each borrowed helper function are named in the
comments of the individual files and have been left untouched. In Wallpepper these files only gained a
`// @name` / `// @tags` header. `velvet-swirl` (upstream name `balatro`) additionally had its constants turned
into settings; its header credits the original author and the intermediate edits.

## Libraries

- [Vortice.Windows](https://github.com/amerkoleci/Vortice.Windows) (MIT): Direct3D 11 / DXGI bindings (Windows build)
- [Microsoft.Web.WebView2](https://www.nuget.org/packages/Microsoft.Web.WebView2) (Microsoft licence): web wallpapers (Windows build)

If you are the author of something in here and want it credited differently or removed, please open an issue.
