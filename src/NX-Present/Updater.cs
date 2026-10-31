using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace NXPresent;

public enum UpdateState { UpToDate, Available, Failed }

/// <summary>A newer release whose setup file and checksum list were found on the official release page.</summary>
public sealed record UpdateOffer(Version Version, string SetupName, Uri SetupUrl, long SetupSize, Uri ChecksumUrl, Uri ReleasePage);

public sealed record UpdateCheck(UpdateState State, string Message, UpdateOffer? Offer = null);

/// <summary>
/// Looks for a newer release on the official GitHub release page, downloads its setup and checks it against the
/// published SHA256SUMS.txt. Every address must stay on github.com/grummelmann/NX-Present/releases over HTTPS.
/// The checksum comes from the same release, so it detects damaged or incomplete downloads, not a compromised release.
/// </summary>
public sealed class Updater : IDisposable
{
    public const string Repository = "grummelmann/NX-Present";
    public const string ChecksumAsset = "SHA256SUMS.txt";
    private const long MaxSetupBytes = 1L << 30;
    private const int MaxChecksumBytes = 64 * 1024;
    private const int MaxApiBytes = 2 * 1024 * 1024;

    private static readonly Regex VersionTag = new(@"^[vV]?(\d{1,4})\.(\d{1,4})\.(\d{1,4})$", RegexOptions.CultureInvariant);
    private static readonly Regex ChecksumLine = new(@"^(?<hash>[0-9a-fA-F]{64})[ \t]+\*?(?<name>\S.*?)\s*$", RegexOptions.CultureInvariant);

    private readonly HttpClient http;
    private readonly Uri releaseApi;
    private readonly string folder;

    public static Version CurrentVersion { get; } = Normalize(typeof(Updater).Assembly.GetName().Version ?? new Version(0, 0, 0));

    /// <summary>Only copies that Setup installed can be updated by Setup. A build from source has no uninstaller next to it.</summary>
    public bool CanInstall => File.Exists(Path.Combine(AppContext.BaseDirectory, "unins000.exe"));

    public string DownloadFolder => folder;

    public Updater(HttpMessageHandler? handler = null, Uri? releaseApi = null, string? downloadFolder = null)
    {
        http = handler == null ? new HttpClient() : new HttpClient(handler);
        http.Timeout = TimeSpan.FromSeconds(30);
        http.DefaultRequestHeaders.UserAgent.ParseAdd("NX-Present/" + CurrentVersion.ToString(3) + " (+https://github.com/" + Repository + ")");
        this.releaseApi = releaseApi ?? new Uri("https://api.github.com/repos/" + Repository + "/releases/latest");
        folder = downloadFolder ?? Path.Combine(App.DataDirectory, "Updates");
    }

    public static Version Normalize(Version version) => new(version.Major, version.Minor, Math.Max(version.Build, 0));

    public static string SetupFileName(Version version) => "NX-Present-Setup-" + version.ToString(3) + "-x64.exe";

    /// <summary>Accepts plain release tags such as v0.4.1 or 0.4.1. Anything with a suffix (-beta, +build) is not offered.</summary>
    public static bool TryParseVersion(string? tag, out Version version)
    {
        version = new Version(0, 0, 0);
        if (tag == null) return false;
        var match = VersionTag.Match(tag.Trim());
        if (!match.Success) return false;
        version = new Version(int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value), int.Parse(match.Groups[3].Value));
        return true;
    }

    /// <summary>Download and page addresses must be HTTPS addresses on github.com inside this repository's releases.</summary>
    public static bool IsTrustedUrl(Uri? url)
    {
        if (url == null || !url.IsAbsoluteUri || url.Scheme != Uri.UriSchemeHttps || !url.IsDefaultPort || url.UserInfo.Length != 0) return false;
        if (!string.Equals(url.Host, "github.com", StringComparison.OrdinalIgnoreCase)) return false;
        return url.AbsolutePath.StartsWith("/" + Repository + "/releases/", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Reads lines of the form "&lt;64 hex digits&gt;  &lt;file name&gt;" as written by sha256sum and Build.ps1.</summary>
    public static Dictionary<string, string> ParseChecksums(string text)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string raw in text.TrimStart('﻿').Split('\n'))
        {
            var match = ChecksumLine.Match(raw.TrimEnd('\r'));
            if (match.Success) result[match.Groups["name"].Value] = match.Groups["hash"].Value.ToLowerInvariant();
        }
        return result;
    }

    public async Task<UpdateCheck> CheckAsync(Version current, CancellationToken cancellation = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, releaseApi);
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound) return new(UpdateState.UpToDate, "Es gibt noch keine veröffentlichte Version.");
            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests) return new(UpdateState.Failed, "GitHub nimmt gerade keine weiteren Anfragen an. Versuche es später erneut.");
            if (!response.IsSuccessStatusCode) return new(UpdateState.Failed, "GitHub hat mit Fehler " + (int)response.StatusCode + " geantwortet.");
            string json = await ReadTextAsync(response, MaxApiBytes, cancellation).ConfigureAwait(false);
            return Evaluate(json, current);
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { return new(UpdateState.Failed, "GitHub antwortet nicht (Zeitüberschreitung)."); }
        catch (HttpRequestException ex) { return new(UpdateState.Failed, "Keine Verbindung zu GitHub: " + ex.Message); }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or InvalidOperationException or KeyNotFoundException)
        {
            return new(UpdateState.Failed, "GitHub hat eine unerwartete Antwort geschickt.");
        }
    }

    /// <summary>Turns the JSON of "releases/latest" into a decision. Separate from the download so that it can be checked without a network.</summary>
    public UpdateCheck Evaluate(string json, Version current)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Die Antwort ist kein Objekt.");
        if (Flag(root, "draft") || Flag(root, "prerelease")) return new(UpdateState.UpToDate, "NX-Present " + current.ToString(3) + " ist aktuell.");
        string? tag = root.TryGetProperty("tag_name", out var tagElement) && tagElement.ValueKind == JsonValueKind.String ? tagElement.GetString() : null;
        if (!TryParseVersion(tag, out var version)) return new(UpdateState.Failed, "Die neueste Veröffentlichung hat keine lesbare Versionsnummer (" + (tag ?? "leer") + ").");
        if (version <= Normalize(current)) return new(UpdateState.UpToDate, "NX-Present " + current.ToString(3) + " ist aktuell.");

        string setupName = SetupFileName(version);
        Uri? setupUrl = null, checksumUrl = null;
        long size = 0;
        if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in assets.EnumerateArray())
            {
                string? name = asset.TryGetProperty("name", out var nameElement) && nameElement.ValueKind == JsonValueKind.String ? nameElement.GetString() : null;
                if (name != setupName && name != ChecksumAsset) continue;
                string? address = asset.TryGetProperty("browser_download_url", out var urlElement) && urlElement.ValueKind == JsonValueKind.String ? urlElement.GetString() : null;
                if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || !IsTrustedUrl(uri))
                    return new(UpdateState.Failed, "Die Downloadadresse von " + name + " liegt nicht auf der offiziellen Veröffentlichungsseite. Es wird nichts geladen.");
                if (name == setupName)
                {
                    setupUrl = uri;
                    if (!asset.TryGetProperty("size", out var sizeElement) || !sizeElement.TryGetInt64(out size) || size <= 0 || size > MaxSetupBytes)
                        return new(UpdateState.Failed, "Die Größe von " + setupName + " ist unplausibel.");
                }
                else checksumUrl = uri;
            }
        }
        if (setupUrl == null || checksumUrl == null)
            return new(UpdateState.Failed, "Version " + version.ToString(3) + " ist veröffentlicht, aber " + (setupUrl == null ? setupName : ChecksumAsset) + " fehlt noch.");
        var page = new Uri("https://github.com/" + Repository + "/releases/tag/" + tag!.Trim());
        return new(UpdateState.Available, "Version " + version.ToString(3) + " ist verfügbar.", new UpdateOffer(version, setupName, setupUrl, size, checksumUrl, page));
    }

    /// <summary>Downloads the setup next to the settings, checks it against the published checksum and returns its path. A file that does not match is deleted.</summary>
    public async Task<string> DownloadAsync(UpdateOffer offer, IProgress<double>? progress, CancellationToken cancellation)
    {
        if (!IsTrustedUrl(offer.SetupUrl) || !IsTrustedUrl(offer.ChecksumUrl)) throw new InvalidDataException("Die Downloadadresse liegt nicht auf der offiziellen Veröffentlichungsseite.");
        if (offer.SetupName != SetupFileName(offer.Version)) throw new InvalidDataException("Der Dateiname des Setups passt nicht zur Version.");
        Directory.CreateDirectory(folder);
        string expected = await FetchChecksumAsync(offer, cancellation).ConfigureAwait(false);
        string target = Path.Combine(folder, offer.SetupName);
        string part = target + ".part";
        try
        {
            using var response = await http.GetAsync(offer.SetupUrl, HttpCompletionOption.ResponseHeadersRead, cancellation).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is long announced && announced != offer.SetupSize) throw new InvalidDataException("Die Dateigröße weicht von der Veröffentlichung ab.");
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long total = 0;
            int lastStep = -1;
            await using (var source = await response.Content.ReadAsStreamAsync(cancellation).ConfigureAwait(false))
            await using (var file = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
            {
                var buffer = new byte[81920];
                int read;
                while ((read = await source.ReadAsync(buffer, cancellation).ConfigureAwait(false)) > 0)
                {
                    total += read;
                    if (total > offer.SetupSize) throw new InvalidDataException("Die Datei ist größer als in der Veröffentlichung angegeben.");
                    hash.AppendData(buffer, 0, read);
                    await file.WriteAsync(buffer.AsMemory(0, read), cancellation).ConfigureAwait(false);
                    int step = (int)(total * 200 / offer.SetupSize);
                    if (step != lastStep) { lastStep = step; progress?.Report((double)total / offer.SetupSize); }
                }
            }
            if (total != offer.SetupSize) throw new InvalidDataException("Der Download ist unvollständig.");
            string actual = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            if (!string.Equals(actual, expected, StringComparison.Ordinal)) throw new InvalidDataException("Die Prüfsumme der Datei stimmt nicht mit der Veröffentlichung überein. Sie wird nicht installiert.");
            File.Move(part, target, true);
            return target;
        }
        catch
        {
            try { File.Delete(part); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            throw;
        }
    }

    /// <summary>
    /// Starts the downloaded setup so that it waits for this process to end, installs over the current copy and starts the new version.
    /// The caller releases the single-instance lock before and closes the application right after.
    /// </summary>
    public Process LaunchInstaller(string setupPath, Version version)
    {
        string full = Path.GetFullPath(setupPath);
        if (!string.Equals(Path.GetDirectoryName(full), Path.GetFullPath(folder), StringComparison.OrdinalIgnoreCase) || !File.Exists(full))
            throw new InvalidOperationException("Das Setup liegt nicht im Update-Ordner.");
        string log = Path.Combine(folder, "setup-" + version.ToString(3) + ".log");
        var info = new ProcessStartInfo(full)
        {
            UseShellExecute = false,
            WorkingDirectory = folder,
            Arguments = "/SILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /relaunch=1 /waitpid=" + Environment.ProcessId + " /LOG=\"" + log + "\""
        };
        return Process.Start(info) ?? throw new InvalidOperationException("Das Setup konnte nicht gestartet werden.");
    }

    /// <summary>Removes setups and unfinished downloads of earlier updates. A setup that is still running stays in place.</summary>
    public void CleanUp()
    {
        try
        {
            foreach (string file in Directory.EnumerateFiles(folder))
            {
                string extension = Path.GetExtension(file);
                if (!extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) && !extension.Equals(".part", StringComparison.OrdinalIgnoreCase)) continue;
                try { File.Delete(file); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
        catch (DirectoryNotFoundException) { }
    }

    public void Dispose() => http.Dispose();

    private async Task<string> FetchChecksumAsync(UpdateOffer offer, CancellationToken cancellation)
    {
        using var response = await http.GetAsync(offer.ChecksumUrl, HttpCompletionOption.ResponseHeadersRead, cancellation).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        string text = await ReadTextAsync(response, MaxChecksumBytes, cancellation).ConfigureAwait(false);
        if (!ParseChecksums(text).TryGetValue(offer.SetupName, out var hash)) throw new InvalidDataException("In " + ChecksumAsset + " fehlt der Eintrag für " + offer.SetupName + ".");
        return hash;
    }

    private static async Task<string> ReadTextAsync(HttpResponseMessage response, int limit, CancellationToken cancellation)
    {
        if (response.Content.Headers.ContentLength is long announced && announced > limit) throw new InvalidDataException("Die Antwort ist zu groß.");
        await using var stream = await response.Content.ReadAsStreamAsync(cancellation).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellation).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > limit) throw new InvalidDataException("Die Antwort ist zu groß.");
            buffer.Write(chunk, 0, read);
        }
        return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    private static bool Flag(JsonElement root, string name) => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
}
