using System;
using System.IO;
using System.Windows;

namespace BoltDownloader.Views
{
    public enum DuplicateChoice
    {
        Rename,
        Skip,
        UpdateExisting
    }

    public partial class DuplicateDownloadDialog : Window
    {
        public string OriginalFileName { get; }
        public string SuggestedNewName { get; }
        public bool CanUpdateExisting { get; }

        public DuplicateChoice Choice { get; private set; } = DuplicateChoice.Rename;
        public string NewFileName { get; private set; } = string.Empty;

        public DuplicateDownloadDialog(string originalFileName, bool canUpdateExisting)
        {
            InitializeComponent();
            OriginalFileName = originalFileName;
            CanUpdateExisting = canUpdateExisting;

            // Sugerir nombre nuevo "(1)"
            var name = Path.GetFileNameWithoutExtension(originalFileName);
            var ext = Path.GetExtension(originalFileName);
            SuggestedNewName = $"{name} (1){ext}";

            txtNewName.Text = SuggestedNewName;
            NewFileName = SuggestedNewName;

            // Mostrar/ocultar opción de actualizar
            rbUpdate.Visibility = canUpdateExisting ? Visibility.Visible : Visibility.Collapsed;
            txtUpdateHint.Visibility = canUpdateExisting ? Visibility.Visible : Visibility.Collapsed;
        }

        private void Option_Checked(object sender, RoutedEventArgs e)
        {
            if (rbRename == null || rbSkip == null || rbUpdate == null || txtNewName == null)
            {
                return;
            }

            if (rbRename.IsChecked == true)
            {
                Choice = DuplicateChoice.Rename;
                txtNewName.IsEnabled = true;
            }
            else if (rbSkip.IsChecked == true)
            {
                Choice = DuplicateChoice.Skip;
                txtNewName.IsEnabled = false;
            }
            else if (rbUpdate.IsChecked == true)
            {
                Choice = DuplicateChoice.UpdateExisting;
                txtNewName.IsEnabled = false;
            }
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            if (Choice == DuplicateChoice.Rename)
            {
                var newName = (txtNewName?.Text ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(newName))
                {
                    BoltDownloader.Services.Localization.Show("Validation_EnterValidName", "Title_Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                NewFileName = newName;
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
