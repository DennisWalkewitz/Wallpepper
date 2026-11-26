using System.Security.Cryptography;
using System.Text;
using Wallpepper.Settings;

namespace Wallpepper.Rendering;

/// <summary>
/// Compiled bytecode on disk, keyed by the exact source and profile. Compiling a large shader takes the HLSL compiler
/// seconds; with this a shader is compiled once per source text and loads in milliseconds from then on (app start,
/// browsing the library, thumbnails). Safe to call from any thread.
/// </summary>
internal static class CompiledShaderStore
{
    private const int MaxFiles = 400;
    private const uint DxbcMagic = 0x43425844;   // "DXBC"

    public static string KeyFor(string source, string profile)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(profile + "\n" + source));
        return Convert.ToHexString(hash, 0, 16);
    }

    public static byte[]? TryLoad(string key)
    {
        string file = FileFor(key);
        try
        {
            if (!File.Exists(file))
            {
                return null;
            }
            byte[] bytecode = File.ReadAllBytes(file);
            if (bytecode.Length < 32 || BitConverter.ToUInt32(bytecode, 0) != DxbcMagic)
            {
                return null;
            }
            File.SetLastWriteTimeUtc(file, DateTime.UtcNow);   // recently used files survive the clean-up
            return bytecode;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static void Save(string key, byte[] bytecode)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.ShaderCacheDir);
            string temp = FileFor(key) + ".tmp";
            File.WriteAllBytes(temp, bytecode);
            File.Move(temp, FileFor(key), true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Warn("store compiled shader", e);
        }
    }

    /// <summary>Keeps the most recently used files (every slider position of a setting leaves one behind).</summary>
    public static void Prune()
    {
        try
        {
            if (!Directory.Exists(AppPaths.ShaderCacheDir))
            {
                return;
            }
            var files = new DirectoryInfo(AppPaths.ShaderCacheDir).GetFiles().OrderByDescending(file => file.LastWriteTimeUtc).ToList();
            foreach (var file in files.Skip(MaxFiles))
            {
                file.Delete();
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Warn("clean up compiled shaders", e);
        }
    }

    private static string FileFor(string key) => Path.Combine(AppPaths.ShaderCacheDir, key + ".cso");
}
