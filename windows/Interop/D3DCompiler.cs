using System.Runtime.InteropServices;

namespace Wallpepper.Interop;

/// <summary>d3dcompiler_47.dll ships with Windows.</summary>
internal static class D3DCompiler
{
    public const uint D3DCOMPILE_OPTIMIZATION_LEVEL3 = 1 << 15;
    /// <summary>D3D_COMPILE_STANDARD_FILE_INCLUDE: "#include" relative to the source file.</summary>
    public static readonly IntPtr StandardFileInclude = new IntPtr(1);

    [ComImport, Guid("8BA5FB08-5195-40e2-AC58-0D989C3A0102"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface ID3DBlob
    {
        [PreserveSig]
        IntPtr GetBufferPointer();

        [PreserveSig]
        UIntPtr GetBufferSize();
    }

    [DllImport("d3dcompiler_47.dll", CharSet = CharSet.Ansi, ExactSpelling = true)]
    public static extern int D3DCompile(
        byte[] source, UIntPtr size, string sourceName, IntPtr defines, IntPtr include,
        string entryPoint, string target, uint flags1, uint flags2, out ID3DBlob? code, out ID3DBlob? errors);
}
