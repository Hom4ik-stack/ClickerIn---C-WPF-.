using System;
using System.Collections.ObjectModel;
using ClickerIn.Helpers;

namespace ClickerIn.Models
{
    public sealed class ScenarioChainItem : BindableBase
    {
        private string _scenarioId;
        private string _scenarioName;
        private int _delayAfterMs = 2000;
        private bool _enabled = true;
        private int _order;
      public string ScenarioId { get => _scenarioId; set => Set(ref _scenarioId, value); }
        public string ScenarioName { get => _scenarioName; set => Set(ref _scenarioName, value); }
        public int DelayAfterMs { get => _delayAfterMs; set => Set(ref _delayAfterMs, value); }
        public bool Enabled { get => _enabled; set => Set(ref _enabled, value); }
        public int Order { get => _order; set => Set(ref _order, value); }
        public string Display => (Order + 1) + ". " + ScenarioName +
            (DelayAfterMs > 0 ? " (пауза " + DelayAfterMs + "мс)" : "");
    }

    public sealed class ScenarioChain : BindableBase
    {
        private string _id = Guid.NewGuid().ToString("N");
        private string _name = "Новая цепочка";
        private bool _stopOnError = true;

        public string Id { get => _id; set => Set(ref _id, value); }
        public string Name { get => _name; set => Set(ref _name, value); }
        public ObservableCollection<ScenarioChainItem> Items { get; set; } = new ObservableCollection<ScenarioChainItem>();
        public bool StopOnError { get => _stopOnError; set => Set(ref _stopOnError, value); }

        public string Display
        {
            get
            {
                if (Items.Count == 0) return Name + " (пусто)";
                return Name + " (" + Items.Count + " сценариев)";
            }
        }

        public override string ToString() => Name;
    }
}