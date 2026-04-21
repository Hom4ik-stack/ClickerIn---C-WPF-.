using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ClickerIn.Models;
using ClickerIn.Services;

namespace ClickerIn
{
    public partial class ScheduleEditWindow : Window
    {
        private readonly ObservableCollection<Scenario> _scenarios;
        private readonly IWindowManager _win;
        private readonly ScheduleEntry _entry;

        public bool Saved { get; private set; }
        public ScheduleEntry Entry => _entry;

        public ScheduleEditWindow(Scenario currentScenario, IWindowManager win,
            ScheduleEntry existing = null)
            : this(null, currentScenario, win, existing)
        { }

        public ScheduleEditWindow(ObservableCollection<Scenario> allScenarios,
     Scenario currentScenario, IWindowManager win, ScheduleEntry existing = null)
        {
            InitializeComponent();
            _win = win;
            _scenarios = allScenarios ?? new ObservableCollection<Scenario>();

            if (existing != null)
            {
                _entry = existing;
                Title = "Редактирование расписания";
            }
            else
            {
                _entry = new ScheduleEntry
                {
                    ScenarioId = currentScenario?.Id,
                    ScenarioName = currentScenario?.Name,
                    TargetProcessName = currentScenario?.TargetProcessName
                };
                Title = "Новое расписание";
            }

            if (_scenarios.Count == 0 && currentScenario != null)
                _scenarios.Add(currentScenario);

            CmbScenario.ItemsSource = _scenarios;
            CmbChainAdd.ItemsSource = _scenarios;

            var selScenario = _scenarios.FirstOrDefault(s => s.Id == _entry.ScenarioId);
            if (selScenario != null) CmbScenario.SelectedItem = selScenario;
            else if (_scenarios.Count > 0) CmbScenario.SelectedIndex = 0;

            if (_scenarios.Count > 0) CmbChainAdd.SelectedIndex = 0;

            DpDate.SelectedDate = DateTime.Today;
            TxtProcess.Text = _entry.TargetProcessName ?? currentScenario?.TargetProcessName ?? "";

            LstChain.ItemsSource = _entry.ChainItems;
            WeekDaysList.ItemsSource = _entry.WeeklyEntries;

            if (existing != null) LoadFromEntry(existing);

            Loaded += (s, e) =>
            {
                WhatMode_Changed(null, null);
                Mode_Changed(null, null);
                UpdatePreview();
            };
        }

        private void LoadFromEntry(ScheduleEntry e)
        {
            if (e.IsChain)
            {
                RbChain.IsChecked = true;
                foreach (var item in e.ChainItems)
                {
                    _entry.ChainItems.Add(new ChainItem
                    {
                        ScenarioId = item.ScenarioId,
                        ScenarioName = item.ScenarioName,
                        DelayAfterMs = item.DelayAfterMs,
                        Order = item.Order
                    });
                }
                RefreshChain();
            }
            else
            {
                RbSingle.IsChecked = true;
            }

            ChkStopOnError.IsChecked = e.StopChainOnError;

            switch (e.Mode)
            {
                case ScheduleMode.Once:
                    RbOnce.IsChecked = true;
                    if (e.HasOnceDateTime)
                    {
                        DpDate.SelectedDate = e.OnceDateTime.Date;
                        TxtOnceHour.Text = e.OnceDateTime.Hour.ToString("D2");
                        TxtOnceMinute.Text = e.OnceDateTime.Minute.ToString("D2");
                    }
                    break;

                case ScheduleMode.Daily:
                    RbDaily.IsChecked = true;
                    TxtDailyHour.Text = e.DailyTime.Hours.ToString("D2");
                    TxtDailyMinute.Text = e.DailyTime.Minutes.ToString("D2");
                    break;

                case ScheduleMode.WeeklyCustom:
                    RbWeekly.IsChecked = true;
                    break;

                case ScheduleMode.Interval:
                    RbInterval.IsChecked = true;
                    TxtIntervalMinutes.Text = e.IntervalMinutes.ToString();
                    ChkTodayOnly.IsChecked = e.IntervalTodayOnly;

                    if (e.HasIntervalStartTime)
                    {
                        TxtStartHour.Text = e.IntervalStartTime.Hour.ToString("D2");
                        TxtStartMinute.Text = e.IntervalStartTime.Minute.ToString("D2");
                    }

                    if (e.HasIntervalEndTime)
                    {
                        TxtEndHour.Text = e.IntervalEndTime.Hour.ToString("D2");
                        TxtEndMinute.Text = e.IntervalEndTime.Minute.ToString("D2");
                    }
                    break;
            }

            ChkPreAlert.IsChecked = e.PreAlertEnabled;
            TxtPreAlertMin.Text = e.PreAlertMinutes.ToString();
            TxtProcess.Text = e.TargetProcessName ?? "";
        }


        private void WhatMode_Changed(object s, RoutedEventArgs e)
        {
            if (PanelSingleScenario == null) return;
            bool isSingle = RbSingle.IsChecked == true;
            PanelSingleScenario.Visibility = isSingle ? Visibility.Visible : Visibility.Collapsed;
            PanelChain.Visibility = isSingle ? Visibility.Collapsed : Visibility.Visible;
            UpdatePreview();
        }

        private void Mode_Changed(object s, RoutedEventArgs e)
        {
            if (PanelOnce == null) return;
            PanelOnce.Visibility = RbOnce.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            PanelDaily.Visibility = RbDaily.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            PanelWeekly.Visibility = RbWeekly.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            PanelInterval.Visibility = RbInterval.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            UpdatePreview();
        }

        private void AddChainItem_Click(object s, RoutedEventArgs e)
        {
            var sc = CmbChainAdd.SelectedItem as Scenario;
            if (sc == null) return;
            int delay;
            int.TryParse(TxtChainDelay.Text, out delay);
            _entry.ChainItems.Add(new ChainItem
            {
                ScenarioId = sc.Id,
                ScenarioName = sc.Name,
                DelayAfterMs = Math.Max(0, delay),
                Order = _entry.ChainItems.Count
            });
            RefreshChain();
            UpdatePreview();
        }

        private void ChainRemove_Click(object s, RoutedEventArgs e)
        {
            if (LstChain.SelectedItem is ChainItem item)
            {
                _entry.ChainItems.Remove(item);
                RenumberChain();
                RefreshChain();
                UpdatePreview();
            }
        }

        private void ChainUp_Click(object s, RoutedEventArgs e)
        {
            int idx = LstChain.SelectedIndex;
            if (idx > 0)
            {
                _entry.ChainItems.Move(idx, idx - 1);
                RenumberChain();
                RefreshChain();
                LstChain.SelectedIndex = idx - 1;
            }
        }

        private void ChainDown_Click(object s, RoutedEventArgs e)
        {
            int idx = LstChain.SelectedIndex;
            if (idx >= 0 && idx < _entry.ChainItems.Count - 1)
            {
                _entry.ChainItems.Move(idx, idx + 1);
                RenumberChain();
                RefreshChain();
                LstChain.SelectedIndex = idx + 1;
            }
        }

        private void RenumberChain()
        {
            for (int i = 0; i < _entry.ChainItems.Count; i++)
                _entry.ChainItems[i].Order = i;
        }

        private void RefreshChain()
        {
            LstChain.ItemsSource = null;
            LstChain.ItemsSource = _entry.ChainItems;
        }

        private void UpdatePreview()
        {
            if (TxtPreview == null) return;

            string what;
            if (RbChain.IsChecked == true && _entry.ChainItems.Count > 0)
                what = "🔗 " + string.Join(" → ", _entry.ChainItems.Select(c => c.ScenarioName));
            else
            {
                var sc = CmbScenario.SelectedItem as Scenario;
                what = sc != null ? sc.Name : "—";
            }

            string when;
            if (RbOnce.IsChecked == true) when = "Однократно";
            else if (RbDaily.IsChecked == true) when = "Ежедневно";
            else if (RbWeekly.IsChecked == true) when = "По дням недели";
            else when = "С интервалом";

            string proc = string.IsNullOrWhiteSpace(TxtProcess.Text) ? "" : "\n🎯 " + TxtProcess.Text;
            TxtPreview.Text = what + "\n⏰ " + when + proc;
        }

        private void Save_Click(object s, RoutedEventArgs e)
        {
            try
            {
                bool isChain = RbChain.IsChecked == true;
                _entry.IsChain = isChain;
                _entry.StopChainOnError = ChkStopOnError.IsChecked == true;

                if (isChain)
                {
                    if (_entry.ChainItems.Count == 0)
                    {
                        MessageBox.Show("Добавьте хотя бы один сценарий в цепочку.", "Ошибка",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    var first = _entry.ChainItems[0];
                    _entry.ScenarioId = first.ScenarioId;
                    _entry.ScenarioName = string.Join(" → ",
                        _entry.ChainItems.Select(c => c.ScenarioName));
                }
                else
                {
                    var sc = CmbScenario.SelectedItem as Scenario;
                    if (sc == null)
                    {
                        MessageBox.Show("Выберите сценарий.", "Ошибка",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    _entry.ScenarioId = sc.Id;
                    _entry.ScenarioName = sc.Name;
                    _entry.ChainItems.Clear();
                }

                _entry.TargetProcessName = TxtProcess.Text.Trim();
                _entry.PreAlertEnabled = ChkPreAlert.IsChecked == true;
                int preMin;
                int.TryParse(TxtPreAlertMin.Text, out preMin);
                _entry.PreAlertMinutes = Math.Max(1, preMin);

                if (RbOnce.IsChecked == true)
                {
                    _entry.Mode = ScheduleMode.Once;
                    if (!DpDate.SelectedDate.HasValue)
                    {
                        MessageBox.Show("Укажите дату.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    int oh, om;
                    int.TryParse(TxtOnceHour.Text, out oh);
                    int.TryParse(TxtOnceMinute.Text, out om);
                    _entry.OnceDateTime = DpDate.SelectedDate.Value.Date +
                        new TimeSpan(Clamp(oh, 0, 23), Clamp(om, 0, 59), 0);
                    if (_entry.OnceDateTime <= DateTime.Now)
                    {
                        MessageBox.Show("Время должно быть в будущем.", "Ошибка",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                }
                else if (RbDaily.IsChecked == true)
                {
                    _entry.Mode = ScheduleMode.Daily;
                    int dh, dm;
                    int.TryParse(TxtDailyHour.Text, out dh);
                    int.TryParse(TxtDailyMinute.Text, out dm);
                    _entry.DailyTime = new TimeSpan(Clamp(dh, 0, 23), Clamp(dm, 0, 59), 0);
                }
                else if (RbWeekly.IsChecked == true)
                {
                    _entry.Mode = ScheduleMode.WeeklyCustom;
                    if (!_entry.WeeklyEntries.Any(x => x.Enabled))
                    {
                        MessageBox.Show("Выберите хотя бы один день.", "Ошибка",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    foreach (var we in _entry.WeeklyEntries)
                    {
                        we.Hour = Clamp(we.Hour, 0, 23);
                        we.Minute = Clamp(we.Minute, 0, 59);
                    }
                }
                else if (RbInterval.IsChecked == true)
                {
                    _entry.Mode = ScheduleMode.Interval;
                    int intMin;
                    int.TryParse(TxtIntervalMinutes.Text, out intMin);
                    _entry.IntervalMinutes = Math.Max(1, intMin);
                    _entry.IntervalTodayOnly = ChkTodayOnly.IsChecked == true;

                    int sh, sm, eh, em;
                    int.TryParse(TxtStartHour.Text, out sh);
                    int.TryParse(TxtStartMinute.Text, out sm);
                    int.TryParse(TxtEndHour.Text, out eh);
                    int.TryParse(TxtEndMinute.Text, out em);

                    var today = DateTime.Today;
                    _entry.IntervalStartTime = today + new TimeSpan(Clamp(sh, 0, 23), Clamp(sm, 0, 59), 0);
                    _entry.IntervalEndTime = today + new TimeSpan(Clamp(eh, 0, 23), Clamp(em, 0, 59), 0);

                    if (_entry.IntervalEndTime <= _entry.IntervalStartTime)
                    {
                        MessageBox.Show("Окончание должно быть позже начала.", "Ошибка",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                }

                _entry.UpdateNextRun();
                Saved = true;
                DialogResult = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка: " + ex.Message, "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Cancel_Click(object s, RoutedEventArgs e) => DialogResult = false;

        private static int Clamp(int v, int min, int max)
        {
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }
   private void ChainEditDelay_Click(object sender, RoutedEventArgs e)
{
    if (LstChain.SelectedItem is ChainItem item)
    {
        var dialog = new Window
        {
            Title = "Изменить паузу",
            Width = 300,
            Height = 150,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            Background = (Brush)Application.Current.Resources["WindowBackgroundBrush"],
            ResizeMode = ResizeMode.NoResize
        };

        var grid = new Grid { Margin = new Thickness(20) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var label = new TextBlock
        {
            Text = "Пауза после выполнения (мс):",
            Margin = new Thickness(0, 0, 0, 10),
            Foreground = (Brush)Application.Current.Resources["ForegroundBrush"]
        };
        Grid.SetRow(label, 0);

        var textBox = new TextBox
        {
            Text = item.DelayAfterMs.ToString(),
            Height = 32,
            FontSize = 14,
            TextAlignment = TextAlignment.Center,
            Background = (Brush)Application.Current.Resources["GridBackgroundBrush"],
            Foreground = (Brush)Application.Current.Resources["ForegroundBrush"],
            BorderBrush = (Brush)Application.Current.Resources["BorderBrush"]
        };
        Grid.SetRow(textBox, 1);

        var btnPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 15, 0, 0)
        };
        Grid.SetRow(btnPanel, 2);

        var btnOk = new Button
        {
            Content = "OK",
            Width = 80,
            Height = 30,
            Margin = new Thickness(0, 0, 10, 0),
            Background = new SolidColorBrush(Color.FromRgb(142, 161, 230)),
            Foreground = new SolidColorBrush(Color.FromRgb(30, 30, 46)),
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            FontWeight = FontWeights.SemiBold
        };
        btnOk.Click += (s, ev) =>
        {
            if (int.TryParse(textBox.Text, out int delay))
            {
                item.DelayAfterMs = Math.Max(0, delay);
                RefreshChain();
                UpdatePreview();
                dialog.DialogResult = true;
            }
        };

        var btnCancel = new Button
        {
            Content = "Отмена",
            Width = 80,
            Height = 30,
            Background = (Brush)Application.Current.Resources["ButtonBackgroundBrush"],
            Foreground = (Brush)Application.Current.Resources["ButtonForegroundBrush"],
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand
        };
        btnCancel.Click += (s, ev) => { dialog.DialogResult = false; };

        btnPanel.Children.Add(btnOk);
        btnPanel.Children.Add(btnCancel);

        grid.Children.Add(label);
        grid.Children.Add(textBox);
        grid.Children.Add(btnPanel);

        dialog.Content = grid;
        dialog.ShowDialog();
    }
    else
    {
        MessageBox.Show("Выберите сценарий из цепочки для изменения паузы.", "Внимание",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
    }

}