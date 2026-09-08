using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Floppy.App;

public interface IFloppyUpdates
{
    string CurrentVersion { get; }
    string Status { get; }
    double? Progress { get; }
    bool CanDownload { get; }
    bool Busy { get; }
    event EventHandler? Changed;
    Task CheckAsync();
    Task DownloadAndRestartAsync();
    void OpenReleases();
}

internal sealed record UpdateOffer(Version Version, string FileName, Uri Download, Uri Checksum, long Size, string? Digest);

/// <summary>Only stable releases from Floppy's fixed public repository can be installed.</summary>
public sealed class UpdateService : IFloppyUpdates, IDisposable
{
    public const string Repository = "easycrashx-nex/Floppy";
    public const string ReleasesUrl = "https://github.com/" + Repository + "/releases";
    internal const long MaximumDownload = 512L * 1024 * 1024;
    private readonly HttpClient _http;
    private readonly Action _restartRequested;
    private readonly Func<string, string, string, Task> _startInstaller;
    private readonly string _downloadRoot;
    private CancellationTokenSource? _operation;
    private UpdateOffer? _offer;
    private bool _disposed;

    public string CurrentVersion { get; }
    public string Status { get; private set; } = "Noch nicht nach Updates gesucht.";
    public double? Progress { get; private set; }
    public bool CanDownload => !Busy && _offer != null;
    public bool Busy { get; private set; }
    public event EventHandler? Changed;

    public UpdateService(Action restartRequested, Func<Task>? prepareRestart = null, Action? cancelRestart = null)
        : this(new HttpClient(new HttpClientHandler
        { AllowAutoRedirect = false }), typeof(UpdateService).Assembly.GetName().Version ?? new Version(1, 5, 0),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Floppy", "Updates"),
        restartRequested, async (path, hash, version) =>
        {
            try
            {
                if (prepareRestart != null) await prepareRestart();
                await Task.Run(() => UpdateInstaller.Start(path, hash, version));
            }
            catch { cancelRestart?.Invoke(); throw; }
        })
    {
        try
        {
            string result = Path.Combine(_downloadRoot, "last-result.json");
            if (File.Exists(result) && new FileInfo(result).Length < 16 * 1024)
            {
                using var json = JsonDocument.Parse(File.ReadAllText(result));
                var root = json.RootElement;
                if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("Success", out var success) && success.ValueKind == JsonValueKind.False
                    && root.TryGetProperty("Message", out var message) && message.ValueKind == JsonValueKind.String)
                    Status = "Letztes Update nicht installiert: " + message.GetString();
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { }
    }

    internal UpdateService(HttpClient http, Version current, string downloadRoot, Action restartRequested,
        Func<string, string, string, Task> startInstaller)
    {
        _http = http; CurrentVersion = current.ToString(3); _downloadRoot = downloadRoot;
        _restartRequested = restartRequested; _startInstaller = startInstaller;
        _http.Timeout = Timeout.InfiniteTimeSpan;
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Floppy/" + CurrentVersion);
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        _http.DefaultRequestHeaders.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
    }

    public async Task CheckAsync()
    {
        if (Busy || _disposed) return;
        Begin("Suche nach Updates …", TimeSpan.FromSeconds(30));
        _offer = null;
        try
        {
            using var response = await GetAsync(new Uri("https://api.github.com/repos/" + Repository + "/releases/latest"), _operation!.Token);
            if (response.StatusCode == HttpStatusCode.NotFound) { Status = "Noch keine veröffentlichte Version verfügbar."; return; }
            RequireSuccess(response);
            string json = await ReadSmallAsync(response, 2 * 1024 * 1024, _operation.Token);
            _offer = ParseRelease(json, Version.Parse(CurrentVersion));
            Status = _offer == null ? "Floppy ist aktuell · Version " + CurrentVersion
                : "Floppy " + _offer.Version.ToString(3) + " ist verfügbar · " + Math.Ceiling(_offer.Size / 1048576d) + " MB";
        }
        catch (OperationCanceledException) { Status = "Updateprüfung abgebrochen oder Zeitlimit erreicht."; }
        catch (Exception error) when (ExpectedFailure(error)) { Status = "Updateprüfung fehlgeschlagen: " + error.Message; }
        finally { End(); }
    }

    public async Task DownloadAndRestartAsync()
    {
        if (Busy || _disposed || _offer == null) return;
        var offer = _offer;
        Begin("Prüfe Update …", TimeSpan.FromMinutes(15));
        string? download = null;
        bool handedOff = false;
        try
        {
            var token = _operation!.Token;
            using var checksumResponse = await GetAsync(offer.Checksum, token);
            RequireSuccess(checksumResponse);
            string hash = ParseChecksum(await ReadSmallAsync(checksumResponse, 4096, token), offer.FileName);
            if (offer.Digest != null && !offer.Digest.Equals("sha256:" + hash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Die veröffentlichten Prüfsummen widersprechen sich.");
            string folder = Path.Combine(_downloadRoot, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            download = Path.Combine(folder, "Floppy.Update.exe");
            using var response = await GetAsync(offer.Download, token);
            RequireSuccess(response);
            if (response.Content.Headers.ContentLength is long length && length != offer.Size)
                throw new InvalidDataException("Die Dateigröße stimmt nicht mit dem Release überein.");
            await using (var source = await response.Content.ReadAsStreamAsync(token))
            await using (var file = new FileStream(download, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            using (var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                byte[] buffer = new byte[81920];
                long total = 0;
                long lastReport = 0;
                Status = "Floppy " + offer.Version.ToString(3) + " wird heruntergeladen …";
                Notify();
                while (true)
                {
                    int count = await source.ReadAsync(buffer, token);
                    if (count == 0) break;
                    total += count;
                    if (total > offer.Size || total > MaximumDownload) throw new InvalidDataException("Das Update ist größer als angekündigt.");
                    digest.AppendData(buffer, 0, count);
                    await file.WriteAsync(buffer.AsMemory(0, count), token);
                    if (Environment.TickCount64 - lastReport >= 100)
                    { Progress = (double)total / offer.Size; lastReport = Environment.TickCount64; Notify(); }
                }
                if (total != offer.Size || !Convert.ToHexString(digest.GetHashAndReset()).Equals(hash, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Die Update-Prüfsumme stimmt nicht. Die bisherige Version bleibt erhalten.");
                await file.FlushAsync(token);
            }
            token.ThrowIfCancellationRequested();
            Status = "Update geprüft. Neustart wird vorbereitet …"; Progress = 1; Notify();
            await _startInstaller(download, hash, offer.Version.ToString(3));
            handedOff = true;
            Status = "Update bereit. Floppy wird neu gestartet …";
            _restartRequested();
        }
        catch (OperationCanceledException) { Status = "Download abgebrochen oder Zeitlimit erreicht. Die bisherige Version bleibt erhalten."; }
        catch (Exception error) when (ExpectedFailure(error)) { Status = "Update nicht installiert: " + error.Message; }
        finally
        {
            if (!handedOff && download != null)
            { try { File.Delete(download); } catch (Exception error) when (error is IOException or UnauthorizedAccessException) { } }
            End();
        }
    }

    public void OpenReleases() => Process.Start(new ProcessStartInfo(ReleasesUrl) { UseShellExecute = true });

    private void Begin(string status, TimeSpan timeout)
    { Busy = true; Status = status; Progress = null; _operation = new CancellationTokenSource(timeout); Notify(); }
    private void End()
    { _operation?.Dispose(); _operation = null; Busy = false; Notify(); }
    private void Notify() => Changed?.Invoke(this, EventArgs.Empty);
    public void Dispose() { _disposed = true; _operation?.Cancel(); _http.Dispose(); }
    private static bool ExpectedFailure(Exception error) => error is HttpRequestException or IOException or InvalidDataException or UnauthorizedAccessException
        or JsonException or FormatException or InvalidOperationException or KeyNotFoundException or TimeoutException
        or System.Security.SecurityException or System.ComponentModel.Win32Exception;

    internal static UpdateOffer? ParseRelease(string json, Version installed)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.GetProperty("draft").GetBoolean() || root.GetProperty("prerelease").GetBoolean())
            throw new InvalidDataException("Dieses Release ist keine stabile Veröffentlichung.");
        string tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!Regex.IsMatch(tag, @"^v[0-9]{1,5}\.[0-9]{1,5}\.[0-9]{1,5}$") || !Version.TryParse(tag[1..], out var version))
            throw new InvalidDataException("Das Release hat keine unterstützte Versionsnummer.");
        if (version <= installed) return null;
        string name = "Floppy-" + version.ToString(3) + "-Portable.exe";
        string prefix = ReleasesUrl + "/download/" + tag + "/";
        Uri? exe = null, checksum = null;
        long size = 0;
        string? digest = null;
        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            string? assetName = asset.GetProperty("name").GetString();
            if (assetName != name && assetName != name + ".sha256") continue;
            string? url = asset.GetProperty("browser_download_url").GetString();
            if (url != prefix + assetName || asset.GetProperty("state").GetString() != "uploaded")
                throw new InvalidDataException("Ungültiger Downloadlink im Floppy-Release.");
            if (assetName == name)
            {
                if (exe != null) throw new InvalidDataException("Mehrdeutiges Update-Paket.");
                exe = new Uri(url); size = asset.GetProperty("size").GetInt64();
                if (asset.TryGetProperty("digest", out var value) && value.ValueKind == JsonValueKind.String)
                {
                    digest = value.GetString();
                    if (digest != null && !Regex.IsMatch(digest, "^sha256:[a-fA-F0-9]{64}$"))
                        throw new InvalidDataException("Ungültige Release-Prüfsumme.");
                }
            }
            else
            {
                if (checksum != null) throw new InvalidDataException("Mehrdeutige Prüfsummendatei.");
                checksum = new Uri(url);
            }
        }
        if (exe == null || checksum == null || size <= 0 || size > MaximumDownload)
            throw new InvalidDataException("Das Release enthält kein vollständiges Windows-Update mit Prüfsumme.");
        return new UpdateOffer(version, name, exe, checksum, size, digest);
    }

    internal static string ParseChecksum(string text, string fileName)
    {
        var match = Regex.Match(text.Trim().TrimStart('\uFEFF'), @"^([a-fA-F0-9]{64})[ \t]+\*?([^\r\n]+)$");
        if (!match.Success || match.Groups[2].Value != fileName)
            throw new InvalidDataException("Die Prüfsummendatei passt nicht zum Update.");
        return match.Groups[1].Value.ToLowerInvariant();
    }

    private async Task<HttpResponseMessage> GetAsync(Uri uri, CancellationToken token)
    {
        for (int redirect = 0; redirect <= 5; redirect++)
        {
            if (!AllowedDownloadHost(uri)
                || (uri.Host == "api.github.com" && !uri.AbsolutePath.StartsWith("/repos/" + Repository + "/", StringComparison.Ordinal))
                || (uri.Host == "github.com" && !uri.AbsolutePath.StartsWith("/" + Repository + "/releases/download/", StringComparison.Ordinal)))
                throw new InvalidDataException("Nicht erlaubtes Downloadziel.");
            var response = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token);
            int status = (int)response.StatusCode;
            if (status is not (301 or 302 or 303 or 307 or 308)) return response;
            Uri? location = response.Headers.Location;
            response.Dispose();
            if (location == null) throw new InvalidDataException("Das Downloadziel fehlt.");
            uri = location.IsAbsoluteUri ? location : new Uri(uri, location);
        }
        throw new InvalidDataException("Zu viele Downloadweiterleitungen.");
    }

    internal static bool AllowedDownloadHost(Uri uri) => uri.Scheme == Uri.UriSchemeHttps && uri.IsDefaultPort
        && string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Fragment)
        && uri.Host is "github.com" or "api.github.com" or "release-assets.githubusercontent.com" or "objects.githubusercontent.com";

    private static void RequireSuccess(HttpResponseMessage response)
    {
        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
            throw new HttpRequestException("GitHub begrenzt gerade die Anfragen. Bitte später erneut versuchen.");
        response.EnsureSuccessStatusCode();
    }

    private static async Task<string> ReadSmallAsync(HttpResponseMessage response, int limit, CancellationToken token)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(token);
        using var data = new MemoryStream();
        var buffer = new byte[8192];
        int count;
        while ((count = await stream.ReadAsync(buffer, token)) != 0)
        {
            if (data.Length + count > limit) throw new InvalidDataException("Die Update-Antwort ist zu groß.");
            data.Write(buffer, 0, count);
        }
        return Encoding.UTF8.GetString(data.ToArray());
    }
}
