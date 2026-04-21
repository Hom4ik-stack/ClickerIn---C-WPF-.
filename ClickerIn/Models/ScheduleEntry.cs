using System;
using System.Collections.ObjectModel;
using System.Linq;
using ClickerIn.Helpers;

namespace ClickerIn.Models
{
    public enum ScheduleMode
    {
        Once,
        Daily,
        WeeklyCustom,
        Interval
    }

    public sealed class DayTimeEntry : BindableBase
    {
        private DayOfWeek _day;
        private int _hour = 12;
        private int _minute;
        private bool _enabled = true;

        public DayOfWeek Day { get => _day; set => Set(ref _day, value); }
        public int Hour { get => _hour; set => Set(ref _hour, value); }
        public int Minute { get => _minute; set => Set(ref _minute, value); }
        public bool Enabled { get => _enabled; set => Set(ref _enabled, value); }
        public TimeSpan Time => new TimeSpan(Hour, Minute, 0);

        public string DayName
        {
            get
            {
                switch (Day)
                {
                    case DayOfWeek.Monday: return "Понедельник";
                    case DayOfWeek.Tuesday: return "Вторник";
                    case DayOfWeek.Wednesday: return "Среда";
                    case DayOfWeek.Thursday: return "Четверг";
                    case DayOfWeek.Friday: return "Пятница";
                    case DayOfWeek.Saturday: return "Суббота";
                    case DayOfWeek.Sunday: return "Воскресенье";
                    default: return Day.ToString();
                }
            }
        }

        public string ShortDayName
        {
            get
            {
                switch (Day)
                {
                    case DayOfWeek.Monday: return "Пн";
                    case DayOfWeek.Tuesday: return "Вт";
                    case DayOfWeek.Wednesday: return "Ср";
                    case DayOfWeek.Thursday: return "Чт";
                    case DayOfWeek.Friday: return "Пт";
                    case DayOfWeek.Saturday: return "Сб";
                    case DayOfWeek.Sunday: return "Вс";
                    default: return Day.ToString();
                }
            }
        }

        public string Display => ShortDayName + " " + Hour.ToString("D2") + ":" + Minute.ToString("D2");
    }

    public sealed class ChainItem : BindableBase
    {
        private string _scenarioId;
        private string _scenarioName;
        private int _delayAfterMs = 2000;
        private int _order;

        public string ScenarioId { get => _scenarioId; set => Set(ref _scenarioId, value); }
        public string ScenarioName { get => _scenarioName; set => Set(ref _scenarioName, value); }
        public int DelayAfterMs { get => _delayAfterMs; set => Set(ref _delayAfterMs, value); }
        public int Order { get => _order; set => Set(ref _order, value); }

        public string OrderDisplay => $"{Order + 1}.";
        public string DelayDisplay => DelayAfterMs > 0 ? $"{DelayAfterMs} мс" : "без паузы";

        public string Display
        {
            get
            {
                string pause = DelayAfterMs > 0 ? " → пауза " + DelayAfterMs + "мс" : "";
                return (Order + 1) + ". " + ScenarioName + pause;
            }
        }
    }

    public sealed class ScheduleEntry : BindableBase
    {
        private string _id = Guid.NewGuid().ToString("N");
        private string _scenarioId;
        private string _scenarioName;
        private bool _enabled = true;
        private ScheduleMode _mode = ScheduleMode.Once;
        private DateTime _onceDateTime = DateTime.Now.AddHours(1);
        private bool _onceDateTimeSet;
        private TimeSpan _dailyTime = new TimeSpan(14, 0, 0);
        private ObservableCollection<DayTimeEntry> _weeklyEntries;
        private int _intervalMinutes = 30;
        private DateTime _intervalStartTime;
        private bool _intervalStartTimeSet;
        private DateTime _intervalEndTime;
        private bool _intervalEndTimeSet;
        private bool _intervalTodayOnly = true;
        private string _targetProcessName;
        private string _targetWindowTitle;
        private int _preAlertMinutes = 5;
        private bool _preAlertEnabled = true;
        private bool _preAlertShown;
        private DateTime _lastRun;
        private bool _lastRunSet;
        private DateTime _nextRun;
        private bool _nextRunSet;
        private int _runCount;
        private bool _isChain;
        private ObservableCollection<ChainItem> _chainItems;
        private bool _stopChainOnError = true;

        public string Id { get => _id; set => Set(ref _id, value); }
        public string ScenarioId { get => _scenarioId; set => Set(ref _scenarioId, value); }
        public string ScenarioName { get => _scenarioName; set => Set(ref _scenarioName, value); }
        public bool Enabled { get => _enabled; set => Set(ref _enabled, value); }

        public ScheduleMode Mode
        {
            get => _mode;
            set { Set(ref _mode, value); OnPropertyChanged(nameof(DisplayInfo)); }
        }

        public DateTime OnceDateTime { get => _onceDateTime; set { _onceDateTimeSet = true; Set(ref _onceDateTime, value); } }
        public bool HasOnceDateTime => _onceDateTimeSet;
        public TimeSpan DailyTime { get => _dailyTime; set => Set(ref _dailyTime, value); }

        public ObservableCollection<DayTimeEntry> WeeklyEntries
        {
            get
            {
                if (_weeklyEntries == null) _weeklyEntries = CreateDefaultWeekly();
                return _weeklyEntries;
            }
            set => Set(ref _weeklyEntries, value);
        }

        public int IntervalMinutes { get => _intervalMinutes; set => Set(ref _intervalMinutes, value); }
        public DateTime IntervalStartTime { get => _intervalStartTime; set { _intervalStartTimeSet = true; Set(ref _intervalStartTime, value); } }
        public bool HasIntervalStartTime => _intervalStartTimeSet;
        public DateTime IntervalEndTime { get => _intervalEndTime; set { _intervalEndTimeSet = true; Set(ref _intervalEndTime, value); } }
        public bool HasIntervalEndTime => _intervalEndTimeSet;
        public bool IntervalTodayOnly { get => _intervalTodayOnly; set => Set(ref _intervalTodayOnly, value); }
        public string TargetProcessName { get => _targetProcessName; set => Set(ref _targetProcessName, value); }
        public string TargetWindowTitle { get => _targetWindowTitle; set => Set(ref _targetWindowTitle, value); }
        public int PreAlertMinutes { get => _preAlertMinutes; set => Set(ref _preAlertMinutes, value); }
        public bool PreAlertEnabled { get => _preAlertEnabled; set => Set(ref _preAlertEnabled, value); }
        public bool PreAlertShown { get => _preAlertShown; set => Set(ref _preAlertShown, value); }
        public DateTime LastRun { get => _lastRun; set { _lastRunSet = true; Set(ref _lastRun, value); } }
        public bool HasLastRun => _lastRunSet;
        public DateTime NextRun { get => _nextRun; set { _nextRunSet = true; Set(ref _nextRun, value); } }
        public bool HasNextRun => _nextRunSet;
        public void ClearNextRun() { _nextRunSet = false; }
        public int RunCount { get => _runCount; set => Set(ref _runCount, value); }
        public bool IsChain { get => _isChain; set => Set(ref _isChain, value); }
        public bool StopChainOnError { get => _stopChainOnError; set => Set(ref _stopChainOnError, value); }

        public ObservableCollection<ChainItem> ChainItems
        {
            get
            {
                if (_chainItems == null) _chainItems = new ObservableCollection<ChainItem>();
                return _chainItems;
            }
            set => Set(ref _chainItems, value);
        }

        private ObservableCollection<DayTimeEntry> CreateDefaultWeekly()
        {
            var list = new ObservableCollection<DayTimeEntry>();
            var days = new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday,
                DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday };
            foreach (var d in days)
                list.Add(new DayTimeEntry { Day = d, Enabled = false });
            return list;
        }

        public bool TryCalculateNextRun(out DateTime result)
        {
            var now = DateTime.Now;
            switch (Mode)
            {
                case ScheduleMode.Once:
                    if (HasOnceDateTime && OnceDateTime > now) { result = OnceDateTime; return true; }
                    result = default; return false;

                case ScheduleMode.Daily:
                    var todayDaily = now.Date + DailyTime;
                    result = todayDaily > now ? todayDaily : todayDaily.AddDays(1);
                    return true;

                case ScheduleMode.WeeklyCustom:
                    return TryCalculateNextWeekly(now, out result);

                case ScheduleMode.Interval:
                    return TryCalculateNextInterval(now, out result);

                default:
                    result = default; return false;
            }
        }

        private bool TryCalculateNextWeekly(DateTime now, out DateTime result)
        {
            var enabled = WeeklyEntries.Where(e => e.Enabled).ToList();
            if (enabled.Count == 0) { result = default; return false; }

            DateTime nearest = DateTime.MaxValue;
            bool found = false;

            for (int offset = 0; offset < 8; offset++)
            {
                var checkDate = now.Date.AddDays(offset);
                foreach (var entry in enabled.Where(e => e.Day == checkDate.DayOfWeek))
                {
                    var candidate = checkDate + entry.Time;
                    if (candidate > now && candidate < nearest) { nearest = candidate; found = true; }
                }
            }

            result = nearest; return found;
        }

        private bool TryCalculateNextInterval(DateTime now, out DateTime result)
        {
            if (IntervalTodayOnly && HasIntervalStartTime && HasIntervalEndTime)
            {
                if (now.Date > IntervalStartTime.Date) { result = default; return false; }
                if (now < IntervalStartTime) { result = IntervalStartTime; return true; }
                if (now > IntervalEndTime) { result = default; return false; }

                if (HasLastRun)
                {
                    var next = LastRun.AddMinutes(IntervalMinutes);
                    if (next <= IntervalEndTime) { result = next; return true; }
                    result = default; return false;
                }

                result = IntervalStartTime; return true;
            }

            if (HasLastRun) { result = LastRun.AddMinutes(IntervalMinutes); return true; }
            result = now; return true;
        }

        public void UpdateNextRun()
        {
            if (TryCalculateNextRun(out var next)) NextRun = next;
            else ClearNextRun();
        }

        public string DisplayName
        {
            get
            {
                if (IsChain && ChainItems.Count > 0)
                    return " " + string.Join(" → ", ChainItems.Select(c => c.ScenarioName));
                return ScenarioName ?? "—";
            }
        }

        public string DisplayInfo
        {
            get
            {
                var parts = new System.Collections.Generic.List<string>();
                parts.Add(" " + DisplayName);

                switch (Mode)
                {
                    case ScheduleMode.Once:
                        if (HasOnceDateTime) parts.Add(" " + OnceDateTime.ToString("dd.MM.yyyy HH:mm"));
                        break;
                    case ScheduleMode.Daily:
                        parts.Add(" Ежедневно " + DailyTime.ToString("hh\\:mm"));
                        break;
                    case ScheduleMode.WeeklyCustom:
                        parts.Add(" " + string.Join(", ", WeeklyEntries.Where(e => e.Enabled).Select(e => e.Display)));
                        break;
                    case ScheduleMode.Interval:
                        parts.Add(" Каждые " + IntervalMinutes + " мин");
                        break;
                }

                if (HasNextRun) parts.Add("➡" + NextRun.ToString("HH:mm"));
                if (!Enabled) parts.Add("⏸");

                return string.Join(" | ", parts);
            }
        }
    }
}