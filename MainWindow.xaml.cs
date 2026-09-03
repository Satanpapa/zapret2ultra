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
    private string _strategy = "balanced";
    private string _lastLog = "Готово.";

    public MainWindow()
    {
        InitializeComponent();
        _engine.Log += message => Dispatcher.Invoke(() => { _lastLog = message; DiagLog.Text = message; });
        AdminText.Text = IsAdministrator() ? "Администратор • OK" : "Нужны права администратора";
        VpnEndpoint.Text = _gateway is not null ? GetDefaultEndpoint() : "";
        RefreshDiagnostics_Click(this, new RoutedEventArgs());
    }

    private void Nav_Click(object sender, RoutedEventArgs e)
    {
        var tag = ((Button)sender).Tag?.ToString();
        HomePage.Visibility = tag == "Home" ? Visibility.Visible : Visibility.Collapsed;
        StrategiesPage.Visibility = tag == "Strategies" ? Visibility.Visible : Visibility.Collapsed;
        GatewayPage.Visibility = tag == "Gateway" ? Visibility.Visible : Visibility.Collapsed;
        DiagnosticsPage.Visibility = tag == "Diagnostics" ? Visibility.Visible : Visibility.Collapsed;
        SettingsPage.Visibility = tag == "Settings" ? Visibility.Visible : Visibility.Collapsed;
        if (tag == "Diagnostics") RefreshDiagnostics_Click(sender, e);
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
                GatewayCard.Text = "Готов";
            }
            else
            {
                if (!IsAdministrator()) { MessageBox.Show("Запусти приложение от имени администратора.", "Zapret2Ultra"); return; }
                SystemStatus.Text = "Запуск…";
                await _engine.StartAsync(_strategy);
                MainState.Text = "Обход активен";
                SystemStatus.Text = "Работает";
                EngineToggle.Content = "■  Выключить обход";
                EngineCard.Text = "winws2 активен";
            }
            RefreshDiagnostics_Click(sender, e);
        }
        catch (Exception ex)
        {
            SystemStatus.Text = "Ошибка";
            MessageBox.Show(ex.Message, "Ошибка запуска", MessageBoxButton.OK, MessageBoxImage.Error);
            _lastLog = ex.Message;
            DiagLog.Text = ex.Message;
        }
    }

    private async void AutoPick_Click(object sender, RoutedEventArgs e)
    {
        // Deterministic first pass: validate engine + use Balanced; a future probe can promote Aggressive.
        try
        {
            await _engine.EnsureInstalledAsync();
            _strategy = "balanced";
            ActiveStrategy.Text = "Balanced";
            ActiveDetail.Text = "HTTP + TLS + QUIC";
            _lastLog = "Автоподбор: выбран Balanced как безопасная стартовая стратегия.";
            DiagLog.Text = _lastLog;
            MessageBox.Show("Стратегия Balanced выбрана. Запусти обход и проверь нужные сайты. Затем можно переключиться на Aggressive.", "Автоподбор", MessageBoxButton.OK, MessageBoxImage.Information);
            EngineCard.Text = "Готов к запуску";
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Автоподбор", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void UseBalanced_Click(object sender, RoutedEventArgs e) { _strategy = "balanced"; ActiveStrategy.Text = "Balanced"; ActiveDetail.Text = "HTTP + TLS + QUIC"; }
    private void UseAggressive_Click(object sender, RoutedEventArgs e) { _strategy = "aggressive"; ActiveStrategy.Text = "Aggressive"; ActiveDetail.Text = "Более сильный TLS/QUIC профиль"; }

    private async void CreateVpn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!int.TryParse(VpnPort.Text, out var port) || port is < 1 or > 65535) { MessageBox.Show("UDP-порт должен быть от 1 до 65535."); return; }
            var endpoint = string.IsNullOrWhiteSpace(VpnEndpoint.Text) ? GetDefaultEndpoint() : VpnEndpoint.Text.Trim();
            GatewayState.Text = "Создание…";
            var info = await _gateway.SetupAsync(port, endpoint);
            GatewayState.Text = info.Running ? "Запущен" : "Не запущен";
            GatewayCard.Text = info.Running ? "Активен" : "Выключен";
            QrImage.Source = info.ClientQr;
            VpnInfo.Text = $"{info.Message}\nLAN: {info.LocalAddress ?? "не найден"}\nEndpoint: {endpoint}:{port}";
            await RefreshDiagnosticsAsync(port);
        }
        catch (Exception ex) { GatewayState.Text = "Ошибка"; VpnInfo.Text = ex.Message; MessageBox.Show(ex.Message, "WireGuard", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private async void StopVpn_Click(object sender, RoutedEventArgs e)
    {
        try { await _gateway.StopAsync(); GatewayState.Text = "Не запущен"; GatewayCard.Text = "Выключен"; QrImage.Source = null; VpnInfo.Text = "WireGuard gateway остановлен и NAT/Firewall правило удалено."; }
        catch (Exception ex) { MessageBox.Show(ex.Message, "WireGuard", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void ExportVpn_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_gateway.LastClientConfig)) { MessageBox.Show("Сначала создай конфигурацию WireGuard."); return; }
        var dlg = new SaveFileDialog { Filter = "WireGuard config (*.conf)|*.conf", FileName = "phone.conf" };
        if (dlg.ShowDialog() == true) File.WriteAllText(dlg.FileName, _gateway.LastClientConfig);
    }

    private async void RefreshDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        try { await RefreshDiagnosticsAsync(int.TryParse(VpnPort.Text, out var p) ? p : 51820); } catch (Exception ex) { DiagLog.Text = ex.Message; }
    }

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
        var host = System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName()).AddressList.FirstOrDefault(x => x.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !x.ToString().StartsWith("169.254."));
        return host?.ToString() ?? "192.168.1.100";
    }
}
