using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using MeasureControl.Models;
using Prism.Commands;
using Prism.Mvvm;

namespace MeasureControl.ViewModels.Dialogs
{
    public class SignalCalibrationDialogViewModel : BindableBase
    {
        #region Private Fields

        private string _signalInfo;
        private string _currentValueInfo;
        private double _slope = 1.0;
        private double _intercept = 0.0;
        private string _previewResult;
        private SignalConfigItem _signal;

        #endregion

        #region Properties

        public string SignalInfo
        {
            get => _signalInfo;
            set => SetProperty(ref _signalInfo, value);
        }

        public string CurrentValueInfo
        {
            get => _currentValueInfo;
            set => SetProperty(ref _currentValueInfo, value);
        }

        public double Slope
        {
            get => _slope;
            set
            {
                if (SetProperty(ref _slope, value))
                {
                    UpdatePreview();
                }
            }
        }

        public double Intercept
        {
            get => _intercept;
            set
            {
                if (SetProperty(ref _intercept, value))
                {
                    UpdatePreview();
                }
            }
        }

        public string PreviewResult
        {
            get => _previewResult;
            set => SetProperty(ref _previewResult, value);
        }

        public SignalConfigItem Result { get; private set; }

        #endregion

        #region Commands

        public ICommand OkCommand { get; }
        public ICommand CancelCommand { get; }

        #endregion

        #region Events

        public event Action RequestClose;

        #endregion

        #region Constructor

        public SignalCalibrationDialogViewModel(SignalConfigItem signal)
        {
            _signal = signal ?? throw new ArgumentNullException(nameof(signal));

            // 初始化信号信息
            SignalInfo = $"{signal.SignalName} ({signal.ActualChannel})";

            // 显示当前值
            CurrentValueInfo = $"原始值: {signal.RawValue:F3}, 实时值: {signal.RealTimeValue:F3}";

            // 加载现有标定参数
            Slope = signal.Slope;
            Intercept = signal.Intercept;

            OkCommand = new DelegateCommand(OnOk, CanOk);
            CancelCommand = new DelegateCommand(OnCancel);
        }

        #endregion

        #region Private Methods

        private void UpdatePreview()
        {
            if (_signal != null)
            {
                double calibratedValue = _signal.RawValue * Slope + Intercept;
                PreviewResult = $"{calibratedValue:F3}";
            }
        }

        private bool CanOk()
        {
            // 验证输入值
            return true; // 可以添加更严格的验证
        }

        private void OnOk()
        {
            if (!ValidateInput())
                return;

            // 创建结果，包含标定参数
            Result = new SignalConfigItem
            {
                Slope = Slope,
                Intercept = Intercept,
                IsCalibrated = true
            };

            RequestClose?.Invoke();
        }

        private void OnCancel()
        {
            Result = null;
            RequestClose?.Invoke();
        }

        private bool ValidateInput()
        {
            // 验证斜率和截距的合理性
            // 可以根据需要添加验证逻辑
            return true;
        }

        #endregion
    }
}
