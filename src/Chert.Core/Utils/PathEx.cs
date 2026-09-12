namespace Chert.Core.Utils;

/// <summary>.minecraft 目录约定与路径计算。</summary>
public static class PathEx
{
    public static string VersionsDir(string root) => Path.Combine(root, "versions");
    public static string VersionDir(string root, string id) => Path.Combine(root, "versions", id);
    public static string VersionJsonPath(string root, string id) => Path.Combine(root, "versions", id, $"{id}.json");
    public static string VersionJarPath(string root, string id) => Path.Combine(root, "versions", id, $"{id}.jar");
    public static string LibrariesDir(string root) => Path.Combine(root, "libraries");
    public static string AssetsDir(string root) => Path.Combine(root, "assets");
    public static string AssetsIndexDir(string root) => Path.Combine(root, "assets", "indexes");
    public static string AssetsObjectsDir(string root) => Path.Combine(root, "assets", "objects");
    public static string NativesDir(string root, string id) => Path.Combine(root, "versions", id, "natives");
    public static string CrashReportsDir(string root) => Path.Combine(root, "crash-reports");
    public static string ModsDir(string root) => Path.Combine(root, "mods");
    public static string ShaderPacksDir(string root) => Path.Combine(root, "shaderpacks");
    public static string ResourcePacksDir(string root) => Path.Combine(root, "resourcepacks");
    public static string SavesDir(string root) => Path.Combine(root, "saves");
    public static string FabricMarker(string root, string id) => Path.Combine(root, "versions", id, ".fabric");

    /// <summary>资源对象路径：assets/objects/{hash[0..2]}/{hash}。</summary>
    public static string AssetObjectPath(string root, string hash)
    {
        var prefix = hash.Length >= 2 ? hash[..2] : hash;
        return Path.Combine(root, "assets", "objects", prefix, hash);
    }
}
