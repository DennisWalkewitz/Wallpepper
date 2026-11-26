using System.Runtime.InteropServices;

namespace Wallpepper.Rendering;

/// <summary>The constant buffer at register(b0): matches Windows Terminal's "PixelShaderSettings", 32 bytes.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct PixelShaderSettings
{
    public const int SizeInBytes = 32;

    public float Time;
    public float Scale;
    public float ResolutionX;
    public float ResolutionY;
    public float BackgroundR;
    public float BackgroundG;
    public float BackgroundB;
    public float BackgroundA;
}

/// <summary>The constant buffer at register(b1): the monitor layout for multi-monitor shaders, 144 bytes.</summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct MonitorConstants
{
    public const int SizeInBytes = 144;
    public const int MaxMonitors = 8;

    /// <summary>x, y, width, height per monitor in span UV.</summary>
    public fixed float Rects[MaxMonitors * 4];
    public float Count;
    public float Index;
    public float Pad0;
    public float Pad1;
}

/// <summary>The constant buffer of the filter pass, 48 bytes.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct FilterConstants
{
    public const int SizeInBytes = 48;

    public float TexelX;
    public float TexelY;
    public float Brightness;
    public float Contrast;
    public float Saturation;
    public float Temperature;
    public float Blur;
    public float Vignette;
    public float Dim;
    public float Pad0;
    public float Pad1;
    public float Pad2;
}

/// <summary>Result of a D3D11 "timestamp disjoint" query.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct DisjointData
{
    public ulong Frequency;
    public int Disjoint;
}
