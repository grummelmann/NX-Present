using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NXPresent;

/// <summary>The update service against a stand-in for GitHub: no network is used and nothing is installed.</summary>
static class UpdateChecks
{
    private const string Base = "https://github.com/grummelmann/NX-Present/releases/download/";

    private sealed class FakeGitHub : HttpMessageHandler
    {
        public readonly Dictionary<string, Func<HttpResponseMessage>> Routes = new();
        public readonly List<string> Requests = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            string address = request.RequestUri!.AbsoluteUri;
            Requests.Add(address);
            return Task.FromResult(Routes.TryGetValue(address, out var make) ? make() : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private sealed class Offline : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation) => throw new HttpRequestException("Kein Netz");
    }

    private static readonly Uri Api = new("https://api.github.com/repos/grummelmann/NX-Present/releases/latest");

    private static string Release(string tag, bool prerelease, params (string Name, string Url, long Size)[] assets) => JsonSerializer.Serialize(new
    {
        tag_name = tag,
        draft = false,
        prerelease,
        assets = assets.Select(a => new { name = a.Name, browser_download_url = a.Url, size = a.Size }).ToArray()
    });

    private static HttpResponseMessage Json(string text) => new(HttpStatusCode.OK) { Content = new StringContent(text, Encoding.UTF8, "application/json") };
    private static HttpResponseMessage Bytes(byte[] data) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(data) };
    private static string Hash(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    private static async Task<Exception?> Thrown(Func<Task> action)
    {
        try { await action(); return null; }
        catch (Exception ex) { return ex; }
    }

    public static async Task Run(Action<bool, string> check, string folder)
    {
        // ---- version tags, addresses, checksum lists ----
        check(Updater.TryParseVersion("v0.4.1", out var v) && v == new Version(0, 4, 1), "Update: release tag with v is read");
        check(Updater.TryParseVersion("0.5.0", out v) && v == new Version(0, 5, 0), "Update: release tag without v is read");
        foreach (var bad in new[] { "v0.4.1-beta", "v0.4.1+build5", "v1.2", "latest", "", "v0.4.1.2", "v-1.0.0" })
            check(!Updater.TryParseVersion(bad, out _), "Update: tag \"" + bad + "\" is not offered");
        check(Updater.Normalize(new Version(0, 4)) == new Version(0, 4, 0) && Updater.Normalize(new Version(0, 4, 0, 0)) == new Version(0, 4, 0), "Update: assembly versions compare equal to release tags");
        check(Updater.CurrentVersion.Revision <= 0 && Updater.CurrentVersion.Build >= 0, "Update: running version is a plain major.minor.build");

        foreach (var good in new[] { Base + "v0.4.1/NX-Present-Setup-0.4.1-x64.exe", "https://GitHub.com/GRUMMELMANN/nx-present/releases/tag/v0.4.1" })
            check(Updater.IsTrustedUrl(new Uri(good)), "Update: official release address accepted " + good);
        foreach (var evil in new[]
        {
            "http://github.com/grummelmann/NX-Present/releases/download/v0.4.1/a.exe",
            "https://github.com.evil.example/grummelmann/NX-Present/releases/download/v0.4.1/a.exe",
            "https://evil.example/grummelmann/NX-Present/releases/download/v0.4.1/a.exe",
            "https://github.com/someone-else/NX-Present/releases/download/v0.4.1/a.exe",
            "https://github.com/grummelmann/NX-Present/issues/1",
            "https://github.com/grummelmann/NX-Present-Fork/releases/download/v0.4.1/a.exe",
            "https://user:secret@github.com/grummelmann/NX-Present/releases/download/v0.4.1/a.exe",
            "https://github.com:8443/grummelmann/NX-Present/releases/download/v0.4.1/a.exe"
        })
            check(!Updater.IsTrustedUrl(new Uri(evil)), "Update: foreign or unsafe address refused " + evil);
        check(!Updater.IsTrustedUrl(null), "Update: missing address refused");

        string zero = new string('a', 64), other = new string('B', 64);
        var sums = Updater.ParseChecksums("﻿" + zero + "  NX-Present-Setup-0.4.1-x64.exe\r\n# comment\r\nnot a hash  file.exe\r\n" + other + " *Other.zip\r\n");
        check(sums.Count == 2 && sums["NX-Present-Setup-0.4.1-x64.exe"] == zero && sums["Other.zip"] == other.ToLowerInvariant(), "Update: SHA256SUMS.txt is read (BOM, CRLF, binary marker, junk lines)");

        // ---- deciding from the release page ----
        string setup = "NX-Present-Setup-0.4.1-x64.exe";
        var payload = new byte[300_000];
        new Random(5).NextBytes(payload);
        string setupUrl = Base + "v0.4.1/" + setup, sumsUrl = Base + "v0.4.1/SHA256SUMS.txt";
        string complete = Release("v0.4.1", false, (setup, setupUrl, payload.Length), ("SHA256SUMS.txt", sumsUrl, 100));
        var current = new Version(0, 4, 0, 0);
        var fake = new FakeGitHub();
        using (var updater = new Updater(fake, Api, Path.Combine(folder, "updates-decide")))
        {
            fake.Routes[Api.AbsoluteUri] = () => Json(complete);
            var found = await updater.CheckAsync(current);
            check(found.State == UpdateState.Available && found.Offer is { } o && o.Version == new Version(0, 4, 1) && o.SetupName == setup && o.SetupSize == payload.Length && o.SetupUrl.AbsoluteUri == setupUrl && o.ChecksumUrl.AbsoluteUri == sumsUrl, "Update: newer release with setup and checksum is offered");
            check(found.Offer!.ReleasePage.AbsoluteUri == "https://github.com/grummelmann/NX-Present/releases/tag/v0.4.1" && Updater.IsTrustedUrl(found.Offer.ReleasePage), "Update: release notes page is built from the tag");
            check(fake.Requests.Count == 1 && fake.Requests.All(x => x == Api.AbsoluteUri), "Update: looking for an update only asks the release API");

            fake.Routes[Api.AbsoluteUri] = () => Json(Release("v0.4.0", false));
            check((await updater.CheckAsync(current)).State == UpdateState.UpToDate, "Update: same version (0.4.0 and 0.4.0.0) is up to date");
            fake.Routes[Api.AbsoluteUri] = () => Json(Release("v0.3.1", false));
            check((await updater.CheckAsync(current)).State == UpdateState.UpToDate, "Update: older release is never offered as a downgrade");
            fake.Routes[Api.AbsoluteUri] = () => Json(Release("v0.4.1", true, (setup, setupUrl, 10), ("SHA256SUMS.txt", sumsUrl, 10)));
            check((await updater.CheckAsync(current)).State == UpdateState.UpToDate, "Update: pre-releases are ignored");
            fake.Routes[Api.AbsoluteUri] = () => Json(Release("v0.4.1", false, ("SHA256SUMS.txt", sumsUrl, 10)));
            check((await updater.CheckAsync(current)).State == UpdateState.Failed, "Update: release without setup file is not offered");
            fake.Routes[Api.AbsoluteUri] = () => Json(Release("v0.4.1", false, (setup, setupUrl, payload.Length)));
            check((await updater.CheckAsync(current)).State == UpdateState.Failed, "Update: release without checksum list is not offered");
            fake.Routes[Api.AbsoluteUri] = () => Json(Release("v0.4.1", false, (setup, "https://evil.example/" + setup, payload.Length), ("SHA256SUMS.txt", sumsUrl, 10)));
            check((await updater.CheckAsync(current)).State == UpdateState.Failed, "Update: setup address outside the official releases is refused");
            fake.Routes[Api.AbsoluteUri] = () => Json(Release("v0.4.1", false, (setup, setupUrl, 0), ("SHA256SUMS.txt", sumsUrl, 10)));
            check((await updater.CheckAsync(current)).State == UpdateState.Failed, "Update: setup without a plausible size is refused");
            fake.Routes[Api.AbsoluteUri] = () => Json(Release("v0.4.1", false, ("NX-Present-Setup-9.9.9-x64.exe", Base + "v0.4.1/NX-Present-Setup-9.9.9-x64.exe", 100), ("SHA256SUMS.txt", sumsUrl, 10)));
            check((await updater.CheckAsync(current)).State == UpdateState.Failed, "Update: a setup whose name does not match the tag is not accepted");
            fake.Routes[Api.AbsoluteUri] = () => Json("[1,2,3]");
            check((await updater.CheckAsync(current)).State == UpdateState.Failed, "Update: unexpected JSON shape is a failure");
            fake.Routes[Api.AbsoluteUri] = () => Json("<html>rate limited</html>");
            check((await updater.CheckAsync(current)).State == UpdateState.Failed, "Update: non-JSON answer is a failure");
            fake.Routes[Api.AbsoluteUri] = () => new HttpResponseMessage(HttpStatusCode.Forbidden);
            check((await updater.CheckAsync(current)).State == UpdateState.Failed, "Update: GitHub rate limit is reported");
            fake.Routes[Api.AbsoluteUri] = () => new HttpResponseMessage(HttpStatusCode.NotFound);
            check((await updater.CheckAsync(current)).State == UpdateState.UpToDate, "Update: no release yet is not an error");
            fake.Routes[Api.AbsoluteUri] = () => new HttpResponseMessage(HttpStatusCode.InternalServerError);
            check((await updater.CheckAsync(current)).State == UpdateState.Failed, "Update: server error is a failure");
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            check(await Thrown(() => updater.CheckAsync(current, cancelled.Token)) is OperationCanceledException, "Update: a cancelled search is cancelled, not reported as failure");
        }
        using (var offline = new Updater(new Offline(), Api, Path.Combine(folder, "updates-offline")))
        {
            var result = await offline.CheckAsync(current);
            check(result.State == UpdateState.Failed && result.Message.Contains("Kein Netz"), "Update: missing connection is reported");
        }

        // ---- downloading and verifying ----
        string target = Path.Combine(folder, "updates-download");
        var download = new FakeGitHub();
        download.Routes[Api.AbsoluteUri] = () => Json(complete);
        download.Routes[sumsUrl] = () => Json(Hash(payload) + "  " + setup + "\r\n");
        download.Routes[setupUrl] = () => Bytes(payload);
        using (var updater = new Updater(download, Api, target))
        {
            var offer = (await updater.CheckAsync(current)).Offer!;
            double last = 0;
            int reports = 0;
            string path = await updater.DownloadAsync(offer, new ImmediateProgress(x => { last = x; reports++; }), CancellationToken.None);
            check(path == Path.Combine(target, setup) && File.ReadAllBytes(path).SequenceEqual(payload), "Update: verified setup is stored byte for byte");
            check(!File.Exists(path + ".part") && reports > 1 && last == 1.0, "Update: download reports progress up to 100 % and leaves no partial file");

            download.Routes[sumsUrl] = () => Json(new string('0', 64) + "  " + setup);
            File.Delete(path);
            var wrong = await Thrown(() => updater.DownloadAsync(offer, null, CancellationToken.None));
            check(wrong is InvalidDataException && !File.Exists(path) && !File.Exists(path + ".part"), "Update: a file that does not match the checksum is deleted and not offered for installation");

            download.Routes[sumsUrl] = () => Json(Hash(payload) + "  Some-Other-File.exe");
            check(await Thrown(() => updater.DownloadAsync(offer, null, CancellationToken.None)) is InvalidDataException && !File.Exists(path), "Update: checksum list without entry for the setup stops the update");

            download.Routes[sumsUrl] = () => Json(Hash(payload) + "  " + setup);
            download.Routes[setupUrl] = () => Bytes(payload[..^1000]);
            check(await Thrown(() => updater.DownloadAsync(offer, null, CancellationToken.None)) is InvalidDataException && !File.Exists(path) && !File.Exists(path + ".part"), "Update: truncated download is rejected");

            download.Routes[setupUrl] = () => Bytes(payload.Concat(new byte[10]).ToArray());
            check(await Thrown(() => updater.DownloadAsync(offer, null, CancellationToken.None)) is InvalidDataException && !File.Exists(path), "Update: oversized download is rejected");

            download.Routes[setupUrl] = () => new HttpResponseMessage(HttpStatusCode.NotFound);
            check(await Thrown(() => updater.DownloadAsync(offer, null, CancellationToken.None)) is HttpRequestException && !File.Exists(path), "Update: missing file on the server is an error");

            var forged = offer with { SetupUrl = new Uri("https://evil.example/" + setup) };
            int before = download.Requests.Count;
            check(await Thrown(() => updater.DownloadAsync(forged, null, CancellationToken.None)) is InvalidDataException && download.Requests.Count == before, "Update: a forged address is refused before any request is made");
            var renamed = offer with { SetupName = "..\\evil.exe" };
            check(await Thrown(() => updater.DownloadAsync(renamed, null, CancellationToken.None)) is InvalidDataException, "Update: a setup name that is not the expected file name is refused");

            download.Routes[setupUrl] = () => Bytes(payload);
            using var stop = new CancellationTokenSource();
            stop.Cancel();
            check(await Thrown(() => updater.DownloadAsync(offer, null, stop.Token)) is OperationCanceledException && !File.Exists(path), "Update: cancelling a download leaves nothing behind");

            // ---- the installer is only started from the update folder ----
            string outside = Path.Combine(folder, "outside.exe");
            File.WriteAllText(outside, "x");
            check(Thrown(() => Task.Run(() => updater.LaunchInstaller(outside, offer.Version))).Result is InvalidOperationException, "Update: the installer is only started from the update folder");

            // ---- clean-up ----
            Directory.CreateDirectory(target);
            File.WriteAllText(Path.Combine(target, "old.exe"), "x");
            File.WriteAllText(Path.Combine(target, "old.exe.part"), "x");
            File.WriteAllText(Path.Combine(target, "setup-0.4.1.log"), "x");
            updater.CleanUp();
            check(!File.Exists(Path.Combine(target, "old.exe")) && !File.Exists(Path.Combine(target, "old.exe.part")) && File.Exists(Path.Combine(target, "setup-0.4.1.log")), "Update: clean-up removes old setups and keeps the installer log");
            using var missing = new Updater(download, Api, Path.Combine(folder, "updates-never-created"));
            missing.CleanUp();
            check(true, "Update: clean-up without an update folder does nothing");
        }

        // ---- settings ----
        var defaults = new AppSettings();
        check(defaults.CheckUpdatesAtStart && defaults.AutoInstallUpdates && defaults.UpdateAttempt == null, "Update: searching and installing at start are on by default");
        string oldFile = Path.Combine(folder, "settings-before-updates.json");
        File.WriteAllText(oldFile, "{\"Version\":1,\"Volume\":55}");
        var loaded = SettingsStore.Load(oldFile);
        check(loaded.Volume == 55 && loaded.CheckUpdatesAtStart && loaded.AutoInstallUpdates, "Update: settings files from before the updater keep working and get the defaults");
        string newFile = Path.Combine(folder, "settings-with-updates.json");
        SettingsStore.Save(new AppSettings { CheckUpdatesAtStart = false, AutoInstallUpdates = false, UpdateAttempt = "0.4.1" }, newFile);
        var roundtrip = SettingsStore.Load(newFile);
        check(!roundtrip.CheckUpdatesAtStart && !roundtrip.AutoInstallUpdates && roundtrip.UpdateAttempt == "0.4.1", "Update: update preferences and the last attempt survive a restart");
    }

    /// <summary>Progress that reports on the calling thread at once, so that a check does not depend on a synchronization context.</summary>
    private sealed class ImmediateProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }
}
