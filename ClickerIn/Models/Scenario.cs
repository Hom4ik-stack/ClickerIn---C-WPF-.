using System;
using System.Collections.ObjectModel;
using ClickerIn.Helpers;

namespace ClickerIn.Models
{
    public sealed class Scenario : BindableBase
    {
        private string _id = Guid.NewGuid().ToString("N");
        private string _name = "Новый сценарий";
        private string _description = "";
        private bool _isLoop;
        private int _loopCount;
        private int _maxDurationMinutes;
        private HotkeySettings _hotkey;
        private RepeatSettings _repeat;
        private string _targetProcessName;
        private string _targetWindowTitle;
        private ScenarioStats _stats;
        private bool _pauseOnFocusLost;
        private bool _humanizeDelays = true;
        private int _humanizePercent = 15;
        private WindowProfile _windowProfile;
        private RecordingOptions _recordingOptions;

        public string Id { get => _id; set => Set(ref _id, value); }
        public string Name { get => _name; set => Set(ref _name, value); }
        public string Description { get => _description; set => Set(ref _description, value); }
        public ObservableCollection<Step> Steps { get; set; } = new ObservableCollection<Step>();
        public bool IsLoop { get => _isLoop; set => Set(ref _isLoop, value); }
        public int LoopCount { get => _loopCount; set => Set(ref _loopCount, value); }
        public int MaxDurationMinutes { get => _maxDurationMinutes; set => Set(ref _maxDurationMinutes, value); }
        public HotkeySettings Hotkey { get => _hotkey; set => Set(ref _hotkey, value); }
        public RepeatSettings Repeat { get => _repeat; set => Set(ref _repeat, value); }
        public string TargetProcessName { get => _targetProcessName; set => Set(ref _targetProcessName, value); }
        public string TargetWindowTitle { get => _targetWindowTitle; set => Set(ref _targetWindowTitle, value); }
        public ScenarioStats Stats { get => _stats ?? (_stats = new ScenarioStats()); set => Set(ref _stats, value); }
        public bool PauseOnFocusLost { get => _pauseOnFocusLost; set => Set(ref _pauseOnFocusLost, value); }
        public bool HumanizeDelays { get => _humanizeDelays; set => Set(ref _humanizeDelays, value); }
        public int HumanizePercent { get => _humanizePercent; set => Set(ref _humanizePercent, value); }
        public WindowProfile WindowProfile { get => _windowProfile; set => Set(ref _windowProfile, value); }

        public RecordingOptions RecordingOptions
        {
            get => _recordingOptions ?? (_recordingOptions = new RecordingOptions());
            set => Set(ref _recordingOptions, value);
        }

        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime? ModifiedAt { get; set; }

        public override string ToString() => Name;
    }
}