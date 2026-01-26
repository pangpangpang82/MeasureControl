using System;
using System.Collections.Generic;
using Prism.Mvvm;

namespace MeasureControl.Models
{
    /// <summary>
    /// 通道校准记录
    /// </summary>
    public class ChannelCalibrationRecord : BindableBase
    {
        private string _channelAddress;
        private string _channelName;
        private double _slope;
        private double _intercept;
        private DateTime _lastCalibrationTime;
        private bool _isCalibrated;
        private int _measurementPointCount;
        private List<double> _instrumentSetValues;
        private List<double> _cardMeasuredValues;

        /// <summary>
        /// 通道地址（格式：AI0, AI1, ... AI15）
        /// </summary>
        public string ChannelAddress
        {
            get => _channelAddress;
            set => SetProperty(ref _channelAddress, value);
        }

        /// <summary>
        /// 通道名称（用户可修改的显示名称）
        /// </summary>
        public string ChannelName
        {
            get => _channelName;
            set => SetProperty(ref _channelName, value);
        }

        /// <summary>
        /// 斜率
        /// </summary>
        public double Slope
        {
            get => _slope;
            set => SetProperty(ref _slope, value);
        }

        /// <summary>
        /// 截距
        /// </summary>
        public double Intercept
        {
            get => _intercept;
            set => SetProperty(ref _intercept, value);
        }

        /// <summary>
        /// 上次校准时间
        /// </summary>
        public DateTime LastCalibrationTime
        {
            get => _lastCalibrationTime;
            set => SetProperty(ref _lastCalibrationTime, value);
        }

        /// <summary>
        /// 是否已校准
        /// </summary>
        public bool IsCalibrated
        {
            get => _isCalibrated;
            set => SetProperty(ref _isCalibrated, value);
        }

        /// <summary>
        /// 测量点数
        /// </summary>
        public int MeasurementPointCount
        {
            get => _measurementPointCount;
            set => SetProperty(ref _measurementPointCount, value);
        }

        /// <summary>
        /// 仪器设定值列表
        /// </summary>
        public List<double> InstrumentSetValues
        {
            get => _instrumentSetValues;
            set => SetProperty(ref _instrumentSetValues, value);
        }

        /// <summary>
        /// 板卡测量值列表
        /// </summary>
        public List<double> CardMeasuredValues
        {
            get => _cardMeasuredValues;
            set => SetProperty(ref _cardMeasuredValues, value);
        }
    }
}

