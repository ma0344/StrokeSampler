using System;
using System.IO;

namespace SkiaTester.Helpers;

internal static class PathHelpers
{
    internal static string? TryFindRepositoryRoot(string startDirectory)
    {
        if (string.IsNullOrWhiteSpace(startDirectory))
        {
            return null;
        }

        var dir = new DirectoryInfo(startDirectory);
        while (dir != null)
        {
            // Gitリポジトリ判定（通常は.gitディレクトリだが、ワークツリーによってはファイルの場合もある）
            var gitDirPath = Path.Combine(dir.FullName, ".git");
            if (Directory.Exists(gitDirPath) || File.Exists(gitDirPath))
            {
                return dir.FullName;
            }

            // フォールバック: このリポジトリ固有の目印
            if (Directory.Exists(Path.Combine(dir.FullName, "Sample"))
                && Directory.Exists(Path.Combine(dir.FullName, "SkiaTester")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        return null;
    }

    internal static string GetAppBaseDirectory()
        => AppContext.BaseDirectory;
}
