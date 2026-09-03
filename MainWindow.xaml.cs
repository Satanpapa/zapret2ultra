using System.Diagnostics;
using System.Security.Principal;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Zapret2Ultra.Services;

namespace Zapret2Ultra;

public partial class MainWindow : Window
{
    private readonly EngineService _engine = new();
    private readonly WireGuardGatewayService _gateway = new();
    private readonly AutoStrategyService _autoStrategy = new();
    private string _strategy = "balanced";
    private string _lastLog = "Готово.";

    public MainWindow()
    {
        InitializeComponent();
        _engine.Log += message => Dispatcher.Invoke(() => { _lastLog = message; DiagLog.Text = message; });
        AdminText.Text = IsAdministrator() ? "Администратор • OK" : "Нужны права администратора";
        VpnEndpoint.Text = GetDefaultEndpoint();
        Loaded += async (_, _) => await RefreshDiagnosticsAsync(51820);
    }

    private void Nav_Click(object sender, RoutedEventArgs e)
    {
        var tag = ((Button)sender).Tag?.ToString();
        HomePage.Visibility = tag == "Home" ? Visibility.Visible : Visibility.Collapsed;
        StrategiesPage.Visibility = tag == "Strategies" ? Visibility.Visible : Visibility.Collapsed;
        GatewayPage.Visibility = tag == "Gateway" ? Visibility.Visible : Visibility.Collapsed;
        DiagnosticsPage.Visibility = tag == "Diagnostics" ? Visibility.Visible : Visibility.Collapsed;
        SettingsPage.Visibility = tag == "Settings" ? Visibility.Visible : Visibility.Collapsed;
        if (tag == "Diagnostics") _ = RefreshDiagnosticsAsync(int.TryParse(VpnPort.Text, out var p) ? p : 51820);
    }

    private async void EngineToggle_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_engine.IsRunning)
            {
                await _engine.StopAsync();
                MainState.Text = "Обход выключен";
                SystemStatus.Text = "Готово";
                EngineToggle.Content = "▶  Включить обход";
            }
            else
            {
                if (!IsAdministrator()) { MessageBox.Show("Приложение должно быть запущено от имени администратора.", "Zapret2Ultra"); return; }
                SystemStatus.Text = "Запуск…";
                await _engine.StartAsync(_strategy);
                MainState.Text = "Обход активен";
                SystemStatus.Text = "Работает";
                EngineToggle.Content = "■  Выключить обход";
            }
            await RefreshDiagnosticsAsync(int.TryParse(VpnPort.Text, out var p) ? p : 51820);
        }
        catch (Exception ex)
        {
            SystemStatus.Text = "Ошибка";
            _lastLog = ex.Message;
            DiagLog.Text = ex.Message;
            MessageBox.Show(ex.Message, "Ошибка запуска", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void AutoPick_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_engine.IsRunning) { MessageBox.Show("Сначала останови текущий профиль.", "Автоподбор"); return; }
            SystemStatus.Text = "Тестирование…";
            var results = await _autoStrategy.ProbeAsync(_engine);
            var best = results.First();
            _strategy = best.Strategy;
            ActiveStrategy.Text = best.Strategy.Equals("aggressive", StringComparison.OrdinalIgnoreCase) ? "Aggressive" : "Balanced";
            ActiveDetail.Text = $"Автоподбор • {best.Details} • {best.AverageLatencyMs:0} ms";
            _lastLog = string.Join(" | ", results.Select(x => $"{x.Strategy}: {x.Details}, {x.AverageLatencyMs:0} ms"));
            DiagLog.Text = _lastLog;
            SystemStatus.Text = "Готово";
            MessageBox.Show($"Выбран профиль {ActiveStrategy.Text}.\n{_lastLog}", "Автоподбор", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) { SystemStatus.Text = "Ошибка"; MessageBox.Show(ex.Message, "Автоподбор", MessageBoxButton.OK, MessageBoxImage.Error); }
        finally { await RefreshDiagnosticsAsync(int.TryParse(VpnPort.Text, out var p) ? p : 51820); }
    }

    private void UseBalanced_Click(object sender, RoutedEventArgs e) { _strategy = "balanced"; ActiveStrategy.Text = "Balanced"; ActiveDetail.Text = "HTTP + TLS + QUIC"; }
    private void UseAggressive_Click(object sender, RoutedEventArgs e) { _strategy = "aggressive"; ActiveStrategy.Text = "Aggressive"; ActiveDetail.Text = "Сильный TLS/QUIC профиль"; }

    private async void CreateVpn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!int.TryParse(VpnPort.Text, out var port) || port is < 1 or > 65535) { MessageBox.Show("UDP-порт должен быть от 1 до 65535."); return; }
            if (string.IsNullOrWhiteSpace(VpnEndpoint.Text)) { MessageBox.Show("Укажи LAN IP для Wi-Fi или публичный DNS/IP для подключения извне.", "WireGuard"); return; }
            GatewayState.Text = "Создание…";
            var info = await _gateway.SetupAsync(port, VpnEndpoint.Text.Trim());
            GatewayState.Text = info.Running ? "Запущен" : "Не запущен";
            GatewayCard.Text = info.Running ? "Активен" : "Выключен";
            QrImage.Source = info.ClientQr;
            VpnInfo.Text = $"{info.Message}\nLAN: {info.LocalAddress ?? "не найден"}\nEndpoint: {VpnEndpoint.Text.Trim()}:{port}";
            await RefreshDiagnosticsAsync(port);
        }
        catch (Exception ex) { GatewayState.Text = "Ошибка"; VpnInfo.Text = ex.Message; MessageBox.Show(ex.Message, "WireGuard", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private async void StopVpn_Click(object sender, RoutedEventArgs e)
    {
        try { await _gateway.StopAsync(); GatewayState.Text = "Не запущен"; GatewayCard.Text = "Выключен"; QrImage.Source = null; VpnInfo.Text = "WireGuard gateway остановлен, NAT и наше firewall-правило удалены."; await RefreshDiagnosticsAsync(int.TryParse(VpnPort.Text, out var p) ? p : 51820); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "WireGuard", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void ExportVpn_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_gateway.LastClientConfig)) { MessageBox.Show("Сначала создай конфигурацию WireGuard."); return; }
        var dlg = new SaveFileDialog { Filter = "WireGuard config (*.conf)|*.conf", FileName = "phone.conf" };
        if (dlg.ShowDialog() == true) File.WriteAllText(dlg.FileName, _gateway.LastClientConfig);
    }

    private void InstallWireGuard_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo { FileName = "https://www.wireguard.com/install/", UseShellExecute = true });
    }

    private async void RefreshDiagnostics_Click(object sender, RoutedEventArgs e) => await RefreshDiagnosticsAsync(int.TryParse(VpnPort.Text, out var p) ? p : 51820);

    private async Task RefreshDiagnosticsAsync(int port)
    {
        var wg = await _gateway.InspectAsync(port);
        DiagEngine.Text = $"Engine: {(_engine.IsRunning ? "RUNNING" : File.Exists(_engine.EnginePath) ? "READY" : "NOT INSTALLED")}";
        DiagAdmin.Text = $"Admin: {(IsAdministrator() ? "YES" : "NO")}";
        DiagWireGuard.Text = $"WireGuard: {(wg.Installed ? (wg.Running ? "RUNNING" : "INSTALLED") : "NOT FOUND")}";
        DiagLan.Text = $"LAN: {wg.LocalAddress ?? "—"}";
        DiagLog.Text = _lastLog;
        EngineCard.Text = _engine.IsRunning ? "winws2 активен" : File.Exists(_engine.EnginePath) ? "Готов" : "Не установлен";
    }

    private static bool IsAdministrator() => new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

    private static string GetDefaultEndpoint()
    {
        var host = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up && n.NetworkInterfaceType != System.Net.NetworkInformation.NetworkInterfaceType.Loopback)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses.Select(u => u.Address))
            .FirstOrDefault(ip => ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !ip.ToString().StartsWith("169.254."));
        return host?.ToString() ?? "";
    }
}
