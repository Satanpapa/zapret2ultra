using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;

namespace Zapret2Ultra.Services;

public sealed class EngineService
{
    private const string Version = "1.0.5";
    private const string ZipUrl = "https://github.com/bol-van/zapret2/releases/download/v1.0.5/zapret2-v1.0.5.zip";
    private const string ZipSha256 = "d73a4c57dad0f20f473aa62ed950505f0737154c3d9ab8fca717e75f1a21fa69";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(5) };
    private readonly string _root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Zapret2Ultra");
    private Process? _process;

    public bool IsRunning => _process is { HasExited: false };
    public string EngineDirectory => Path.Combine(_root, "engine", $"v{Version}");
    public string EnginePath => Path.Combine(EngineDirectory, "winws2.exe");
    public event Action<string>? Log;

    public async Task EnsureInstalledAsync(CancellationToken cancellationToken = default)
    {
        if (File.Exists(EnginePath)) return;
        Directory.CreateDirectory(EngineDirectory);
        var archive = Path.Combine(_root, $"zapret2-v{Version}.zip");
        Log?.Invoke($"Загрузка winws2 v{Version}…");
        await using (var source = await Http.GetStreamAsync(ZipUrl, cancellationToken))
        await using (var target = File.Create(archive))
            await source.CopyToAsync(target, cancellationToken);
        await using (var file = File.OpenRead(archive))
        {
            var actual = await SHA256.HashDataAsync(file, cancellationToken);
            var expected = Convert.FromHexString(ZipSha256);
            if (!CryptographicOperations.FixedTimeEquals(actual, expected))
            {
                File.Delete(archive);
                throw new InvalidOperationException("SHA-256 движка не совпадает с опубликованным значением.");
            }
        }
        var temp = Path.Combine(_root, $"extract-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temp);
        try
        {
            ZipFile.ExtractToDirectory(archive, temp);
            var exe = FindFile(temp, "winws2.exe") ?? throw new FileNotFoundException("winws2.exe не найден в официальном архиве.");
            var exeDir = Path.GetDirectoryName(exe)!;
            foreach (var sourceFile in Directory.EnumerateFiles(exeDir, "*", SearchOption.TopDirectoryOnly))
                File.Copy(sourceFile, Path.Combine(EngineDirectory, Path.GetFileName(sourceFile)), true);
            foreach (var dir in new[] { "lua", "files", "windivert.filter" })
            {
                var found = Directory.EnumerateDirectories(temp, dir, SearchOption.AllDirectories).FirstOrDefault();
                if (found is not null) CopyDirectory(found, Path.Combine(EngineDirectory, dir));
            }
        }
        finally
        {
            if (Directory.Exists(temp)) Directory.Delete(temp, true);
            if (File.Exists(archive)) File.Delete(archive);
        }
        Log?.Invoke("Движок установлен и проверен.");
    }

    public async Task StartAsync(string strategy = "balanced", CancellationToken cancellationToken = default)
    {
        if (IsRunning) return;
        await EnsureInstalledAsync(cancellationToken);
        var args = BuildArguments(strategy, EngineDirectory);
        var psi = new ProcessStartInfo(EnginePath)
        {
            WorkingDirectory = EngineDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var arg in args) psi.ArgumentList.Add(arg);
        _process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        _process.OutputDataReceived += (_, e) => { if (e.Data is not null) Log?.Invoke(e.Data); };
        _process.ErrorDataReceived += (_, e) => { if (e.Data is not null) Log?.Invoke(e.Data); };
        if (!_process.Start()) throw new InvalidOperationException("Не удалось запустить winws2.");
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();
        await Task.Delay(500, cancellationToken);
        if (_process.HasExited)
        {
            var code = _process.ExitCode;
            _process.Dispose();
            _process = null;
            throw new InvalidOperationException($"winws2 завершился сразу после запуска, код {code}. Проверьте права администратора и журнал.");
        }
        Log?.Invoke($"winws2 запущен со стратегией {strategy}.");
    }

    public async Task StopAsync()
    {
        if (_process is null) return;
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                await _process.WaitForExitAsync();
            }
        }
        finally
        {
            _process.Dispose();
            _process = null;
            Log?.Invoke("winws2 остановлен.");
        }
    }

    private static IReadOnlyList<string> BuildArguments(string strategy, string root)
    {
        var lua = Path.Combine(root, "lua");
        var lib = Path.Combine(lua, "zapret-lib.lua");
        var antidpi = Path.Combine(lua, "zapret-antidpi.lua");
        var quic = FindFile(root, "quic_initial_www_google_com.bin");
        var filterDir = Path.Combine(root, "windivert.filter");
        var args = new List<string>
        {
            "--wf-tcp-out=80,443",
            "--wf-udp-out=443",
            $"--lua-init=@{lib}",
            $"--lua-init=@{antidpi}",
            "--lua-init=fake_default_tls = tls_mod(fake_default_tls,'rnd,rndsni')",
            "--filter-tcp=80",
            "--filter-l7=http",
            "--out-range=-d10",
            "--payload=http_req",
            "--lua-desync=fake:blob=fake_default_http:ip_autottl=-2,3-20:ip6_autottl=-2,3-20:tcp_md5",
            "--lua-desync=fakedsplit:ip_autottl=-2,3-20:ip6_autottl=-2,3-20:tcp_md5",
            "--new",
            "--filter-tcp=443",
            "--filter-l7=tls",
            "--out-range=-d10",
            "--payload=tls_client_hello",
        };
        if (strategy.Equals("aggressive", StringComparison.OrdinalIgnoreCase))
        {
            args.Add("--lua-desync=fake:blob=fake_default_tls:tcp_md5:repeats=11:tls_mod=rnd,dupsid,sni=www.google.com");
            args.Add("--lua-desync=multidisorder:pos=1,midsld");
        }
        else
        {
            args.Add("--lua-desync=fake:blob=fake_default_tls:tcp_md5:tcp_seq=-10000:repeats=6");
            args.Add("--lua-desync=multidisorder:pos=midsld");
        }
        args.Add("--new");
        if (File.Exists(quic))
        {
            args.Add($"--blob=quic_google:@{quic}");
            args.Add("--filter-udp=443");
            args.Add("--filter-l7=quic");
            args.Add("--payload=quic_initial");
            args.Add("--lua-desync=fake:blob=quic_google:repeats=11");
            args.Add("--new");
        }
        if (Directory.Exists(filterDir))
        {
            foreach (var name in new[] { "discord_media", "stun", "wireguard", "quic_initial_ietf" })
            {
                var file = Directory.EnumerateFiles(filterDir, $"*{name}*.txt", SearchOption.TopDirectoryOnly).FirstOrDefault();
                if (file is not null) args.Add($"--wf-raw-part=@{file}");
            }
        }
        if (strategy.Equals("aggressive", StringComparison.OrdinalIgnoreCase))
        {
            args.Add("--filter-l7=wireguard,stun,discord");
            args.Add("--payload=wireguard_initiation,wireguard_cookie,stun,discord_ip_discovery");
            args.Add("--lua-desync=fake:blob=0x00000000000000000000000000000000:repeats=2");
        }
        return args;
    }

    private static string? FindFile(string root, string name) => Directory.Exists(root)
        ? Directory.EnumerateFiles(root, name, SearchOption.AllDirectories).FirstOrDefault()
        : null;

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.EnumerateFiles(source)) File.Copy(file, Path.Combine(target, Path.GetFileName(file)), true);
        foreach (var dir in Directory.EnumerateDirectories(source)) CopyDirectory(dir, Path.Combine(target, Path.GetFileName(dir)));
    }
}
