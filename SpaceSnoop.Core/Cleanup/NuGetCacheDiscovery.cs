using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;

namespace SpaceSnoop.Core.Cleanup;

internal static class NuGetCacheDiscovery
{
    internal static readonly IReadOnlyDictionary<NuGetCacheKind, string> Names = new Dictionary<NuGetCacheKind, string>
    {
        [NuGetCacheKind.HttpCache] = "http-cache",
        [NuGetCacheKind.Temp] = "temp",
        [NuGetCacheKind.PluginsCache] = "plugins-cache",
        [NuGetCacheKind.GlobalPackages] = "global-packages",
    };

    internal static async Task<NuGetCacheLocations> DiscoverAsync(CancellationToken token)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        using var identity = WindowsIdentity.GetCurrent();
        using var process = new Process
        {
            StartInfo = new("dotnet")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = home,
            },
        };
        foreach (var argument in new[] { "nuget", "locals", "all", "--list", "--force-english-output" })
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        process.StartInfo.Environment["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1";
        process.StartInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        try
        {
            process.Start();
        }
        catch (Win32Exception exception)
        {
            throw new InvalidOperationException("Не удалось запустить dotnet. Для определения кэшей нужен .NET SDK.", exception);
        }

        var output = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var error = process.StandardError.ReadToEndAsync(CancellationToken.None);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
            {
                Debug.WriteLine(exception);
            }

            token.ThrowIfCancellationRequested();
            throw new TimeoutException("dotnet не ответил за 30 секунд.");
        }

        var stdout = await output;
        var stderr = await error;
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Не удалось определить кэши NuGet: {stderr.Trim()} {stdout.Trim()}");
        }

        return Parse(stdout, $"{identity.User?.Value}|{home}");
    }

    internal static NuGetCacheLocations Parse(string output, string context)
    {
        var paths = new Dictionary<NuGetCacheKind, string>();
        foreach (var line in output.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var (kind, name) in Names)
            {
                if (!line.StartsWith(name + ":", StringComparison.Ordinal))
                {
                    continue;
                }

                var path = line[(name.Length + 1)..].Trim();
                if (!paths.TryAdd(kind, NuGetCleanupService.Normalize(path)))
                {
                    throw new InvalidOperationException($"Неоднозначный путь NuGet: {name}.");
                }
            }
        }

        if (paths.Count != Names.Count)
        {
            throw new InvalidOperationException("dotnet вернул неполный список кэшей NuGet.");
        }

        return new(context, paths);
    }
}
