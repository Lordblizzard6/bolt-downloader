using System;
using System.Windows;
using System.Windows.Controls;
using BoltDownloader.Models;
using BoltDownloader.Services;
using Localization = BoltDownloader.Services.Localization;

namespace BoltDownloader.Views
{
    public partial class SchedulerDialog : Window
    {
        private readonly ConfigurationService _configService;

        public SchedulerDialog(ConfigurationService configService)
        {
            InitializeComponent();
            _configService = configService;

            LoadTasks();
            cmbTaskType.SelectedIndex = 0;
            cmbTaskAction.SelectedIndex = 0;
            dpTaskDate.SelectedDate = DateTime.Today;
        }

        private void LoadTasks()
        {
            dgTasks.ItemsSource = null;
            dgTasks.ItemsSource = _configService.ScheduledTasks;
        }

        private void NewTask_Click(object sender, RoutedEventArgs e)
        {
            panelNewTask.Visibility = Visibility.Visible;
        }

        private void cmbTaskType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cmbTaskType.SelectedItem is ComboBoxItem item)
            {
                var type = item.Tag?.ToString();
                
                if (type == "OnStartup")
                {
                    panelDateTime.Visibility = Visibility.Collapsed;
                    lblDateTime.Visibility = Visibility.Collapsed;
                }
                else
                {
                    panelDateTime.Visibility = Visibility.Visible;
                    lblDateTime.Visibility = Visibility.Visible;
                }
            }
        }

        private void SaveTask_Click(object sender, RoutedEventArgs e)
        {
            // Validar campos
            if (string.IsNullOrWhiteSpace(txtTaskName.Text))
            {
                Localization.Show("Validation_Schedule_EnterName", "Title_Validation",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (cmbTaskType.SelectedItem is not ComboBoxItem typeItem)
            {
                Localization.Show("Validation_Schedule_SelectType", "Title_Validation",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var typeTag = typeItem.Tag?.ToString();
            if (!Enum.TryParse<ScheduleType>(typeTag, out var scheduleType))
            {
                return;
            }

            DateTime scheduledTime = DateTime.Now;
            
            if (scheduleType != ScheduleType.OnStartup)
            {
                if (!dpTaskDate.SelectedDate.HasValue)
                {
                    Localization.Show("Validation_Schedule_SelectDate", "Title_Validation",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (!TimeSpan.TryParse(txtTaskTime.Text, out var time))
                {
                    Localization.Show("Validation_Schedule_EnterValidTime", "Title_Validation",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                scheduledTime = dpTaskDate.SelectedDate.Value.Date + time;
            }

            if (cmbTaskAction.SelectedItem is not ComboBoxItem actionItem)
            {
                Localization.Show("Validation_Schedule_SelectAction", "Title_Validation",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var actionTag = actionItem.Tag?.ToString();
            if (!Enum.TryParse<TaskAction>(actionTag, out var taskAction))
            {
                return;
            }

            // Crear tarea
            var task = new ScheduledTask
            {
                Name = txtTaskName.Text,
                Type = scheduleType,
                ScheduledTime = scheduledTime,
                Action = taskAction,
                IsEnabled = true
            };

            _configService.AddScheduledTask(task);
            LoadTasks();

            // Limpiar formulario
            txtTaskName.Text = "";
            cmbTaskType.SelectedIndex = 0;
            cmbTaskAction.SelectedIndex = 0;
            dpTaskDate.SelectedDate = DateTime.Today;
            txtTaskTime.Text = "00:00";
            panelNewTask.Visibility = Visibility.Collapsed;

            Localization.Show("Info_Schedule_Created", "Title_Success",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void CancelNewTask_Click(object sender, RoutedEventArgs e)
        {
            panelNewTask.Visibility = Visibility.Collapsed;
        }

        private void DeleteTask_Click(object sender, RoutedEventArgs e)
        {
            if (dgTasks.SelectedItem is ScheduledTask task)
            {
                var result = MessageBox.Show(
                    Localization.F("Confirm_Schedule_Delete", task.Name),
                    Localization.L("Title_ConfirmDelete"),
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    _configService.RemoveScheduledTask(task.Id);
                    LoadTasks();
                }
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
