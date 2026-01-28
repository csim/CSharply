using System.Collections.Concurrent;
using Microsoft.Extensions.FileSystemGlobbing;

namespace CSharply;

public class IgnoreFileService
{
   );
 
  

    public bool Ignore(FileInfo file)
    {
        if (!file.Exists)
            return true;

        IgnoreInfo ignoreInfo = GetIgnoreInfo(file.Directory!, []);

        if (ignoreInfo.Globs.Count == 0)
            return false;

        Matcher matcher = new();

        foreach (string glob in ignoreInfo.Globs)
        {
            string trimmedGlob = glob.Trim();

            if (string.IsNullOrWhiteSpace(trimmedGlob) || trimmedGlob.StartsWith('#'))
                continue;

            if (trimmedGlob.StartsWith('!'))
                matcher.AddExclude(trimmedGlob[1..]);
            else
                matcher.AddInclude(trimmedGlob);
        }

        string relativePath = Path.GetRelativePath(ignoreInfo.Directory.FullName, file.FullName);
        relativePath = relativePath.Replace('\\', '/');
        PatternMatchingResult result = matcher.Match(relativePath);

        return result.HasMatches;
    }

    private IgnoreInfo GetIgnoreInfo(DirectoryInfo directory, List<DirectoryInfo> triedDirectories)
    {
        if (_globCache.TryGetValue(directory.FullName, out IgnoreInfo? info))
            return info;

        FileInfo file = new(Path.Combine(directory.FullName, ".csharplyignore"));
        if (file.Exists)
        {
            List<string> globs = File.ReadLines(file.FullName).ToList();
            info = new(Directory: directory, Globs: globs);
            triedDirectories.ForEach(d => _globCache[d.FullName] = info);
            triedDirectories.Clear();

            return _globCache[file.Directory!.FullName] = info;
        }

        if (directory.Parent == null)
            return _globCache[directory.FullName] = new(Directory: directory, Globs: []);

        triedDirectories.Add(directory);

        return GetIgnoreInfo(directory.Parent, triedDirectories);
    }

    private sealed record IgnoreInfo(DirectoryInfo Directory, IReadOnlyList<string> Globs);

    private readonly ConcurrentDictionary<string, IgnoreInfo> _globCache = new(
   StringComparer.OrdinalIgnoreCase


    public IgnoreFileService() { }

}
