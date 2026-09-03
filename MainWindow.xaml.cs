using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

namespace Zapret2Ultra;

public partial class MainWindow : Window
{
    private bool _running;

    public MainWindow() => InitializeComponent();

    private void Home_Click(object sender, RoutedEventArgs e) { HomePage.Visibility = Visibility.Visible; VpnPage.Visibility = Visibility.Collapsed; }
    private void Vpn_Click(object sender, RoutedEventArgs e) { HomePage.Visibility = Visibility.Collapsed; VpnPage.Visibility = Visibility.Visible; }
    private void Strategies_Click(object sender, RoutedEventArgs e) => MessageBox.Show("Strategy manager is ready for the engine adapter.", "Strategies");
    private void Diagnostics_Click(object sender, RoutedEventArgs e) => MessageBox.Show("Diagnostics module: engine, WFP, DNS, routing and connectivity checks will be shown here.", "Diagnostics");
    private void Settings_Click(object sender, RoutedEventArgs e) => MessageBox.Show("Settings: startup, tray, language, engine path and network gateway.", "Settings");

    private void Toggle_Click(object sender, RoutedEventArgs e)
    {
        _running = !_running;
        SystemStatus.Text = _running ? "Обход активен" : "Готово";
        ((Button)sender).Content = _running ? "■  Выключить обход" : "▶  Включить обход";
    }

    private void CreateVpn_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(VpnPort.Text, out var port) || port is < 1 or > 65535)
        {
            MessageBox.Show("Укажите корректный UDP-порт 1–65535.", "VPN-шлюз");
            return;
        }

        VpnText.Text = "Конфигурация готова";
        VpnInfo.Text = $"WireGuard gateway подготовлен на UDP {port}. Следующий шаг — генерация ключей, адресного пула, QR-кода и правил NAT/WFP с проверкой прав администратора.";
    }
}
