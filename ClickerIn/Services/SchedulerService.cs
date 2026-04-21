using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;
using ClickerIn.Models;

namespace ClickerIn.Services
{
    public interface ISchedulerService
    {
        void Add(ScheduleEntry entry, Func<ScheduleEntry, Task> callback);
        void Remove(string id);
        void Update(ScheduleEntry entry);
        void Stop();
        void Start();
        event Action<ScheduleEntry> PreAlert;
        event Action<ScheduleEntry> Executing;
    }

    public sealed class SchedulerService : ISchedulerService
    {
        private readonly IWindowManager _win;
        private readonly INotificationService _notify;
        private readonly ILogService _log;
        private readonly DispatcherTimer _timer;
        private readonly Dictionary<string, ScheduleEntry> _entries = new Dictionary<string, ScheduleEntry>();
        private readonly Dictionary<string, Func<ScheduleEntry, Task>> _callbacks = new Dictionary<string, Func<ScheduleEntry, Task>>();
        private readonly HashSet<string> _waitingForResponse = new HashSet<string>();

        public event Action<ScheduleEntry> PreAlert;
        public event Action<ScheduleEntry> Executing;

        public SchedulerService(IWindowManager win, INotificationService notify, ILogService log)
        {
            _win = win;
            _notify = notify;
            _log = log;
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += OnTick;
            _timer.Start();
        }

        public void Add(ScheduleEntry entry, Func<ScheduleEntry, Task> callback)
        {
            entry.UpdateNextRun();
            entry.PreAlertShown = false;
            _entries[entry.Id] = entry;
            _callbacks[entry.Id] = callback;
        }

        public void Remove(string id)
        {
            _entries.Remove(id);
            _callbacks.Remove(id);
            _waitingForResponse.Remove(id);
        }

        public void Update(ScheduleEntry entry)
        {
            entry.UpdateNextRun();
            entry.PreAlertShown = false;
            _entries[entry.Id] = entry;
            _callbacks[entry.Id] = _callbacks.ContainsKey(entry.Id)
                ? _callbacks[entry.Id]
                : null;
            _waitingForResponse.Remove(entry.Id);
        }

        public void Start() => _timer.Start();
        public void Stop() => _timer.Stop();

        private async void OnTick(object s, EventArgs e)
        {
            var now = DateTime.Now;
            var toProcess = _entries.Values.Where(x => x.Enabled).ToList();

            foreach (var entry in toProcess)
            {
                if (_waitingForResponse.Contains(entry.Id)) continue;

                if (!entry.HasNextRun)
                {
                    entry.UpdateNextRun();
                    if (!entry.HasNextRun) continue;
                }

                var nextRun = entry.NextRun;
                var preAlertTime = nextRun.AddMinutes(-entry.PreAlertMinutes);

                if (entry.PreAlertEnabled && !entry.PreAlertShown && now >= preAlertTime && now < nextRun)
                {
                    entry.PreAlertShown = true;
                    _waitingForResponse.Add(entry.Id);
                    int minutesLeft = (int)Math.Ceiling((nextRun - now).TotalMinutes);
                    string alertMsg = "Через " + minutesLeft + " мин:\n" + entry.DisplayName;
                    if (!string.IsNullOrEmpty(entry.TargetProcessName))
                        alertMsg += "\n\nОкно: " + entry.TargetProcessName;

                    _log.Info("⏰ Предупреждение: " + entry.DisplayName);
                    PreAlert?.Invoke(entry);
                    HandlePreAlertResponse(entry, alertMsg, nextRun);
                }

                if (now >= nextRun && !_waitingForResponse.Contains(entry.Id))
                    await ExecuteEntry(entry);
            }
        }

        private async void HandlePreAlertResponse(ScheduleEntry entry, string alertMsg, DateTime originalNextRun)
        {
            try
            {
                var result = await _notify.ShowScheduleAlert(
                    "ClickerIn — Запланированный запуск", alertMsg, originalNextRun);

                _waitingForResponse.Remove(entry.Id);

                switch (result)
                {
                    case NotificationResult.Accepted:
                        _log.Info("✅ Подтверждено: " + entry.DisplayName);
                        break;
                    case NotificationResult.Postponed:
                        var newTime = entry.NextRun.AddMinutes(5);
                        entry.NextRun = newTime;
                        entry.PreAlertShown = false;
                        _log.Info("⏰ Отложено +5 мин: " + entry.DisplayName + " → " + newTime.ToString("HH:mm"));
                        break;
                    case NotificationResult.Dismissed:
                        break;
                }
            }
            catch
            {
                _waitingForResponse.Remove(entry.Id);
            }
        }

        private async Task ExecuteEntry(ScheduleEntry entry)
        {
            entry.PreAlertShown = false;
            Executing?.Invoke(entry);

            if (!string.IsNullOrEmpty(entry.TargetProcessName))
            {
                var hwnd = _win.FindByProcess(entry.TargetProcessName);
                if (hwnd != IntPtr.Zero)
                {
                    _win.BringToFront(hwnd);
                    await Task.Delay(500);
                }
            }

            try
            {
                if (_callbacks.TryGetValue(entry.Id, out var cb))
                    await cb(entry);

                entry.RunCount++;
                entry.LastRun = DateTime.Now;
            }
            catch (Exception ex)
            {
                _log.Error("Ошибка: " + ex.Message);
            }

            if (entry.Mode == ScheduleMode.Once)
            {
                entry.Enabled = false;
                entry.ClearNextRun();
            }
            else if (entry.Mode == ScheduleMode.Interval)
            {
                entry.LastRun = DateTime.Now;
                entry.UpdateNextRun();
            }
            else
            {
                entry.UpdateNextRun();
            }
        }
    }
}