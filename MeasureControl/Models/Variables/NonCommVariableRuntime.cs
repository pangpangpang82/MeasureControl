using System;
using System.ComponentModel;

namespace MeasureControl.Models.Variables
{
    /// <summary>
    /// 非通讯变量的运行态（不持久化）
    /// </summary>
    public sealed class NonCommVariableRuntime : INotifyPropertyChanged
    {
        private object _currentValue;
        public object CurrentValue
        {
            get => _currentValue;
            set { _currentValue = value; OnPropertyChanged(nameof(CurrentValue)); }
        }

        public DateTime Timestamp { get; set; } = DateTime.Now;
        public string Quality { get; set; } = "Good";
        public bool IsValid { get; set; } = true;
        public string ValidationMessage { get; set; }
        public string LastError { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

