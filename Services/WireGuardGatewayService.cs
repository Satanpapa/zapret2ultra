using System.Net;
using System.Net.NetworkInformation;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using QRCoder;
using System.Windows.Media.Imaging;

namespace Zapret2Ultra.Services;

public sealed record WireGuardGatewayInfo(bool Installed, bool Running, string? LocalAddress, int Port, string? ClientConfig, BitmapImage? ClientQr, string Message);

public sealed class WireGuardGatewayService
{
    private const string TunnelName = "Zapret2UltraGateway";
    private const string NatName = "Zapret2UltraNat";
    private const string FirewallName = "Zapret2Ultra-WireGuard";
    private const string GatewayCidr = "10.66.0.0/24";
    private const string GatewayIp = "10.66.0.1/24";
    private const string ClientIp = "10.66.0.2/32";
    private readonly string _root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Zapret2Ultra", "wireguard");
    private string ConfigPath => Path.Combine(_root, $"{TunnelName}.conf");
    private string ClientPath => Path.Combine(_root, "phone.conf");
    public string? LastClientConfig { get; private set; }
    public BitmapImage? LastQr { get; private set; }

    public bool IsAdministrator => new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
    public string? WireGuardExe => FindWireGuardExe();

    public async Task<WireGuardGatewayInfo> SetupAsync(int port, string endpoint)
    {
        if (!IsAdministrator) return new(false, false, GetLanAddress(), port, null, null, "Для создания WireGuard-шлюза нужны права администратора.");
        var wg = FindWireGuardExe();
        if (wg is null) return new(false, false, GetLanAddress(), port, null, null, "WireGuard for Windows не найден. Установите официальный WireGuard, затем повторите.");
        Directory.CreateDirectory(_root);

        var serverPrivate = await GetOrCreateKeyAsync(wg, "server.key");
        var serverPublic = await GetPublicKeyAsync(wg, serverPrivate);
        var clientPrivate = await GetOrCreateKeyAsync(wg, "phone.key");
        var clientPublic = await GetPublicKeyAsync(wg, clientPrivate);

        var server = $"[Interface]\nPrivateKey = {serverPrivate}\nAddress = {GatewayIp}\nListenPort = {port}\n\n[Peer]\nPublicKey = {clientPublic}\nAllowedIPs = {ClientIp}\n";
        var client = $"[Interface]\nPrivateKey = {clientPrivate}\nAddress = {ClientIp}\nDNS = 1.1.1.1\n\n[Peer]\nPublicKey = {serverPublic}\nAllowedIPs = 0.0.0.0/0\nEndpoint = {endpoint}:{port}\nPersistentKeepalive = 25\n";
        await File.WriteAllTextAsync(ConfigPath, server, Encoding.UTF8);
        await File.WriteAllTextAsync(ClientPath, client, Encoding.UTF8);
        RestrictFile(ConfigPath); RestrictFile(ClientPath); RestrictFile(Path.Combine(_root, "server.key")); RestrictFile(Path.Combine(_root, "phone.key"));

        await RunElevatedCommandAsync(wg, "/uninstalltunnelservice", TunnelName, ignoreFailure: true);
        var install = await ProcessRunner.RunAsync(wg, new[] { "/installtunnelservice", ConfigPath });
        if (install.ExitCode != 0) throw new InvalidOperationException(install.StdErr.Trim());
        await ProcessRunner.RunAsync("sc.exe", new[] { "start", $"WireGuardTunnel${TunnelName}" });
        await ConfigureNatAsync(port);

        LastClientConfig = client;
        LastQr = CreateQr(client);
        return new(true, true, GetLanAddress(), port, client, LastQr, "Шлюз запущен. Для телефона отсканируйте QR или импортируйте phone.conf.");
    }

    public async Task StopAsync()
    {
        if (!IsAdministrator) throw new InvalidOperationException("Для остановки шлюза нужны права администратора.");
        var wg = FindWireGuardExe();
        if (wg is not null) await RunElevatedCommandAsync(wg, "/uninstalltunnelservice", TunnelName, true);
        await ProcessRunner.RunAsync("powershell.exe", new[] { "-NoProfile", "-Command", $"Remove-NetNat -Name '{NatName}' -ErrorAction SilentlyContinue; Remove-NetFirewallRule -DisplayName '{FirewallName}' -ErrorAction SilentlyContinue" });
    }

    public async Task<WireGuardGatewayInfo> InspectAsync(int port)
    {
        var wg = FindWireGuardExe();
        var running = false;
        if (wg is not null)
        {
            var result = await ProcessRunner.RunAsync("sc.exe", new[] { "query", $"WireGuardTunnel${TunnelName}" });
            running = result.StdOut.Contains("RUNNING", StringComparison.OrdinalIgnoreCase);
        }
        return new(wg is not null, running, GetLanAddress(), port, LastClientConfig, LastQr, running ? "WireGuard gateway активен." : "WireGuard gateway выключен.");
    }

    private async Task ConfigureNatAsync(int port)
    {
        var script = $"$i=Get-NetIPInterface -InterfaceAlias '{TunnelName}' -ErrorAction Stop; Set-NetIPInterface -InterfaceAlias '{TunnelName}' -Forwarding Enabled; Get-NetIPInterface -AddressFamily IPv4 | Where-Object {{$_.ConnectionState -eq 'Connected' -and $_.InterfaceAlias -ne '{TunnelName}'}} | ForEach-Object {{Set-NetIPInterface -InterfaceIndex $_.ifIndex -Forwarding Enabled -ErrorAction SilentlyContinue}}; if (-not (Get-NetNat -Name '{NatName}' -ErrorAction SilentlyContinue)) {{New-NetNat -Name '{NatName}' -InternalIPInterfaceAddressPrefix '{GatewayCidr}'}}; if (-not (Get-NetFirewallRule -DisplayName '{FirewallName}' -ErrorAction SilentlyContinue)) {{New-NetFirewallRule -DisplayName '{FirewallName}' -Direction Inbound -Protocol UDP -LocalPort {port} -Action Allow -Profile Any}}";
        var result = await ProcessRunner.RunAsync("powershell.exe", new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", script });
        if (result.ExitCode != 0) throw new InvalidOperationException($"Не удалось настроить NAT/Firewall: {result.StdErr.Trim()}");
    }

    private async Task<string> GetOrCreateKeyAsync(string wg, string name)
    {
        var path = Path.Combine(_root, name);
        if (File.Exists(path)) return (await File.ReadAllTextAsync(path)).Trim();
        var result = await ProcessRunner.RunAsync(wg, new[] { "genkey" });
        if (result.ExitCode != 0) throw new InvalidOperationException(result.StdErr.Trim());
        var key = result.StdOut.Trim();
        await File.WriteAllTextAsync(path, key + "\n");
        return key;
    }

    private static async Task<string> GetPublicKeyAsync(string wg, string privateKey) 
    {
        var result = await ProcessRunner.RunAsync(wg, new[] { "pubkey" }, stdin: privateKey + "\n");
        if (result.ExitCode != 0) throw new InvalidOperationException(result.StdErr.Trim());
        return result.StdOut.Trim();
    }

    private static BitmapImage CreateQr(string text)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.Q);
        var png = new PngByteQRCode(data).GetGraphic(12);
        using var stream = new MemoryStream(png);
        var bitmap = new BitmapImage();
        bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze();
        return bitmap;
    }

    private static string? FindWireGuardExe()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WireGuard", "wireguard.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WireGuard", "wg.exe"),
        };
        foreach (var path in candidates) if (File.Exists(path)) return path;
        return null;
    }

    private static async Task RunElevatedCommandAsync(string exe, string arg1, string arg2, bool ignoreFailure)
    {
        var result = await ProcessRunner.RunAsync(exe, new[] { arg1, arg2 });
        if (!ignoreFailure && result.ExitCode != 0) throw new InvalidOperationException(result.StdErr.Trim());
    }

    private static string? GetLanAddress()
    {
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up))
        foreach (var uni in nic.GetIPProperties().UnicastAddresses)
            if (uni.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !IPAddress.IsLoopback(uni.Address) && !uni.Address.ToString().StartsWith("169.254.")) return uni.Address.ToString();
        return null;
    }

    private static void RestrictFile(string path)
    {
        try
        {
            var info = new FileInfo(path);
            info.Attributes |= FileAttributes.Hidden;
        }
        catch { }
    }
}
