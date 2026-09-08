using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Security;
using System.Text;
using System.Text.Json;
using Floppy.App;

internal static class Program
{
    private const string Version = "1.6.0";
    private const string Name = "Floppy-1.6.0-Portable.exe";
    private const string Api = "https://api.github.com/repos/easycrashx-nex/Floppy/releases/latest";
    private const string Download = "https://github.com/easycrashx-nex/Floppy/releases/download/v1.6.0/" + Name;
    private const string Checksum = Download + ".sha256";
    private static readonly byte[] Payload = Encoding.UTF8.GetBytes("Floppy isolated update fixture; this is not an executable.");
    private static readonly string Hash = Convert.ToHexString(SHA256.HashData(Payload)).ToLowerInvariant();
    private static int _checks;
    private static void Check(bool condition, string message)
    { _checks++; if (!condition) throw new Exception(message); }
    private static void Reject(Action action, string message)
    {
        bool rejected = false;
        try { action(); }
        catch (Exception error) when (error is IOException or InvalidDataException or JsonException or InvalidOperationException or FormatException or KeyNotFoundException)
        { rejected = true; }
        Check(rejected, message);
    }

    private static async Task<int> Main()
    {
        var cases = new (string Name, Func<Task> Run)[]
        {
            ("stable release parsing, version ordering and complete assets", ParseManifests),
            ("malformed API data fails visibly without an update offer", MalformedApi),
            ("startup check preserves the previous install failure", PreviousInstallFailure),
            ("fixed repository links and allowed HTTPS hosts", UrlPolicy),
            ("checksum text binds its digest to the exact filename", Checksums),
            ("404 and rate limits preserve an idle service", HttpErrors),
            ("download redirects reject foreign hosts and redirect loops", Redirects),
            ("foreign repository redirects are rejected", OffRepositoryRedirect),
            ("verified download reaches only the injected installer", SuccessfulDownload),
            ("digest disagreement, corrupt and truncated downloads never install", InvalidDownloads),
            ("oversized metadata and checksums are bounded", OversizedResponses),
            ("installer preparation failure preserves the running application", InstallerFailure),
            ("overlapping checks use one HTTP operation", SingleFlight),
            ("dispose cancels an active check and prevents future work", CancelCheck),
            ("dispose cancels body download and removes its partial file", CancelDownload)
        };
        int passed = 0;
        foreach (var test in cases)
        {
            try { await test.Run(); passed++; Console.WriteLine("PASS " + test.Name); }
            catch (Exception error) { Console.Error.WriteLine("FAIL " + test.Name + ": " + error.GetType().Name + ": " + error.Message); }
        }
        Console.WriteLine($"{passed}/{cases.Length} update regression groups passed; {_checks} assertions; no real network or installer processes.");
        return passed == cases.Length ? 0 : 1;
    }

    private static async Task PreviousInstallFailure()
    {
        using var fixture = new Fixture(DefaultReply);
        typeof(UpdateService).GetField("_lastInstallFailure", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(fixture.Service, "Die EXE war gesperrt.");
        await fixture.Service.CheckAsync();
        Check(fixture.Service.CanDownload && fixture.Service.Status.Contains("Die EXE war gesperrt.")
            && fixture.Service.Status.Contains("ist verfügbar"), "automatic checking must keep rollback errors visible alongside the new offer");
    }

    private static string Release(string version = Version, bool draft = false, bool prerelease = false,
        bool includeExe = true, bool includeSha = true, string? exeUrl = null, string? shaUrl = null,
        long? size = null, string? digest = null, bool duplicateExe = false, string state = "uploaded")
    {
        string name = "Floppy-" + version + "-Portable.exe";
        string prefix = "https://github.com/" + UpdateService.Repository + "/releases/download/v" + version + "/";
        var exe = new { name, browser_download_url = exeUrl ?? prefix + name, state, size = size ?? Payload.Length, digest };
        var assets = new List<object>();
        if (includeExe) assets.Add(exe);
        if (duplicateExe) assets.Add(exe);
        if (includeSha) assets.Add(new { name = name + ".sha256", browser_download_url = shaUrl ?? prefix + name + ".sha256", state, size = 100 });
        return JsonSerializer.Serialize(new { tag_name = "v" + version, draft, prerelease, assets });
    }

    private static Task ParseManifests()
    {
        var installed = new System.Version(1, 5, 0);
        var offer = UpdateService.ParseRelease(Release(digest: "sha256:" + Hash), installed);
        Check(offer is { FileName: Name } && offer.Version == new System.Version(Version) && offer.Size == Payload.Length,
            "complete stable release must preserve its version and package identity");
        foreach (string version in new[] { "1.4.9", "1.5.0" })
            Check(UpdateService.ParseRelease(Release(version), installed) == null, "older and equal releases must not offer an update");
        foreach (string json in new[] { Release(prerelease: true), Release(draft: true), Release("1.6.0-beta"),
            Release(includeSha: false), Release(includeExe: false), Release(size: 0), Release(size: UpdateService.MaximumDownload + 1),
            Release(duplicateExe: true), Release(state: "new"), Release(digest: "sha256:short") })
            Reject(() => UpdateService.ParseRelease(json, installed), "unstable, ambiguous or incomplete release must be rejected");
        return Task.CompletedTask;
    }

    private static async Task MalformedApi()
    {
        foreach (string json in new[] { "{", "[]", "null", "{}", "{\"draft\":\"false\",\"prerelease\":false}",
            "{\"draft\":false,\"prerelease\":false,\"tag_name\":\"v1.6.0\",\"assets\":null}",
            Release(includeSha: false), Release(prerelease: true) })
        {
            using var f = new Fixture(_ => Reply(json));
            await f.Service.CheckAsync();
            Check(!f.Service.CanDownload && !f.Service.Busy && f.Service.Status.Contains("fehlgeschlagen"),
                "malformed metadata must become a visible failure and clear Busy");
        }
    }

    private static Task UrlPolicy()
    {
        foreach (string url in new[] { "http://github.com/x", "https://github.com:444/x", "https://github.com.evil.invalid/x",
            "https://user:pass@github.com/x", "https://github.com/x#fragment", "https://evil.invalid/x" })
            Check(!UpdateService.AllowedDownloadHost(new Uri(url)), "non-HTTPS, credentials, port, suffix and fragment bypasses must be rejected");
        foreach (string host in new[] { "github.com", "api.github.com", "release-assets.githubusercontent.com", "objects.githubusercontent.com" })
            Check(UpdateService.AllowedDownloadHost(new Uri("https://" + host + "/fixture")), "expected GitHub HTTPS host should be accepted");
        foreach (string url in new[] { Download.Replace("easycrashx-nex/Floppy", "fixture-other/Floppy"),
            Download + "?replacement=1", Download.Replace("github.com/", "github.com.evil.invalid/") })
            Reject(() => UpdateService.ParseRelease(Release(exeUrl: url), new System.Version(1, 5, 0)),
                "release metadata cannot substitute another repository or package URL");
        return Task.CompletedTask;
    }

    private static Task Checksums()
    {
        Check(UpdateService.ParseChecksum("\uFEFF" + Hash.ToUpperInvariant() + "  *" + Name + "\r\n", Name) == Hash,
            "BOM, uppercase checksum and binary marker are supported");
        foreach (string text in new[] { Hash, Hash + "  wrong.exe", Hash + "  ../" + Name,
            Hash + "  " + Name + "\n" + Hash + "  " + Name, new string('g', 64) + "  " + Name })
            Reject(() => UpdateService.ParseChecksum(text, Name), "checksum must have one valid digest and the exact expected filename");
        return Task.CompletedTask;
    }

    private static async Task HttpErrors()
    {
        foreach (var status in new[] { HttpStatusCode.NotFound, HttpStatusCode.Forbidden, HttpStatusCode.TooManyRequests, HttpStatusCode.InternalServerError })
        {
            using var f = new Fixture(_ => new HttpResponseMessage(status));
            await f.Service.CheckAsync();
            Check(!f.Service.CanDownload && !f.Service.Busy && f.Installs == 0, "HTTP failure must not leave a downloadable offer");
        }
    }

    private static async Task Redirects()
    {
        using (var f = new Fixture(_ => Redirect("https://evil.invalid/payload")))
        {
            await f.Service.CheckAsync();
            Check(f.Handler.Requests.Count == 1 && !f.Service.CanDownload, "foreign redirect target must be rejected before its request");
        }
        using (var f = new Fixture(_ => Redirect(Api)))
        {
            await f.Service.CheckAsync();
            Check(f.Handler.Requests.Count <= 6 && !f.Service.CanDownload && !f.Service.Busy, "redirect loops must be bounded");
        }
    }

    private static async Task OffRepositoryRedirect()
    {
        string foreign = "https://api.github.com/repos/fixture-other/Floppy/releases/latest";
        using (var f = new Fixture(uri => uri == Api ? Redirect(foreign) : Reply(Release())))
        {
            await f.Service.CheckAsync();
            Check(!f.Handler.Requests.Contains(foreign) && !f.Service.CanDownload,
                "an allowed hostname must not authorize metadata from a different repository");
        }
        foreach (string redirected in new[] { Download, Checksum })
        {
            string target = redirected.Replace("easycrashx-nex/Floppy", "fixture-other/Floppy");
            using var f = new Fixture(uri => uri == redirected ? Redirect(target) : DefaultReply(uri));
            await f.Service.CheckAsync(); await f.Service.DownloadAndRestartAsync();
            Check(!f.Handler.Requests.Contains(target) && f.Installs == 0 && f.Restarts == 0,
                "asset and checksum redirects must remain bound to the configured repository");
        }
    }

    private static async Task SuccessfulDownload()
    {
        const string cdn = "https://release-assets.githubusercontent.com/fixture/package?signature=fixture";
        using var f = new Fixture(uri => uri == Api ? Reply(Release(digest: "sha256:" + Hash))
            : uri == Checksum ? Reply(Hash + "  " + Name) : uri == Download ? Redirect(cdn) : Bytes(Payload));
        int events = 0;
        f.Service.Changed += (_, _) => events++;
        await f.Service.CheckAsync();
        Check(f.Service.CanDownload && f.Service.CurrentVersion == "1.5.0", "valid newer release becomes downloadable");
        await f.Service.DownloadAndRestartAsync();
        Check(f.Installs == 1 && f.Restarts == 1 && f.InstalledVersion == Version && f.InstalledHash == Hash,
            "only verified payload reaches the injected installer and restart callback once");
        Check(f.InstalledPath != null && File.ReadAllBytes(f.InstalledPath).SequenceEqual(Payload),
            "staged installer payload has the expected complete bytes");
        Check(!f.Service.Busy && f.Service.Progress == 1 && events >= 4, "success leaves coherent progress and status notifications");
    }

    private static async Task InvalidDownloads()
    {
        foreach (string failure in new[] { "digest", "wronghash", "truncated", "oversized", "header", "checksumname", "checksummissing" })
        {
            using var f = new Fixture(uri =>
            {
                if (uri == Api) return Reply(Release(digest: failure == "digest" ? "sha256:" + new string('0', 64) : null));
                if (uri == Checksum) return failure == "checksummissing" ? new(HttpStatusCode.NotFound)
                    : Reply(Hash + "  " + (failure == "checksumname" ? "Other.exe" : Name));
                byte[] bytes = failure == "wronghash" ? Enumerable.Repeat((byte)0, Payload.Length).ToArray()
                    : failure == "truncated" ? Payload[..^1] : failure == "oversized" ? Payload.Concat(new byte[] { 1 }).ToArray() : Payload;
                var response = Bytes(bytes);
                response.Content.Headers.ContentLength = failure == "header" ? Payload.Length + 1 : Payload.Length;
                return response;
            });
            await f.Service.CheckAsync(); await f.Service.DownloadAndRestartAsync();
            Check(f.Installs == 0 && f.Restarts == 0 && !f.Service.Busy,
                failure + ": unverified download must never invoke installer or restart");
            Check(!Directory.Exists(f.Root) || !Directory.EnumerateFiles(f.Root, "*.exe", SearchOption.AllDirectories).Any(),
                failure + ": partial payload must be removed");
        }
    }

    private static async Task OversizedResponses()
    {
        using (var f = new Fixture(_ => Reply(new string(' ', 2 * 1024 * 1024 + 1))))
        {
            await f.Service.CheckAsync();
            Check(!f.Service.CanDownload && !f.Service.Busy, "oversized metadata must fail within its read bound");
        }
        using (var f = new Fixture(uri => uri == Api ? Reply(Release()) : Reply(new string(' ', 4097))))
        {
            await f.Service.CheckAsync(); await f.Service.DownloadAndRestartAsync();
            Check(f.Installs == 0 && !f.Handler.Requests.Contains(Download), "oversized checksum must fail before payload request");
        }
    }

    private static async Task InstallerFailure()
    {
        foreach (Exception error in new Exception[] { new IOException("Fixture IO failure"),
            new TimeoutException("Fixture installer did not acknowledge"), new SecurityException("Fixture identity mismatch") })
        {
            using var f = new Fixture(DefaultReply, (_, _, _) => throw error);
            await f.Service.CheckAsync(); await f.Service.DownloadAndRestartAsync();
            Check(f.Restarts == 0 && !f.Service.Busy && f.Service.Status.Contains("nicht installiert"),
                "failed installer preparation must not close the running application");
            Check(!Directory.EnumerateFiles(f.Root, "*.exe", SearchOption.AllDirectories).Any(), "failed handoff removes its unowned staged payload");
        }
    }

    private static async Task SingleFlight()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var response = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var f = new Fixture(async (_, token) => { started.TrySetResult(); return await response.Task.WaitAsync(token); });
        Task first = f.Service.CheckAsync();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await f.Service.CheckAsync(); await f.Service.DownloadAndRestartAsync();
        Check(f.Handler.Requests.Count == 1 && f.Service.Busy, "overlapping operations must not start another request");
        response.SetResult(Reply(Release())); await first;
        Check(f.Service.CanDownload && !f.Service.Busy, "the original check retains ownership until completion");
    }

    private static async Task CancelCheck()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var f = new Fixture(async (_, token) =>
        { started.TrySetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, token); return Reply(Release()); });
        Task operation = f.Service.CheckAsync();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        f.Service.Dispose();
        await operation.WaitAsync(TimeSpan.FromSeconds(2));
        await f.Service.CheckAsync(); await f.Service.DownloadAndRestartAsync();
        Check(!f.Service.Busy && f.Installs == 0 && f.Restarts == 0 && f.Handler.Requests.Count == 1,
            "Dispose must cancel in-flight work and prevent new operations");
    }

    private static async Task CancelDownload()
    {
        using var blocked = new BlockingReadStream();
        using var f = new Fixture(uri => uri == Download
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(blocked) } : DefaultReply(uri));
        await f.Service.CheckAsync();
        Task operation = f.Service.DownloadAndRestartAsync();
        await blocked.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        int requests = f.Handler.Requests.Count;
        await f.Service.CheckAsync(); await f.Service.DownloadAndRestartAsync();
        Check(f.Handler.Requests.Count == requests && f.Service.Busy, "overlapping check and download calls cannot start a second body download");
        f.Service.Dispose();
        await operation.WaitAsync(TimeSpan.FromSeconds(2));
        Check(!f.Service.Busy && f.Installs == 0 && f.Restarts == 0, "canceled body download must not hand off or restart");
        Check(!Directory.EnumerateFiles(f.Root, "*.exe", SearchOption.AllDirectories).Any(), "canceled body removes the partial download");
    }

    private static HttpResponseMessage DefaultReply(string uri) => uri == Api ? Reply(Release())
        : uri == Checksum ? Reply(Hash + "  " + Name) : uri == Download ? Bytes(Payload)
        : throw new InvalidOperationException("Unexpected fixture request");
    private static HttpResponseMessage Reply(string text) => new(HttpStatusCode.OK) { Content = new StringContent(text) };
    private static HttpResponseMessage Bytes(byte[] bytes) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
    private static HttpResponseMessage Redirect(string target)
    { var response = new HttpResponseMessage(HttpStatusCode.Found); response.Headers.Location = new Uri(target); return response; }

    private sealed class Fixture : IDisposable
    {
        public readonly string Root = Path.Combine(Path.GetTempPath(), "Floppy.Update.Tests-" + Guid.NewGuid().ToString("N"));
        public readonly FakeHandler Handler;
        public readonly UpdateService Service;
        public int Installs, Restarts;
        public string? InstalledPath, InstalledHash, InstalledVersion;
        public Fixture(Func<string, HttpResponseMessage> responder, Func<string, string, string, Task>? installer = null)
            : this((uri, _) => Task.FromResult(responder(uri)), installer) { }
        public Fixture(Func<string, CancellationToken, Task<HttpResponseMessage>> responder,
            Func<string, string, string, Task>? installer = null)
        {
            Handler = new FakeHandler(responder);
            Service = new UpdateService(new HttpClient(Handler), new System.Version(1, 5, 0), Root,
                () => Restarts++, async (path, hash, version) =>
                {
                    Check(Path.GetFullPath(path).StartsWith(Path.GetFullPath(Root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase),
                        "installer fixture can receive only a file beneath its own temporary root");
                    if (installer != null) await installer(path, hash, version);
                    Installs++; InstalledPath = path; InstalledHash = hash; InstalledVersion = version;
                });
        }
        public void Dispose()
        {
            Service.Dispose();
            string resolved = Path.GetFullPath(Root);
            string allowed = Path.Combine(Path.GetFullPath(Path.GetTempPath()), "Floppy.Update.Tests-");
            if (resolved.StartsWith(allowed, StringComparison.OrdinalIgnoreCase) && Directory.Exists(resolved)) Directory.Delete(resolved, true);
        }
    }

    private sealed class FakeHandler(Func<string, CancellationToken, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        public readonly List<string> Requests = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { string uri = request.RequestUri!.AbsoluteUri; Requests.Add(uri); return responder(uri, cancellationToken); }
    }

    private sealed class BlockingReadStream : Stream
    {
        public readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        { Started.TrySetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, token); return 0; }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
