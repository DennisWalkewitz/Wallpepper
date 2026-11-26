using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

// System DLLs (d3dcompiler_47, dwmapi, winmm ...) are only ever loaded from System32, never from next to the exe.
[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]

[assembly: InternalsVisibleTo("Wallpepper.Tests")]
