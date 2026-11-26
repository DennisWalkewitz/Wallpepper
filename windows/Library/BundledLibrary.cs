using System.Reflection;
using System.Security.Cryptography;
using Wallpepper.Settings;

namespace Wallpepper.Library;

/// <summary>
/// The library items that travel inside the exe (shaders, web wallpapers) and how they get into the library folder.
/// Three hashes decide per item: the bundled one, the one last copied, and the file's current one. New items are
/// added, items the user has not touched since they were copied are updated with the app, edited ones are left alone
/// and deleted ones stay deleted.
/// </summary>
internal static class BundledLibrary
{
    private const string ResourcePrefix = "shaders/";

    private static readonly Assembly Exe = Assembly.GetExecutingAssembly();

    private static readonly HashSet<string> Names = new(
        Exe.GetManifestResourceNames()
            .Where(name => name.StartsWith(ResourcePrefix, StringComparison.OrdinalIgnoreCase))
            .Select(name => name.Substring(ResourcePrefix.Length)),
        StringComparer.OrdinalIgnoreCase);

    /// <summary>Identifies this build of the exe; a new build looks at the library again.</summary>
    public static string BuildId => Exe.ManifestModule.ModuleVersionId.ToString("N");

    public static bool Contains(string fileName) => Names.Contains(fileName);

    public static string ReadText(string fileName)
    {
        using Stream stream = Open(fileName);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>Brings the folder up to date once per build. Returns the number of files written.</summary>
    public static int Sync(string folder, Config config)
    {
        if (config.SeededByBuild == BuildId && Directory.Exists(folder))
        {
            return 0;
        }
        int written = Copy(folder, config, restoreMissing: false);
        config.SeededByBuild = BuildId;
        return written;
    }

    /// <summary>Like <see cref="Sync"/>, and puts back bundled items the user deleted. Returns the number of files written.</summary>
    public static int RestoreMissing(string folder, Config config)
    {
        int written = Copy(folder, config, restoreMissing: true);
        config.SeededByBuild = BuildId;
        return written;
    }

    private static int Copy(string folder, Config config, bool restoreMissing)
    {
        int written = 0;
        Directory.CreateDirectory(folder);
        foreach (string name in Names)
        {
            try
            {
                if (CopyItem(folder, name, config.Seeded, restoreMissing))
                {
                    written++;
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Log.Warn("copy bundled " + name, e);
            }
        }
        return written;
    }

    /// <returns>true when the file was written.</returns>
    private static bool CopyItem(string folder, string name, Dictionary<string, string> seeded, bool restoreMissing)
    {
        string target = Path.Combine(folder, name);
        byte[] bundled = ReadBytes(name);
        string bundledHash = Hash(bundled);
        seeded.TryGetValue(name, out string? seededHash);
        if (File.Exists(target))
        {
            string currentHash = Hash(File.ReadAllBytes(target));
            if (currentHash == bundledHash)
            {
                seeded[name] = bundledHash;
                return false;
            }
            if (currentHash != seededHash)
            {
                return false;   // edited by the user (or never tracked): left alone
            }
        }
        else if (seededHash != null && !restoreMissing)
        {
            return false;       // deleted by the user: stays deleted
        }
        File.WriteAllBytes(target, bundled);
        seeded[name] = bundledHash;
        return true;
    }

    private static Stream Open(string fileName) =>
        Exe.GetManifestResourceStream(ResourcePrefix + fileName)
        ?? throw new InvalidOperationException("This build does not contain " + fileName + ".");

    private static byte[] ReadBytes(string fileName)
    {
        using Stream stream = Open(fileName);
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    private static string Hash(byte[] data) => Convert.ToHexString(SHA256.HashData(data), 0, 8);
}
