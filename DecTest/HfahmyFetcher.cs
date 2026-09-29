// Copyright (c) JakeSays
// SPDX-License-Identifier: MIT

using System.IO.Compression;
using System.Security.Cryptography;

namespace Decimals.DecTest;

/// <summary>
/// Downloads the Sayed-Ahmed and Fahmy vector archives, unpacks them, and checks each file
/// against the published md5 sums.
/// </summary>
/// <remarks>
/// The site only works over plain HTTP. Its HTTPS endpoint rejects modern TLS. The md5 sums
/// come from the same site, so they detect a damaged download but not a changed file.
/// </remarks>
public static class HfahmyFetcher
{
    private const string BaseAddress = "http://eece.cu.edu.eg/~hfahmy/arith_debug/";

    private static readonly string[] Archives =
    [
        "2010_07_d64_add",
        "2011_09_d128_add",
        "2010_07_d64_mul",
        "2011_10_d128_mul",
        "2010_07_d64_fma",
        "2011_02_d64_fma",
        "2011_09_d128_fma",
        "2011_03_d64_div",
        "2011_10_d64_div",
        "2011_04_d128_div",
        "2011_11_d128_div",
        "2011_03_d64_sqrt",
        "2011_04_d128_sqrt"
    ];

    public static async Task<int> Fetch(string directory)
    {
        Directory.CreateDirectory(directory);

        using var client = new HttpClient();
        client.Timeout = TimeSpan.FromMinutes(30);

        var failures = 0;
        foreach (var archive in Archives)
        {
            Console.WriteLine($"fetching {archive}");

            var zipPath = Path.Combine(directory, archive + ".zip");
            var sumsPath = Path.Combine(directory, archive + ".md5sum");

            if (!await DownloadFile(client, archive + ".zip", zipPath)
                || !await DownloadFile(client, archive + ".md5sum", sumsPath))
            {
                failures++;
                continue;
            }

            ZipFile.ExtractToDirectory(zipPath, directory, overwriteFiles: true);
            File.Delete(zipPath);

            failures += Verify(directory, sumsPath);
        }

        var summary = failures == 0
            ? $"all {Archives.Length} archives fetched and verified"
            : $"{failures} problems; see above";

        Console.WriteLine(summary);

        return failures == 0
            ? 0
            : 1;
    }

    private static async Task<bool> DownloadFile(HttpClient client, string name, string path)
    {
        using var response = await client.GetAsync(BaseAddress + name, HttpCompletionOption.ResponseHeadersRead);
        if (!response.IsSuccessStatusCode)
        {
            Console.WriteLine($"  {name}: HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
            return false;
        }

        await using var source = await response.Content.ReadAsStreamAsync();
        await using var target = File.Create(path);
        await source.CopyToAsync(target);
        return true;
    }

    /// <summary>
    /// Checks each file listed in an md5sum file. Returns the number of files that are
    /// missing or have the wrong checksum. Paths in the md5sum file are relative to the
    /// directory.
    /// </summary>
    private static int Verify(string directory, string sumsPath)
    {
        var failures = 0;
        foreach (var line in File.ReadLines(sumsPath))
        {
            var parts = line.Split(' ', 2, StringSplitOptions.TrimEntries);
            if (parts.Length != 2)
            {
                continue;
            }

            var path = Path.Combine(directory, parts[1]);
            if (!File.Exists(path))
            {
                Console.WriteLine($"  missing {parts[1]}");
                failures++;
                continue;
            }

            using var stream = File.OpenRead(path);
            var sum = Convert.ToHexStringLower(MD5.HashData(stream));
            if (!string.Equals(sum, parts[0], StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine($"  checksum differs for {parts[1]}");
                failures++;
            }
        }

        return failures;
    }
}
