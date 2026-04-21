using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using ClickerIn.Models;
using ClickerIn.Services;

namespace ClickerIn
{
    public partial class OverlayWindow : Window
    {
        private readonly ObservableCollection<Scenario> _scenarios;
        private readonly ObservableCollection<ScheduleEntry> _entries;
        private readonly IScenarioRunner _runner;
        private readonly IRecorderService _rec;
        private readonly ILogService _log;
        private readonly IWindowManager _win;
        private readonly ISchedulerService _sched;
        private readonly Func<ScheduleEntry, Task> _onScheduledRun;
        private CancellationTokenSource _cts;

        public OverlayWindow(
            ObservableCollection<Scenario> scenarios,
            ObservableCollection<ScheduleEntry> entries,
            IScenarioRunner runner,
            IRecorderService rec,
            ILogService log,
            IWindowManager win,
            ISchedulerService sched,
            Func<ScheduleEntry, Task> onScheduledRun)
        {
            InitializeComponent();
            _scenarios = scenarios;
            _entries = entries;
            _runner = runner;
            _rec = rec;
            _log = log;
            _win = win;
            _sched = sched;
            _onScheduledRun = onScheduledRun;

            CmbScenarios.ItemsSource = _scenarios;
            CmbScenarios.DisplayMemberPath = "Name";
            if (_scenarios.Count > 0) CmbScenarios.SelectedIndex = 0;

            RefreshUpcoming();
        }

        public void RefreshScenarios()
        {
            var sel = CmbScenarios.SelectedItem;
            CmbScenarios.ItemsSource = null;
            CmbScenarios.ItemsSource = _scenarios;
            CmbScenarios.DisplayMemberPath = "Name";
            if (sel != null && _scenarios.Contains((Scenario)sel))
                CmbScenarios.SelectedItem = sel;
            else if (_scenarios.Count > 0)
                CmbScenarios.SelectedIndex = 0;
        }

        public void RefreshUpcoming()
        {
            var upcoming = _entries
                .Where(e => e.Enabled && e.HasNextRun)
                .OrderBy(e => e.NextRun)
                .Take(5)
                .Select(e => e.NextRun.ToString("dd.MM HH:mm") + " — " + e.DisplayName)
                .ToList();

            if (upcoming.Count == 0) upcoming.Add("Нет запланированных");
            LstUpcoming.ItemsSource = upcoming;
        }

        private async void RunOverlay_Click(object s, RoutedEventArgs e)
        {
            var sc = CmbScenarios.SelectedItem as Scenario;
            if (sc == null || sc.Steps.Count == 0)
            {
                TxtOverlayStatus.Text = "⚠ Нет шагов";
                return;
            }

            _cts = new CancellationTokenSource();
            BtnOverlayRun.IsEnabled = false;
            BtnOverlayStop.IsEnabled = true;
            TxtOverlayStatus.Text = "▶ " + sc.Name;

            var progress = new Progress<RunProgress>(p =>
            {
                TxtOverlayStatus.Text = $"▶ Цикл {p.Loop}: {p.Step}/{p.Total} ({p.Percent:F0}%)";
            });

            try
            {
                await _runner.Run(sc, _cts.Token, progress);
                TxtOverlayStatus.Text = "✅ Готово";
            }
            catch (OperationCanceledException) { TxtOverlayStatus.Text = "⏹ Стоп"; }
            catch (Exception ex) { TxtOverlayStatus.Text = "❌ " + ex.Message; }
            finally
            {
                _cts = null;
                BtnOverlayRun.IsEnabled = true;
                BtnOverlayStop.IsEnabled = false;
            }
        }

        private void StopOverlay_Click(object s, RoutedEventArgs e)
        {
            _cts?.Cancel();
            _runner.Stop();
        }

        private void RecordOverlay_Click(object s, RoutedEventArgs e)
        {
            var sc = CmbScenarios.SelectedItem as Scenario;
            if (sc == null) { TxtOverlayStatus.Text = "⚠ Выберите сценарий"; return; }

            var options = sc.RecordingOptions ?? new RecordingOptions();
            _rec.Start(options);
            BtnOverlayRecord.IsEnabled = false;
            BtnOverlayStopRec.IsEnabled = true;
            TxtOverlayStatus.Text = "⏺ Запись...";
        }

        private void StopRecOverlay_Click(object s, RoutedEventArgs e)
        {
            var steps = _rec.Stop();
            BtnOverlayRecord.IsEnabled = true;
            BtnOverlayStopRec.IsEnabled = false;

            var sc = CmbScenarios.SelectedItem as Scenario;
            if (sc != null && steps.Count > 0)
            {
                int id = sc.Steps.Count + 1;
                foreach (var st in steps) { st.Id = id++; sc.Steps.Add(st); }
                TxtOverlayStatus.Text = "✅ +" + steps.Count + " шагов";
            }
            else
            {
                TxtOverlayStatus.Text = "Готов";
            }
        }

        private void ScheduleOverlay_Click(object s, RoutedEventArgs e)
        {
            var sc = CmbScenarios.SelectedItem as Scenario;
            if (sc == null) { TxtOverlayStatus.Text = "⚠ Выберите сценарий"; return; }

            var w = new ScheduleEditWindow(_scenarios, sc, _win) { Owner = this };
            if (w.ShowDialog() == true && w.Saved && w.Entry != null)
            {
                var existing = _entries.FirstOrDefault(en => en.Id == w.Entry.Id);
                if (existing != null)
                {
                    int idx = _entries.IndexOf(existing);
                    _entries[idx] = w.Entry;
                    _sched.Update(w.Entry);
                }
                else
                {
                    _entries.Add(w.Entry);
                    _sched.Add(w.Entry, _onScheduledRun);
                }

                TxtOverlayStatus.Text = "⏰ Добавлено";
                RefreshUpcoming();
            }
        }

        private void OverlayDrag(object s, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left) DragMove();
        }

        private void CloseOverlay_Click(object s, RoutedEventArgs e) => Close();
        private void MainWindow_Click(object s, RoutedEventArgs e) => App.ShowMainWindow();

        private void UpcomingItem_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (LstUpcoming.SelectedItem is string selected)
            {
                var entry = FindEntryByDisplayString(selected);
                if (entry != null)
                {
                    EditScheduleFromOverlay(entry);
                }
            }
        }

        private void EditUpcoming_Click(object sender, RoutedEventArgs e)
        {
            if (LstUpcoming.SelectedItem is string selected)
            {
                var entry = FindEntryByDisplayString(selected);
                if (entry != null)
                {
                    EditScheduleFromOverlay(entry);
                }
            }
        }

        private ScheduleEntry FindEntryByDisplayString(string display)
        {
            if (string.IsNullOrWhiteSpace(display)) return null;

            return _entries.FirstOrDefault(e =>
                display.Contains(e.DisplayName) ||
                display.Contains(e.ScenarioName ?? ""));
        }

        private void EditScheduleFromOverlay(ScheduleEntry entry)
        {
            var w = new ScheduleEditWindow(_scenarios, null, _win, entry) { Owner = this };
            if (w.ShowDialog() == true && w.Saved && w.Entry != null)
            {
                int idx = _entries.IndexOf(entry);
                if (idx >= 0)
                {
                    _entries.RemoveAt(idx);
                    _entries.Insert(idx, w.Entry);

                    _sched.Remove(entry.Id);
                    _sched.Add(w.Entry, _onScheduledRun);

                    RefreshUpcoming();
                    TxtOverlayStatus.Text = "📝 Изменено";
                }
            }
        }
    }
}