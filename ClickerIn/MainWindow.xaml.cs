using ClickerIn.Helpers;
using ClickerIn.Models;
using ClickerIn.Services;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ClickerIn
{
    public partial class MainWindow : Window
    {
        private readonly MouseHookService _hook;
        private readonly IInputSimulatorService _sim;
        private readonly IWindowManager _win;
       
        private readonly IHotkeyService _hk;
        private readonly IScenarioRunner _runner;
        private readonly IRecorderService _rec;
        private readonly ISchedulerService _sched;
        private readonly ILogService _log;
        private readonly IThemeService _theme;
        private readonly ISettingsService _settingsSvc;
        private readonly IScenarioStorage _storage;
        private readonly INotificationService _notify;
        private readonly IFocusWatcherService _focusWatcher;
        private readonly IAntiAfkService _antiAfk;
        private readonly IDpiService _dpi;
        private readonly IWindowProfileService _profileSvc;
        private readonly IScenarioChainRunner _chainRunner;

        private Scenario _current;
        private ObservableCollection<Scenario> _scenarios;
        private ObservableCollection<ScheduleEntry> _scheduleEntries;
        private ObservableCollection<ScenarioChain> _chains;
        private CancellationTokenSource _cts;
        private AppSettings _settings;
        private SimpleThrottle<System.Drawing.Point> _mouseThrottle;
        private OverlayWindow _overlay;

        public MainWindow()
        {
            InitializeComponent();

            _hook = App.MouseHook;
            _log = App.Log;
            _theme = App.Theme;
            _settingsSvc = App.Settings;
            _win = App.WindowManager;
            _notify = App.Notifications;
            _dpi = App.Dpi;

            _sim = new InputSimulatorService();
            
            _hk = new HotkeyService();
            _storage = new ScenarioStorageService();
            _settings = _settingsSvc.Load();
            _focusWatcher = new FocusWatcherService(_win);
            _antiAfk = new AntiAfkService(_sim);
            _profileSvc = new WindowProfileService(_win, _dpi);
            _rec = new RecorderService(_hook, _win, _dpi);
            _runner = new ScenarioRunnerService(_sim, _win, _dpi, _profileSvc);
            _sched = new SchedulerService(_win, _notify, _log);
            _chainRunner = new ScenarioChainRunner(_runner, _log);

            Init();
        }

        private void Init()
        {
            LoadData();
            SetupMouse();
            LstLog.ItemsSource = _log.Entries;
            LstTasks.ItemsSource = _scheduleEntries;
            Opacity = _settings.WindowOpacity;
            SldOpacity.Value = _settings.WindowOpacity;

            if (_rec is RecorderService rs)
                rs.StepRecorded += s => Dispatcher.Invoke(() =>
                    _log.Info("Записан: " + s.DisplayAction + " [" + s.RecordedProcessName + "]"));

            _sched.PreAlert += entry => Dispatcher.Invoke(() =>
                _log.Info("⏰ Предупреждение: " + entry.ScenarioName));

            _sched.Executing += entry => Dispatcher.Invoke(() =>
            {
                TxtStatus.Text = "▶ Авто: " + entry.ScenarioName;
                RefreshScheduleList();
            });

            if (_settings.ShowOverlayOnStart)
                Loaded += (_, __) =>
                {
                    SetupHotkeys();
                    ShowOverlayWindow();
                    if (_settings.RunInBackground) Hide();
                };
            else
                Loaded += (_, __) => SetupHotkeys();

            Closing += OnClosing;
        }

        private void LoadData()
        {
            _scenarios = new ObservableCollection<Scenario>(_storage.LoadAll());
            if (_scenarios.Count == 0)
            {
                var demo = new Scenario { Name = "Пример", Description = "Демонстрационный сценарий" };
                demo.Steps.Add(new Step
                {
                    Id = 1,
                    ClickType = ClickType.Left,
                    X = 500,
                    Y = 300,
                    DelayBeforeMs = 500,
                    DelayAfterMs = 200,
                    RandomizeCoords = true,
                    RandomCoordsRadius = 3
                });
                _scenarios.Add(demo);
            }
            LstScenarios.ItemsSource = _scenarios;
            if (_scenarios.Count > 0) LstScenarios.SelectedIndex = 0;

            _scheduleEntries = new ObservableCollection<ScheduleEntry>(_storage.LoadSchedules());
            foreach (var entry in _scheduleEntries)
            {
                entry.UpdateNextRun();
                _sched.Add(entry, async en => await OnScheduledRun(en));
            }

            _chains = new ObservableCollection<ScenarioChain>(_storage.LoadChains());
        }

        private void SaveData()
        {
            _storage.SaveAll(_scenarios.ToList());
            _storage.SaveSchedules(_scheduleEntries.ToList());
            _storage.SaveChains(_chains.ToList());
        }

        private void SetupMouse()
        {
            _mouseThrottle = new SimpleThrottle<System.Drawing.Point>(
                TimeSpan.FromMilliseconds(33), pt =>
                {
                    TxtMouse.Text = "X: " + pt.X + ", Y: " + pt.Y;
                    var h = NativeMethods.WindowFromPoint(
                        new NativeMethods.POINT { X = pt.X, Y = pt.Y });
                    var root = _win.GetRootWindow(h);
                    var proc = _win.GetProcessName(root);
                    var title = _win.GetTitle(root);
                    string display = string.IsNullOrEmpty(proc) ? title : proc + " — " + title;
                    if (display.Length > 40) display = display.Substring(0, 37) + "...";
                    TxtWindow.Text = display;
                });
            _hook.MouseMoved += pt =>
                Dispatcher.BeginInvoke(new Action(() => _mouseThrottle.Push(pt)));
        }

        private void SetupHotkeys()
        {
            _hk.Register("StartStop", _settings.StartStopKey, _settings.StartStopModifiers,
                (_, __) => Dispatcher.Invoke(Toggle));
            _hk.Register("Record", _settings.RecordKey, _settings.RecordModifiers,
                (_, __) => Dispatcher.Invoke(ToggleRec));
        }

        public void ShowOverlayWindow()
        {
            if (_overlay == null || !_overlay.IsLoaded)
            {
                _overlay = new OverlayWindow(_scenarios, _scheduleEntries,
                    _runner, _rec, _log, _win, _sched,
                    async entry => await OnScheduledRun(entry));
                _overlay.Closed += (_, __) => _overlay = null;
            }
            _overlay.Show();
            _overlay.Activate();
        }

        private async System.Threading.Tasks.Task OnScheduledRun(ScheduleEntry entry)
        {
            await Dispatcher.InvokeAsync(async () =>
            {
                if (entry.IsChain && entry.ChainItems.Count > 0)
                {
                    await RunChain(entry);
                }
                else
                {
                    var sc = _scenarios.FirstOrDefault(x => x.Id == entry.ScenarioId);
                    if (sc == null) { _log.Error("Не найден: " + entry.ScenarioName); return; }
                    await RunSingleScheduled(sc);
                }
                RefreshScheduleList();
                SaveData();
            });
        }

        private async System.Threading.Tasks.Task RunChain(ScheduleEntry entry)
        {
            _log.Info("🔗 Цепочка: " + entry.DisplayName);
            TxtStatus.Text = "🔗 Цепочка...";
            _cts = new CancellationTokenSource();
            BtnRun.IsEnabled = false;
            BtnStop.IsEnabled = true;

            try
            {
                foreach (var item in entry.ChainItems.OrderBy(c => c.Order))
                {
                    _cts.Token.ThrowIfCancellationRequested();

                    var sc = _scenarios.FirstOrDefault(x => x.Id == item.ScenarioId);
                    if (sc == null)
                    {
                        _log.Warn("⚠ Не найден: " + item.ScenarioName);
                        if (entry.StopChainOnError) break;
                        continue;
                    }

                    _log.Info("▶ [" + (item.Order + 1) + "] " + sc.Name);
                    TxtStatus.Text = "▶ " + sc.Name;

                    _current = sc;
                    LstScenarios.SelectedItem = sc;
                    Grid.ItemsSource = sc.Steps;
                    UpdateUI();

                    try
                    {
                        await _runner.Run(sc, _cts.Token);
                        _log.Info("✅ " + sc.Name);
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        _log.Error("❌ " + sc.Name + ": " + ex.Message);
                        if (entry.StopChainOnError) break;
                    }

                    if (item.DelayAfterMs > 0 && item.Order < entry.ChainItems.Count - 1)
                    {
                        _log.Info("⏳ Пауза " + item.DelayAfterMs + " мс");
                        await System.Threading.Tasks.Task.Delay(item.DelayAfterMs, _cts.Token);
                    }
                }
                _log.Info("🔗 Цепочка завершена");
                TxtStatus.Text = "✅ Цепочка завершена";
            }
            catch (OperationCanceledException) { TxtStatus.Text = "⏹ Остановлено"; }
            catch (Exception ex) { _log.Error(ex.Message); TxtStatus.Text = "❌ Ошибка"; }
            finally
            {
                _cts = null;
                BtnRun.IsEnabled = true;
                BtnStop.IsEnabled = false;
                UpdateUI();
            }
        }

        private async System.Threading.Tasks.Task RunSingleScheduled(Scenario sc)
        {
            _current = sc;
            LstScenarios.SelectedItem = sc;
            Grid.ItemsSource = sc.Steps;
            UpdateUI();

            _cts = new CancellationTokenSource();
            BtnRun.IsEnabled = false;
            BtnStop.IsEnabled = true;
            TxtStatus.Text = "▶ Авто: " + sc.Name;

            try
            {
                await _runner.Run(sc, _cts.Token);
                _log.Info("✅ " + sc.Name);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { _log.Error(ex.Message); }
            finally
            {
                _cts = null;
                BtnRun.IsEnabled = true;
                BtnStop.IsEnabled = false;
                TxtStatus.Text = "Готов";
                UpdateUI();
            }
        }




        private void AddTask_Click(object s, RoutedEventArgs e)
        {
            if (_current == null) { MessageBox.Show("Выберите сценарий."); return; }
            var w = new ScheduleEditWindow(_scenarios, _current, _win) { Owner = this };
            if (w.ShowDialog() == true && w.Saved && w.Entry != null)
            {
                var existing = _scheduleEntries.FirstOrDefault(en => en.Id == w.Entry.Id);
                if (existing != null)
                {
                    int idx = _scheduleEntries.IndexOf(existing);
                    _scheduleEntries[idx] = w.Entry;
                    _sched.Update(w.Entry);
                }
                else
                {
                    _scheduleEntries.Add(w.Entry);
                    _sched.Add(w.Entry, async entry => await OnScheduledRun(entry));
                }

                SaveData();
                _log.Info("Расписание: " + w.Entry.DisplayInfo);
                _overlay?.RefreshUpcoming();
            }
        }

        private async void DoRun()
        {
            if (_current == null || _current.Steps.Count == 0)
            { MessageBox.Show("Нет шагов."); return; }

            if (_settings.ConfirmBeforeRun &&
                MessageBox.Show("Запустить \"" + _current.Name + "\"?", "Пуск",
                    MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;

            _cts = new CancellationTokenSource();
            BtnRun.IsEnabled = false;
            BtnStop.IsEnabled = true;
            TxtStatus.Text = "▶ Работает...";
            TxtRunStatus.Text = "▶ ЗАПУЩЕН";
            TxtRunStatus.Foreground = Brushes.LimeGreen;

            var progress = new Progress<RunProgress>(p =>
            {
                PrgBar.Value = p.Percent;
                TxtProgress.Text = "Цикл " + p.Loop + ": " + p.Step + "/" + p.Total;
                TxtProgressDetail.Text = p.Message;
            });

            try
            {
                await _runner.Run(_current, _cts.Token, progress);
                TxtStatus.Text = "✅ Завершён";
            }
            catch (OperationCanceledException) { TxtStatus.Text = "⏹ Остановлен"; }
            catch (Exception ex) { _log.Error(ex.Message); TxtStatus.Text = "❌ Ошибка"; }
            finally
            {
                _cts = null;
                BtnRun.IsEnabled = true;
                BtnStop.IsEnabled = false;
                TxtRunStatus.Text = "";
                PrgBar.Value = 0;
                TxtProgress.Text = "";
                TxtProgressDetail.Text = "";
                UpdateUI();
                SaveData();
            }
        }

        private void NewScenario_Click(object s, RoutedEventArgs e)
        {
            var sc = new Scenario { Name = "Сценарий " + (_scenarios.Count + 1) };
            _scenarios.Add(sc);
            LstScenarios.SelectedItem = sc;
            SaveData();
            _log.Info("Создан: " + sc.Name);
        }

        private void DelScenario_Click(object s, RoutedEventArgs e)
        {
            if (_current == null) return;
            if (MessageBox.Show("Удалить \"" + _current.Name + "\"?", "Подтверждение",
                MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            {
                _log.Info("Удалён: " + _current.Name);
                _scenarios.Remove(_current);
                _current = null;
                Grid.ItemsSource = null;
                SaveData();
            }
        }

        private void DupScenario_Click(object s, RoutedEventArgs e)
        {
            if (_current == null) return;
            var c = new Scenario
            {
                Name = _current.Name + " (копия)",
                IsLoop = _current.IsLoop,
                LoopCount = _current.LoopCount,
                TargetProcessName = _current.TargetProcessName
            };
            foreach (var st in _current.Steps) c.Steps.Add(st.Clone());
            _scenarios.Add(c);
            LstScenarios.SelectedItem = c;
            SaveData();
        }

        private void LstScenarios_Changed(object s, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            _current = LstScenarios.SelectedItem as Scenario;
            Grid.ItemsSource = _current?.Steps;
            UpdateUI();
        }

        private void UpdateUI()
        {
            if (_current != null)
            {
                TxtName.Text = _current.Name;
                ChkLoop.IsChecked = _current.IsLoop;
                TxtLoops.Text = _current.LoopCount.ToString();
                string loopInfo = "";
                if (_current.IsLoop)
                    loopInfo = _current.LoopCount <= 0 ? " | Цикл: ∞" : " | Цикл: " + _current.LoopCount;
                TxtStepCount.Text = "Шагов: " + _current.Steps.Count + loopInfo;
                if (!string.IsNullOrEmpty(_current.TargetProcessName))
                    TxtTargetProcess.Text = "🎯 " + _current.TargetProcessName + " | " + _current.Stats.Summary;
                else
                    TxtTargetProcess.Text = "";
            }
            else
            {
                TxtName.Text = "";
                ChkLoop.IsChecked = false;
                TxtLoops.Text = "0";
                TxtStepCount.Text = "";
                TxtTargetProcess.Text = "";
            }
        }

        private void TxtName_Changed(object s, System.Windows.Controls.TextChangedEventArgs e)
        {
            if (_current != null && TxtName.Text != _current.Name)
            {
                _current.Name = TxtName.Text;
                LstScenarios.Items.Refresh();
                _overlay?.RefreshScenarios();
            }
        }

        private void Loop_Changed(object s, RoutedEventArgs e)
        {
            if (_current != null) _current.IsLoop = ChkLoop.IsChecked == true;
        }

        private void TxtLoops_Changed(object s, System.Windows.Controls.TextChangedEventArgs e)
        {
            int v;
            if (_current != null && int.TryParse(TxtLoops.Text, out v))
                _current.LoopCount = v;
        }

        private void AddStep_Click(object s, RoutedEventArgs e)
        {
            if (_current == null) return;
            _current.Steps.Add(new Step
            {
                Id = _current.Steps.Count + 1,
                ClickType = ClickType.Left,
                DelayBeforeMs = 500,
                DelayAfterMs = 100,
                RandomizeCoords = true,
                RandomCoordsRadius = 3
            });
            TxtStepCount.Text = "Шагов: " + _current.Steps.Count;
        }

        private void DelStep_Click(object s, RoutedEventArgs e)
        {
            if (_current == null || !(Grid.SelectedItem is Step)) return;
            _current.Steps.Remove((Step)Grid.SelectedItem);
            Renumber();
        }

        private void StepUp_Click(object s, RoutedEventArgs e)
        {
            if (_current == null) return;
            int i = Grid.SelectedIndex;
            if (i > 0) { _current.Steps.Move(i, i - 1); Renumber(); Grid.SelectedIndex = i - 1; }
        }

        private void StepDown_Click(object s, RoutedEventArgs e)
        {
            if (_current == null) return;
            int i = Grid.SelectedIndex;
            if (i >= 0 && i < _current.Steps.Count - 1)
            { _current.Steps.Move(i, i + 1); Renumber(); Grid.SelectedIndex = i + 1; }
        }

        private void DupStep_Click(object s, RoutedEventArgs e)
        {
            if (_current == null || !(Grid.SelectedItem is Step st)) return;
            var c = st.Clone();
            c.Id = _current.Steps.Count + 1;
            _current.Steps.Add(c);
            TxtStepCount.Text = "Шагов: " + _current.Steps.Count;
        }

        private void Renumber()
        {
            for (int i = 0; i < _current.Steps.Count; i++)
                _current.Steps[i].Id = i + 1;
            TxtStepCount.Text = "Шагов: " + _current.Steps.Count;
        }

        private void ToggleRec()
        {
            if (_rec.IsRecording) DoStopRec(); else DoStartRec();
        }

        private void StartRec_Click(object s, RoutedEventArgs e) => DoStartRec();
        private void StopRec_Click(object s, RoutedEventArgs e) => DoStopRec();

        private void DoStartRec()
        {
            if (_current == null) { MessageBox.Show("Выберите сценарий."); return; }
            var options = _current.RecordingOptions ?? new RecordingOptions();
            options.RecordMouseMovement = ChkRecordMovement.IsChecked == true;
            _current.RecordingOptions = options;
            _rec.Start(options);
            BtnRecord.IsEnabled = false;
            BtnStopRec.IsEnabled = true;
            TxtStatus.Text = "⏺ Запись...";
            _log.Info("Запись начата");
        }

        private void DoStopRec()
        {
            var steps = _rec.Stop();
            BtnRecord.IsEnabled = true;
            BtnStopRec.IsEnabled = false;
            TxtStatus.Text = "Готов";

            if (_current != null && steps.Count > 0)
            {
                int id = _current.Steps.Count + 1;
                foreach (var st in steps) { st.Id = id++; _current.Steps.Add(st); }
                TxtStepCount.Text = "Шагов: " + _current.Steps.Count;

                var mainProcess = steps
                    .Where(x => !string.IsNullOrEmpty(x.RecordedProcessName))
                    .GroupBy(x => x.RecordedProcessName)
                    .OrderByDescending(g => g.Count())
                    .FirstOrDefault();

                if (mainProcess != null && string.IsNullOrEmpty(_current.TargetProcessName))
                {
                    _current.TargetProcessName = mainProcess.Key;
                    UpdateUI();
                }

                _log.Info("Записано " + steps.Count + " шагов");
                SaveData();
            }
        }

        private void CaptureProfile_Click(object s, RoutedEventArgs e)
        {
            if (_current == null) { MessageBox.Show("Выберите сценарий."); return; }
            if (string.IsNullOrEmpty(_current.TargetProcessName))
            {
                MessageBox.Show("Запишите хотя бы один клик.", "Нет целевого процесса");
                return;
            }
            var profile = _profileSvc.CaptureProfile(_current.TargetProcessName);
            if (profile == null)
            {
                MessageBox.Show("Окно не найдено. Убедитесь что программа запущена.", "Ошибка");
                return;
            }
            _current.WindowProfile = profile;
            SaveData();
            _log.Info("Профиль: " + profile.DisplayInfo);
            UpdateUI();
        }

        private void Toggle()
        {
            if (_cts != null) DoStop(); else DoRun();
        }

        private void Run_Click(object s, RoutedEventArgs e) => DoRun();
        private void Stop_Click(object s, RoutedEventArgs e) => DoStop();


        private void DoStop()
        {
            _cts?.Cancel();
            _runner.Stop();
        }


     

   





        private void RefreshScheduleList()
        {
            LstTasks.Items.Refresh();
            _overlay?.RefreshUpcoming();
        }

    
     
        private void OpenScenario_Click(object s, RoutedEventArgs e)
        {
            var d = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "JSON|*.json",
                InitialDirectory = _storage.DefaultDir
            };
            if (d.ShowDialog() == true)
            {
                try
                {
                    var sc = _storage.LoadFile(d.FileName);
                    _scenarios.Add(sc);
                    LstScenarios.SelectedItem = sc;
                    SaveData();
                    _log.Info("Загружен: " + sc.Name);
                    _overlay?.RefreshScenarios();
                }
                catch (Exception ex) { MessageBox.Show(ex.Message); }
            }
        }

        private void SaveScenario_Click(object s, RoutedEventArgs e)
        {
            if (_current == null) return;
            SaveData();
            var d = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "JSON|*.json",
                FileName = _current.Name + ".json",
                InitialDirectory = _storage.DefaultDir
            };
            if (d.ShowDialog() == true)
            {
                try { _storage.SaveFile(_current, d.FileName); _log.Info("Экспорт: " + d.FileName); }
                catch (Exception ex) { MessageBox.Show(ex.Message); }
            }
        }

        private void Settings_Click(object s, RoutedEventArgs e)
        {
            var w = new SettingsWindow(_settings, _theme) { Owner = this };
            if (w.ShowDialog() == true)
            {
                _settings = w.Settings;
                _settingsSvc.Save(_settings);
                Opacity = _settings.WindowOpacity;
                _hk.UnregisterAll();
                SetupHotkeys();
                _log.Info("Настройки применены");
            }
        }

        private void Help_Click(object s, RoutedEventArgs e) =>
            new HelpWindow { Owner = this }.ShowDialog();

        private void Overlay_Click(object s, RoutedEventArgs e) =>
            ShowOverlayWindow();

        private void Opacity_Changed(object s, RoutedPropertyChangedEventArgs<double> e)
        {
            if (IsLoaded)
            {
                Opacity = SldOpacity.Value;
                _settings.WindowOpacity = SldOpacity.Value;
            }
        }

        private void ClearLog_Click(object s, RoutedEventArgs e) => _log.Clear();

        private void OnClosing(object s, System.ComponentModel.CancelEventArgs e)
        {
            SaveData();
            if (_settings.RunInBackground) { e.Cancel = true; Hide(); return; }
            DoShutdown();
        }

        private void DoShutdown()
        {
            SaveData();
            _mouseThrottle?.Dispose();
            _cts?.Cancel();
            _sched.Stop();
            _hk.UnregisterAll();
            _antiAfk?.Dispose();
            _focusWatcher?.Dispose();
            if (_rec is IDisposable d) d.Dispose();
            _overlay?.Close();
        }

        private void TaskItem_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is ListBoxItem item && item.Content is ScheduleEntry entry)
            {
                EditScheduleEntry(entry);
                e.Handled = true;
            }
        }

        private void MenuItem_EditTask_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem &&
                menuItem.DataContext is ScheduleEntry entry)
            {
                EditScheduleEntry(entry);
            }
        }

        private void MenuItem_DuplicateTask_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem &&
                menuItem.DataContext is ScheduleEntry entry)
            {
                DuplicateScheduleEntry(entry);
            }
        }

        private void MenuItem_DeleteTask_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem &&
                menuItem.DataContext is ScheduleEntry entry)
            {
                DeleteScheduleEntry(entry);
            }
        }

        private void MenuItem_ToggleTask_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem &&
                menuItem.DataContext is ScheduleEntry entry)
            {
                ToggleScheduleEntry(entry);
            }
        }

        private void BtnAddTask_Click(object sender, RoutedEventArgs e)
        {
            AddTask_Click(sender, e);
        }

        private void BtnEditTask_Click(object sender, RoutedEventArgs e)
        {
            if (LstTasks.SelectedItem is ScheduleEntry entry)
            {
                EditScheduleEntry(entry);
            }
            else
            {
                MessageBox.Show("Выберите расписание для редактирования.", "Внимание",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void BtnDeleteTask_Click(object sender, RoutedEventArgs e)
        {
            if (LstTasks.SelectedItem is ScheduleEntry entry)
            {
                DeleteScheduleEntry(entry);
            }
            else
            {
                MessageBox.Show("Выберите расписание для удаления.", "Внимание",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void EditScheduleEntry(ScheduleEntry entry)
        {
            if (entry == null) return;

            var w = new ScheduleEditWindow(_scenarios, null, _win, entry) { Owner = this };
            if (w.ShowDialog() == true && w.Saved && w.Entry != null)
            {
                int idx = _scheduleEntries.IndexOf(entry);
                if (idx >= 0)
                {
                    _sched.Remove(entry.Id);

                    _scheduleEntries.RemoveAt(idx);
                    _scheduleEntries.Insert(idx, w.Entry);

                    _sched.Add(w.Entry, async en => await OnScheduledRun(en));

                    SaveData();
                    LstTasks.Items.Refresh();
                    _log.Info("📝 Изменено: " + w.Entry.DisplayInfo);
                    _overlay?.RefreshUpcoming();
                }
            }
        }

        private void DuplicateScheduleEntry(ScheduleEntry entry)
        {
            if (entry == null) return;

            var duplicate = new ScheduleEntry
            {
                ScenarioId = entry.ScenarioId,
                ScenarioName = entry.ScenarioName + " (копия)",
                Mode = entry.Mode,
                Enabled = false,
                TargetProcessName = entry.TargetProcessName,
                PreAlertEnabled = entry.PreAlertEnabled,
                PreAlertMinutes = entry.PreAlertMinutes,
                IsChain = entry.IsChain,
                StopChainOnError = entry.StopChainOnError,
                DailyTime = entry.DailyTime,
                IntervalMinutes = entry.IntervalMinutes,
                IntervalTodayOnly = entry.IntervalTodayOnly
            };

            if (entry.HasOnceDateTime)
                duplicate.OnceDateTime = entry.OnceDateTime.AddDays(1);

            if (entry.HasIntervalStartTime)
                duplicate.IntervalStartTime = entry.IntervalStartTime;

            if (entry.HasIntervalEndTime)
                duplicate.IntervalEndTime = entry.IntervalEndTime;

            foreach (var we in entry.WeeklyEntries)
            {
                var newWe = duplicate.WeeklyEntries.FirstOrDefault(x => x.Day == we.Day);
                if (newWe != null)
                {
                    newWe.Enabled = we.Enabled;
                    newWe.Hour = we.Hour;
                    newWe.Minute = we.Minute;
                }
            }

            foreach (var ci in entry.ChainItems)
            {
                duplicate.ChainItems.Add(new ChainItem
                {
                    ScenarioId = ci.ScenarioId,
                    ScenarioName = ci.ScenarioName,
                    DelayAfterMs = ci.DelayAfterMs,
                    Order = ci.Order
                });
            }

            duplicate.UpdateNextRun();
            _scheduleEntries.Add(duplicate);
            _sched.Add(duplicate, async en => await OnScheduledRun(en));

            SaveData();
            LstTasks.Items.Refresh();
            _log.Info("📋 Дублировано: " + duplicate.ScenarioName);
            _overlay?.RefreshUpcoming();
        }

        private void DeleteScheduleEntry(ScheduleEntry entry)
        {
            if (entry == null) return;

            if (MessageBox.Show($"Удалить расписание:\n{entry.DisplayInfo}?",
                "Подтверждение", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                _sched.Remove(entry.Id);
                _scheduleEntries.Remove(entry);

                SaveData();
                LstTasks.Items.Refresh();
                _log.Info("🗑 Удалено: " + entry.ScenarioName);
                _overlay?.RefreshUpcoming();
            }
        }

        private void ToggleScheduleEntry(ScheduleEntry entry)
        {
            if (entry == null) return;

            entry.Enabled = !entry.Enabled;

            if (entry.Enabled)
            {
                entry.UpdateNextRun();
                _log.Info("▶ Включено: " + entry.ScenarioName);
            }
            else
            {
                entry.ClearNextRun();
                _log.Info("⏸ Отключено: " + entry.ScenarioName);
            }

            SaveData();
            LstTasks.Items.Refresh();
            _overlay?.RefreshUpcoming();
        }

        private void DelTask_Click(object sender, RoutedEventArgs e)
        {
            if (LstTasks.SelectedItem is ScheduleEntry entry)
            {
                DeleteScheduleEntry(entry);
            }
            else
            {
                MessageBox.Show("Выберите расписание для удаления.", "Внимание",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void LstTasks_RightClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is ListBox listBox && listBox.SelectedItem is ScheduleEntry entry)
            {
                var contextMenu = listBox.ContextMenu;
                if (contextMenu != null)
                {
                    contextMenu.DataContext = entry;
                    contextMenu.IsOpen = true;
                    e.Handled = true;
                }
            }
        }

    }
}