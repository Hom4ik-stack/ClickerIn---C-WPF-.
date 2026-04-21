using System;
using System.Collections.ObjectModel;
using System.Windows.Threading;

namespace ClickerIn.Services
{
    public interface ILogService
    {
        ObservableCollection<LogEntry> Entries { get; }
        void Info(string msg);
        void Warn(string msg);
        void Error(string msg);
        void Clear();
    }

    public sealed class LogEntry
    {
        public DateTime Time { get; set; }
        public LogLevel Level { get; set; }
        public string Message { get; set; }
        public override string ToString() => $"[{Time:HH:mm:ss}] [{Level}] {Message}";
    }

    public enum LogLevel { Info, Warn, Error }

    public sealed class LogService : ILogService
    {
        private readonly Dispatcher _disp;
        private readonly int _max;
        public ObservableCollection<LogEntry> Entries { get; } = new ObservableCollection<LogEntry>();

        public LogService(int max = 500)
        {
            _max = max;
            _disp = Dispatcher.CurrentDispatcher;
        }

        public void Info(string msg) => Add(LogLevel.Info, msg);
        public void Warn(string msg) => Add(LogLevel.Warn, msg);
        public void Error(string msg) => Add(LogLevel.Error, msg);
        public void Clear() => _disp.Invoke(() => Entries.Clear());

        private void Add(LogLevel lvl, string msg)
        {
            var e = new LogEntry { Time = DateTime.Now, Level = lvl, Message = msg };
            if (_disp.CheckAccess()) Insert(e);
            else _disp.BeginInvoke(new Action(() => Insert(e)));
        }

        private void Insert(LogEntry e)
        {
            Entries.Add(e);
            if (Entries.Count > _max + 50)
            {
                while (Entries.Count > _max)
                    Entries.RemoveAt(0);
            }
        }
    }
}