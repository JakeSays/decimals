// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

using Decimals.Conformance;

namespace Decimals.DecTest;

/// <summary>
/// Writes the committed sample of the Sayed-Ahmed and Fahmy vectors. The sample keeps every
/// hundredth vector of each file, in a file with the same name and set directory.
/// </summary>
/// <remarks>
/// The sample always includes the first vector of each file, so every file and model is
/// covered. Blank lines and known defects are removed before counting. The output always
/// uses LF line endings, so the same full set always produces the same sample.
/// </remarks>
public static class HfahmySampler
{
    private const int Step = 100;

    public static int Write(string fullDirectory, string sampleDirectory)
    {
        if (!Directory.Exists(fullDirectory))
        {
            Console.WriteLine($"error: no full vector set at {fullDirectory}");
            return 1;
        }

        var files = Directory.GetFiles(fullDirectory, "*.txt", SearchOption.AllDirectories)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        var total = 0L;
        foreach (var file in files)
        {
            var relative = Path.GetRelativePath(fullDirectory, file);
            var target = Path.Combine(sampleDirectory, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);

            var name = Path.GetFileName(file);
            var kept = new List<string>();
            var lineNumber = 0;
            var candidate = 0;

            foreach (var rawLine in File.ReadLines(file))
            {
                lineNumber++;
                var line = rawLine.Trim();
                if (line.Length == 0 || HfahmyKnownDefects.TryFind(name, lineNumber, line, out _))
                {
                    continue;
                }

                if (candidate % Step == 0)
                {
                    kept.Add(line);
                }

                candidate++;
            }

            File.WriteAllText(target, string.Join('\n', kept) + "\n");
            total += kept.Count;
        }

        Console.WriteLine($"wrote {total} vectors from {files.Length} files to {sampleDirectory}");
        return 0;
    }
}
