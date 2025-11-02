using System;
using System.Windows;
using Localization = BoltDownloader.Services.Localization;
using BoltDownloader.Services;

namespace BoltDownloader.Views
{
    public partial class SpeedLimitDialog : Window
    {
        private readonly ConfigurationService _configService;

        public SpeedLimitDialog(ConfigurationService configService)
        {
            InitializeComponent();
            _configService = configService;

            LoadCurrentSettings();
        }

        private void LoadCurrentSettings()
        {
            if (_configService.SpeedLimitKBps == 0)
            {
                rbNoLimit.IsChecked = true;
            }
            else
            {
                rbCustomLimit.IsChecked = true;
                
                // Convertir a la unidad más apropiada
                if (_configService.SpeedLimitKBps >= 1024)
                {
                    txtSpeedLimit.Text = (_configService.SpeedLimitKBps / 1024.0).ToString("F2");
                    cmbSpeedUnit.SelectedIndex = 1; // MB/s
                }
                else
                {
                    txtSpeedLimit.Text = _configService.SpeedLimitKBps.ToString();
                    cmbSpeedUnit.SelectedIndex = 0; // KB/s
                }
            }
        }

        private void SpeedOption_Changed(object sender, RoutedEventArgs e)
        {
            panelCustomSpeed.IsEnabled = rbCustomLimit.IsChecked ?? false;
        }

        private void Apply_Click(object sender, RoutedEventArgs e)
        {
            if (rbNoLimit.IsChecked == true)
            {
                _configService.SpeedLimitKBps = 0;
            }
            else
            {
                if (!double.TryParse(txtSpeedLimit.Text, out double speedValue) || speedValue <= 0)
                {
                    Localization.Show("Validation_EnterValidSpeed", "Title_Validation",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Convertir a KB/s
                long speedKBps;
                if (cmbSpeedUnit.SelectedIndex == 1) // MB/s
                {
                    speedKBps = (long)(speedValue * 1024);
                }
                else // KB/s
                {
                    speedKBps = (long)speedValue;
                }

                _configService.SpeedLimitKBps = speedKBps;
            }

            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
