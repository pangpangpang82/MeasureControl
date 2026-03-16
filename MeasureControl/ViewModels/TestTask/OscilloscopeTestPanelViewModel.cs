using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MeasureControl.Drivers;
using MeasureControl.Drivers.ArtSwitch;
using MeasureControl.Models;
using MeasureControl.Models.Devices;
using MeasureControl.Services;
using MeasureControl.Views;
using MeasureControl.Views.Dialogs;
using NationalInstruments.Visa;
using Microsoft.Win32;
using Prism.Commands;
using Prism.Mvvm;
using Microsoft.VisualBasic;

namespace MeasureControl.ViewModels.TestTask
{
    /// <summary>
    /// 示波器独立测试面板ViewModel
    /// </summary>
    public class OscilloscopeTestPanelViewModel : BindableBase, IDisposable
    {
        private readonly IPxiChassisService _pxiChassisService;
        private NationalInstruments.Visa.MessageBasedSession _oscilloscopeSession;
        private NationalInstruments.Visa.ResourceManager _resourceManager;
        private TcpClient _oscilloscopeTcpClient;
        private NetworkStream _oscilloscopeTcpStream;
        private IDeviceDriver _analogOutputDriver; // 模拟量输出板卡驱动
        private AnalogOutputDevice _analogOutputDevice; // 模拟量输出设备
        private readonly SemaphoreSlim _oscilloscopeIoLock = new SemaphoreSlim(1, 1);
        private CancellationTokenSource _oscilloscopeMeasureCts;
        private Task _oscilloscopeMeasureTask;
        private CancellationTokenSource _oscilloscopeUiRefreshCts;
        private Task _oscilloscopeUiRefreshTask;
        private bool _oscilloscopeMeasureItemsDirty;
        private string _oscilloscopeMeasureCategory;
        private bool _disposed = false;
        private byte[] _lastScreenshotBytes;

        

        #region Properties

        private string _cardName;
        /// <summary>
        /// 仪表名称
        /// </summary>
        public string CardName
        {
            get => _cardName;
            set => SetProperty(ref _cardName, value);
        }

        private ImageSource _screenshotPreview;
        public ImageSource ScreenshotPreview
        {
            get => _screenshotPreview;
            set => SetProperty(ref _screenshotPreview, value);
        }

        private PointCollection _waveformPoints = new PointCollection();
        public PointCollection WaveformPoints
        {
            get => _waveformPoints;
            set => SetProperty(ref _waveformPoints, value);
        }

        private string _screenshotSavePath;
        public string ScreenshotSavePath
        {
            get => _screenshotSavePath;
            set => SetProperty(ref _screenshotSavePath, value);
        }

        private string _scpiCommandText;
        public string ScpiCommandText
        {
            get => _scpiCommandText;
            set => SetProperty(ref _scpiCommandText, value);
        }

        private string _scpiResponseText;
        public string ScpiResponseText
        {
            get => _scpiResponseText;
            set => SetProperty(ref _scpiResponseText, value);
        }

        private async Task SendOscilloscopeCommandAsync(string command)
        {
            if (_oscilloscopeSession == null && _oscilloscopeTcpStream == null)
                return;

            await _oscilloscopeIoLock.WaitAsync(CancellationToken.None);
            try
            {
                await Task.Run(() =>
                {
                    if (_oscilloscopeSession != null)
                    {
                        _oscilloscopeSession.RawIO.Write(command);
                        return;
                    }

                    var stream = _oscilloscopeTcpStream;
                    if (stream == null)
                        return;
                    var bytes = Encoding.ASCII.GetBytes(command.EndsWith("\n") ? command : command + "\n");
                    stream.Write(bytes, 0, bytes.Length);
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[OscilloscopeTestPanel] 发送指令失败 {command}: {ex.Message}");
                ReMessageBox.Show($"发送指令失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _oscilloscopeIoLock.Release();
            }
        }

        private async Task TakeScreenshotAsync()
        {
            if (_oscilloscopeSession == null && _oscilloscopeTcpStream == null)
                return;

            try
            {
                byte[] imageBytes;

                await _oscilloscopeIoLock.WaitAsync(CancellationToken.None);
                try
                {
                    imageBytes = await Task.Run(() =>
                    {
                        if (_oscilloscopeSession != null)
                        {
                            _oscilloscopeSession.RawIO.Write(":DISPlay:DATA?");
                            var block = ReadIeee4882DefiniteLengthBlock(_oscilloscopeSession, 100_000_000);
                            return block;
                        }

                        var stream = _oscilloscopeTcpStream;
                        if (stream == null)
                            return Array.Empty<byte>();

                        var cmd = Encoding.ASCII.GetBytes(":DISPlay:DATA?\n");
                        stream.Write(cmd, 0, cmd.Length);
                        return ReadIeee4882DefiniteLengthBlock(stream, 100_000_000);
                    });
                }
                finally
                {
                    _oscilloscopeIoLock.Release();
                }

                if (imageBytes == null || imageBytes.Length == 0)
                {
                    ReMessageBox.Show("未获取到截图数据", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                _lastScreenshotBytes = imageBytes;
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    ScreenshotPreview = CreateBitmapImage(imageBytes);
                });

                var targetPath = ScreenshotSavePath;
                if (string.IsNullOrWhiteSpace(targetPath))
                {
                    var fileName = $"ScreenShot_{DateTime.Now:yyyyMMdd_HHmmss}.png";
                    targetPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName);
                }

                File.WriteAllBytes(targetPath, imageBytes);
                ScreenshotSavePath = targetPath;

                ReMessageBox.Show($"截图保存成功: {targetPath}", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[OscilloscopeTestPanel] 截图失败: {ex.Message}");
                ReMessageBox.Show($"截图失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private Task PreviewScreenshotAsync()
        {
            return PreviewScreenshotAsync(CancellationToken.None, false);
        }

        private async Task PreviewScreenshotAsync(CancellationToken token, bool silent)
        {
            if (_oscilloscopeSession == null && _oscilloscopeTcpStream == null)
                return;

            try
            {
                byte[] imageBytes;
                await _oscilloscopeIoLock.WaitAsync(token);
                try
                {
                    imageBytes = await Task.Run(() =>
                    {
                        if (_oscilloscopeSession != null)
                        {
                            _oscilloscopeSession.RawIO.Write(":DISPlay:DATA?");
                            return ReadIeee4882DefiniteLengthBlock(_oscilloscopeSession, 100_000_000);
                        }

                        var stream = _oscilloscopeTcpStream;
                        if (stream == null)
                            return Array.Empty<byte>();

                        var cmd = Encoding.ASCII.GetBytes(":DISPlay:DATA?\n");
                        stream.Write(cmd, 0, cmd.Length);
                        return ReadIeee4882DefiniteLengthBlock(stream, 100_000_000);
                    }, token);
                }
                finally
                {
                    _oscilloscopeIoLock.Release();
                }

                if (imageBytes == null || imageBytes.Length == 0)
                {
                    if (!silent)
                        ReMessageBox.Show("未获取到截图数据", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                _lastScreenshotBytes = imageBytes;
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    ScreenshotPreview = CreateBitmapImage(imageBytes);
                });
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[OscilloscopeTestPanel] 截图预览失败: {ex.Message}");
                if (!silent)
                    ReMessageBox.Show($"截图预览失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BrowseScreenshotSavePath()
        {
            try
            {
                var dlg = new SaveFileDialog
                {
                    Filter = "PNG Image (*.png)|*.png|All Files (*.*)|*.*",
                    FileName = $"ScreenShot_{DateTime.Now:yyyyMMdd_HHmmss}.png"
                };

                if (!string.IsNullOrWhiteSpace(ScreenshotSavePath))
                {
                    try
                    {
                        dlg.InitialDirectory = Path.GetDirectoryName(ScreenshotSavePath);
                        dlg.FileName = Path.GetFileName(ScreenshotSavePath);
                    }
                    catch
                    {
                    }
                }

                if (dlg.ShowDialog() == true)
                {
                    ScreenshotSavePath = dlg.FileName;
                }
            }
            catch (Exception ex)
            {
                ReMessageBox.Show($"选择保存路径失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SaveScreenshotAs()
        {
            if (_lastScreenshotBytes == null || _lastScreenshotBytes.Length == 0)
            {
                ReMessageBox.Show("请先获取截图预览", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                var dlg = new SaveFileDialog
                {
                    Filter = "PNG Image (*.png)|*.png|All Files (*.*)|*.*",
                    FileName = $"ScreenShot_{DateTime.Now:yyyyMMdd_HHmmss}.png"
                };

                if (!string.IsNullOrWhiteSpace(ScreenshotSavePath))
                {
                    try
                    {
                        dlg.InitialDirectory = Path.GetDirectoryName(ScreenshotSavePath);
                        dlg.FileName = Path.GetFileName(ScreenshotSavePath);
                    }
                    catch
                    {
                    }
                }

                if (dlg.ShowDialog() == true)
                {
                    File.WriteAllBytes(dlg.FileName, _lastScreenshotBytes);
                    ScreenshotSavePath = dlg.FileName;
                    ReMessageBox.Show($"截图保存成功: {dlg.FileName}", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                ReMessageBox.Show($"保存截图失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static BitmapImage CreateBitmapImage(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
                return null;

            var img = new BitmapImage();
            using (var ms = new MemoryStream(bytes))
            {
                img.BeginInit();
                img.CacheOption = BitmapCacheOption.OnLoad;
                img.StreamSource = ms;
                img.EndInit();
                img.Freeze();
            }
            return img;
        }

        private static byte[] ReadIeee4882DefiniteLengthBlock(MessageBasedSession session, int maxBytes)
        {
            if (session == null)
                throw new ArgumentNullException(nameof(session));
            if (maxBytes <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxBytes));

            var data = session.RawIO.Read(Math.Min(maxBytes, 4096));
            if (data == null || data.Length < 3)
                return data;

            int start = Array.IndexOf(data, (byte)'#');
            if (start < 0 || start + 2 >= data.Length)
                return data;

            int nDigits = data[start + 1] - (byte)'0';
            if (nDigits < 0 || nDigits > 9)
                return data;
            if (start + 2 + nDigits > data.Length)
                return data;

            var lenBytes = new byte[nDigits];
            Array.Copy(data, start + 2, lenBytes, 0, nDigits);
            if (!int.TryParse(Encoding.ASCII.GetString(lenBytes), out var payloadLen) || payloadLen < 0)
                return data;

            int payloadStart = start + 2 + nDigits;
            int totalNeeded = payloadStart + payloadLen;
            if (totalNeeded > maxBytes)
                throw new InvalidOperationException($"块数据长度超出限制: {payloadLen} bytes");

            if (data.Length < totalNeeded)
            {
                using (var ms = new MemoryStream(totalNeeded))
                {
                    ms.Write(data, 0, data.Length);
                    int remaining = totalNeeded - data.Length;
                    while (remaining > 0)
                    {
                        var chunk = session.RawIO.Read(remaining);
                        if (chunk == null || chunk.Length == 0)
                            break;
                        ms.Write(chunk, 0, chunk.Length);
                        remaining -= chunk.Length;
                    }
                    data = ms.ToArray();
                }
            }

            int available = Math.Max(0, data.Length - payloadStart);
            int copyLen = Math.Min(payloadLen, available);
            var payload = new byte[copyLen];
            Array.Copy(data, payloadStart, payload, 0, copyLen);
            return payload;
        }

        private static byte[] ReadIeee4882DefiniteLengthBlock(NetworkStream stream, int maxBytes)
        {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));
            if (maxBytes <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxBytes));

            try
            {
                int b;
                do
                {
                    b = stream.ReadByte();
                    if (b < 0)
                        return Array.Empty<byte>();
                } while (b != '#');

                int n = stream.ReadByte();
                if (n < 0)
                    return Array.Empty<byte>();
                int nDigits = n - '0';
                if (nDigits < 0 || nDigits > 9)
                    return Array.Empty<byte>();

                var lenBuf = new byte[nDigits];
                ReadExact(stream, lenBuf, 0, nDigits);
                if (!int.TryParse(Encoding.ASCII.GetString(lenBuf), out var payloadLen) || payloadLen < 0)
                    return Array.Empty<byte>();
                if (payloadLen > maxBytes)
                    throw new InvalidOperationException($"块数据长度超出限制: {payloadLen} bytes");

                var payload = new byte[payloadLen];
                ReadExact(stream, payload, 0, payloadLen);

                try
                {
                    while (stream.DataAvailable)
                    {
                        int next = stream.ReadByte();
                        if (next < 0)
                            break;
                        if (next != '\n' && next != '\r')
                            break;
                    }
                }
                catch
                {
                }
                return payload;
            }
            catch (IOException)
            {
                return Array.Empty<byte>();
            }
            catch (System.Net.Sockets.SocketException)
            {
                return Array.Empty<byte>();
            }
            catch (ObjectDisposedException)
            {
                return Array.Empty<byte>();
            }
            catch (InvalidOperationException)
            {
                return Array.Empty<byte>();
            }
            catch
            {
                return Array.Empty<byte>();
            }
        }

        private static void ReadExact(NetworkStream stream, byte[] buffer, int offset, int count)
        {
            int read = 0;
            while (read < count)
            {
                int r = stream.Read(buffer, offset + read, count - read);
                if (r <= 0)
                    throw new IOException("网络读取失败");
                read += r;
            }
        }

        private static async Task<string> ReadLineAsync(NetworkStream stream, int timeoutMs, CancellationToken token)
        {
            using (var cts = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                cts.CancelAfter(timeoutMs);
                var sb = new StringBuilder();
                var buf = new byte[1];
                while (true)
                {
                    int n;
                    try
                    {
                        n = await stream.ReadAsync(buf, 0, 1, cts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        if (token.IsCancellationRequested)
                            throw;
                        throw new TimeoutException($"网络读取超时({timeoutMs}ms)");
                    }
                    if (n <= 0)
                        break;
                    char ch = (char)buf[0];
                    if (ch == '\n')
                        break;
                    if (ch != '\r')
                        sb.Append(ch);
                }
                return sb.ToString().Trim();
            }
        }

        private void WriteOscilloscopeUnsafe(string command)
        {
            if (_oscilloscopeSession != null)
            {
                _oscilloscopeSession.RawIO.Write(command);
                return;
            }

            var stream = _oscilloscopeTcpStream;
            if (stream == null)
                return;
            var bytes = Encoding.ASCII.GetBytes(command.EndsWith("\n") ? command : command + "\n");
            stream.Write(bytes, 0, bytes.Length);
        }

        private string _oscilloscopeIpAddress;
        /// <summary>
        /// 示波器设备IP地址
        /// </summary>
        public string OscilloscopeIpAddress
        {
            get => _oscilloscopeIpAddress;
            set => SetProperty(ref _oscilloscopeIpAddress, value);
        }

        private bool _isOscilloscopeConnected;
        /// <summary>
        /// 示波器是否已连接
        /// </summary>
        public bool IsOscilloscopeConnected
        {
            get => _isOscilloscopeConnected;
            set
            {
                if (SetProperty(ref _isOscilloscopeConnected, value))
                {
                    RaisePropertyChanged(nameof(OscilloscopeConnectButtonText));
                }
            }
        }

        /// <summary>
        /// 连接/断开 按钮文本
        /// </summary>
        public string OscilloscopeConnectButtonText => IsOscilloscopeConnected ? "断开示波器" : "连接示波器";


        private bool _isAnalogOutputConnected;
        /// <summary>
        /// 模拟量输出板卡是否已连接
        /// </summary>
        public bool IsAnalogOutputConnected
        {
            get => _isAnalogOutputConnected;
            private set => SetProperty(ref _isAnalogOutputConnected, value);
        }

        private string _connectionStatus;
        /// <summary>
        /// 连接状态
        /// </summary>
        public string ConnectionStatus
        {
            get => _connectionStatus;
            set => SetProperty(ref _connectionStatus, value);
        }

        private string _variableName;
        /// <summary>
        /// 变量名称（用于查找矩阵开关配置）
        /// </summary>
        public string VariableName
        {
            get => _variableName;
            set => SetProperty(ref _variableName, value);
        }

        private ObservableCollection<MatrixSwitchConfigItem> _availableMatrixSwitches;
        /// <summary>
        /// 可用的矩阵开关配置列表
        /// </summary>
        public ObservableCollection<MatrixSwitchConfigItem> AvailableMatrixSwitches
        {
            get => _availableMatrixSwitches;
            set => SetProperty(ref _availableMatrixSwitches, value);
        }

        private bool _isSwitchConnected;
        /// <summary>
        /// 矩阵开关是否已连接（指通道连接）
        /// </summary>
        public bool IsSwitchConnected
        {
            get => _isSwitchConnected;
            set => SetProperty(ref _isSwitchConnected, value);
        }

        private string _switchStatus;
        /// <summary>
        /// 矩阵开关状态（连接/断开）
        /// </summary>
        public string SwitchStatus
        {
            get => _switchStatus;
            set => SetProperty(ref _switchStatus, value);
        }

        // 是否有匹配的开关配置（用于命令 CanExecute）
        private bool _hasMatchedSwitch;
        public bool HasMatchedSwitch
        {
            get => _hasMatchedSwitch;
            private set => SetProperty(ref _hasMatchedSwitch, value);
        }

        // 当前匹配的开关信息（只读，用于UI显示）
        private string _currentSwitchName;
        public string CurrentSwitchName
        {
            get => _currentSwitchName;
            private set => SetProperty(ref _currentSwitchName, value);
        }

        private string _currentSwitchInput;
        public string CurrentSwitchInput
        {
            get => _currentSwitchInput;
            private set => SetProperty(ref _currentSwitchInput, value);
        }

        private string _currentSwitchOutput;
        public string CurrentSwitchOutput
        {
            get => _currentSwitchOutput;
            private set => SetProperty(ref _currentSwitchOutput, value);
        }

        private string _testTaskName;
        /// <summary>
        /// 测试任务名称
        /// </summary>
        public string TestTaskName
        {
            get => _testTaskName;
            set => SetProperty(ref _testTaskName, value);
        }

        private string _configTableName;
        /// <summary>
        /// 配置表名称
        /// </summary>
        public string ConfigTableName
        {
            get => _configTableName;
            set => SetProperty(ref _configTableName, value);
        }

        private string _chassisName;
        /// <summary>
        /// 机箱名称
        /// </summary>
        public string ChassisName
        {
            get => _chassisName;
            set => SetProperty(ref _chassisName, value);
        }

        // 模拟量输出通道选择（前两个通道）
        private ObservableCollection<string> _analogOutputChannels;
        /// <summary>
        /// 模拟量输出通道列表（AO0, AO1）
        /// </summary>
        public ObservableCollection<string> AnalogOutputChannels
        {
            get => _analogOutputChannels;
            set => SetProperty(ref _analogOutputChannels, value);
        }

        private string _selectedAnalogOutputChannel;
        /// <summary>
        /// 选中的模拟量输出通道
        /// </summary>
        public string SelectedAnalogOutputChannel
        {
            get => _selectedAnalogOutputChannel;
            set => SetProperty(ref _selectedAnalogOutputChannel, value);
        }

        // 波形类型相关属性
        private ObservableCollection<string> _waveformTypes;
        /// <summary>
        /// 波形类型列表
        /// </summary>
        public ObservableCollection<string> WaveformTypes
        {
            get => _waveformTypes;
            set => SetProperty(ref _waveformTypes, value);
        }

        private string _selectedWaveformType;
        /// <summary>
        /// 选中的波形类型
        /// </summary>
        public string SelectedWaveformType
        {
            get => _selectedWaveformType;
            set
            {
                if (SetProperty(ref _selectedWaveformType, value))
                {
                    UpdateOscilloscopeMeasurementVisibility();
                    UpdatePreviewWaveformPoints();
                }
            }
        }

        // 频率相关属性
        private ObservableCollection<string> _frequencies;
        /// <summary>
        /// 频率列表
        /// </summary>
        public ObservableCollection<string> Frequencies
        {
            get => _frequencies;
            set => SetProperty(ref _frequencies, value);
        }

        private string _selectedFrequency;
        /// <summary>
        /// 选中的频率
        /// </summary>
        public string SelectedFrequency
        {
            get => _selectedFrequency;
            set
            {
                if (SetProperty(ref _selectedFrequency, value))
                {
                    UpdatePreviewWaveformPoints();
                }
            }
        }

        // 幅度相关属性
        private ObservableCollection<string> _amplitudes;
        /// <summary>
        /// 幅度列表
        /// </summary>
        public ObservableCollection<string> Amplitudes
        {
            get => _amplitudes;
            set => SetProperty(ref _amplitudes, value);
        }

        private string _selectedAmplitude;
        /// <summary>
        /// 选中的幅度
        /// </summary>
        public string SelectedAmplitude
        {
            get => _selectedAmplitude;
            set
            {
                if (SetProperty(ref _selectedAmplitude, value))
                {
                    UpdatePreviewWaveformPoints();
                }
            }
        }

        // 偏置相关属性
        private string _offset;
        /// <summary>
        /// 偏置值（可输入）
        /// </summary>
        public string Offset
        {
            get => _offset;
            set
            {
                if (SetProperty(ref _offset, value))
                {
                    UpdatePreviewWaveformPoints();
                }
            }
        }

        // 占空比相关属性（方波时使用）
        private string _dutyCycle;
        /// <summary>
        /// 占空比（方波时使用）
        /// </summary>
        public string DutyCycle
        {
            get => _dutyCycle;
            set
            {
                if (SetProperty(ref _dutyCycle, value))
                {
                    UpdatePreviewWaveformPoints();
                }
            }
        }

        // 采样率
        private double _sampleRate;
        /// <summary>
        /// 采样率
        /// </summary>
        public double SampleRate
        {
            get => _sampleRate;
            set => SetProperty(ref _sampleRate, value);
        }

        private bool _outputEnabled;
        /// <summary>
        /// 输出使能
        /// </summary>
        public bool OutputEnabled
        {
            get => _outputEnabled;
            set => SetProperty(ref _outputEnabled, value);
        }

        private string _outputStatus;
        /// <summary>
        /// 输出状态
        /// </summary>
        public string OutputStatus
        {
            get => _outputStatus;
            set => SetProperty(ref _outputStatus, value);
        }

        private string _oscVpp;
        public string OscVpp
        {
            get => _oscVpp;
            set => SetProperty(ref _oscVpp, value);
        }

        private bool _ch1DisplayEnabled = false;
        public bool Ch1DisplayEnabled
        {
            get => _ch1DisplayEnabled;
            set
            {
                if (SetProperty(ref _ch1DisplayEnabled, value) && IsOscilloscopeConnected)
                {
                    _ = ToggleOscilloscopeChannelDisplayAsync(1);
                }
            }
        }

        private bool _ch2DisplayEnabled = false;
        public bool Ch2DisplayEnabled
        {
            get => _ch2DisplayEnabled;
            set
            {
                if (SetProperty(ref _ch2DisplayEnabled, value) && IsOscilloscopeConnected)
                {
                    _ = ToggleOscilloscopeChannelDisplayAsync(2);
                }
            }
        }

        private bool _ch3DisplayEnabled = false;
        public bool Ch3DisplayEnabled
        {
            get => _ch3DisplayEnabled;
            set
            {
                if (SetProperty(ref _ch3DisplayEnabled, value) && IsOscilloscopeConnected)
                {
                    _ = ToggleOscilloscopeChannelDisplayAsync(3);
                }
            }
        }

        private bool _ch4DisplayEnabled = false;
        public bool Ch4DisplayEnabled
        {
            get => _ch4DisplayEnabled;
            set
            {
                if (SetProperty(ref _ch4DisplayEnabled, value) && IsOscilloscopeConnected)
                {
                    _ = ToggleOscilloscopeChannelDisplayAsync(4);
                }
            }
        }

        private string _ch1Vpp;
        public string Ch1Vpp
        {
            get => _ch1Vpp;
            set => SetProperty(ref _ch1Vpp, value);
        }

        private string _ch1Frequency;
        public string Ch1Frequency
        {
            get => _ch1Frequency;
            set => SetProperty(ref _ch1Frequency, value);
        }

        private string _ch1Vrms;
        public string Ch1Vrms
        {
            get => _ch1Vrms;
            set => SetProperty(ref _ch1Vrms, value);
        }

        private string _ch2Vpp;
        public string Ch2Vpp
        {
            get => _ch2Vpp;
            set => SetProperty(ref _ch2Vpp, value);
        }

        private string _ch2Frequency;
        public string Ch2Frequency
        {
            get => _ch2Frequency;
            set => SetProperty(ref _ch2Frequency, value);
        }

        private string _ch2Vrms;
        public string Ch2Vrms
        {
            get => _ch2Vrms;
            set => SetProperty(ref _ch2Vrms, value);
        }

        private string _ch3Vpp;
        public string Ch3Vpp
        {
            get => _ch3Vpp;
            set => SetProperty(ref _ch3Vpp, value);
        }

        private string _ch3Frequency;
        public string Ch3Frequency
        {
            get => _ch3Frequency;
            set => SetProperty(ref _ch3Frequency, value);
        }

        private string _ch3Vrms;
        public string Ch3Vrms
        {
            get => _ch3Vrms;
            set => SetProperty(ref _ch3Vrms, value);
        }

        private string _ch4Vpp;
        public string Ch4Vpp
        {
            get => _ch4Vpp;
            set => SetProperty(ref _ch4Vpp, value);
        }

        private string _ch4Frequency;
        public string Ch4Frequency
        {
            get => _ch4Frequency;
            set => SetProperty(ref _ch4Frequency, value);
        }

        private string _ch4Vrms;
        public string Ch4Vrms
        {
            get => _ch4Vrms;
            set => SetProperty(ref _ch4Vrms, value);
        }

        private string _oscFrequency;
        public string OscFrequency
        {
            get => _oscFrequency;
            set => SetProperty(ref _oscFrequency, value);
        }

        private string _oscPeriod;
        public string OscPeriod
        {
            get => _oscPeriod;
            set => SetProperty(ref _oscPeriod, value);
        }

        private string _oscVmax;
        public string OscVmax
        {
            get => _oscVmax;
            set => SetProperty(ref _oscVmax, value);
        }

        private string _oscVmin;
        public string OscVmin
        {
            get => _oscVmin;
            set => SetProperty(ref _oscVmin, value);
        }

        private string _oscVavg;
        public string OscVavg
        {
            get => _oscVavg;
            set => SetProperty(ref _oscVavg, value);
        }

        private string _oscVrms;
        public string OscVrms
        {
            get => _oscVrms;
            set => SetProperty(ref _oscVrms, value);
        }

        private string _oscPwidth;
        public string OscPwidth
        {
            get => _oscPwidth;
            set => SetProperty(ref _oscPwidth, value);
        }

        private string _oscNwidth;
        public string OscNwidth
        {
            get => _oscNwidth;
            set => SetProperty(ref _oscNwidth, value);
        }

        private bool _showOscVpp;
        public bool ShowOscVpp
        {
            get => _showOscVpp;
            set => SetProperty(ref _showOscVpp, value);
        }

        private bool _showOscFrequency;
        public bool ShowOscFrequency
        {
            get => _showOscFrequency;
            set => SetProperty(ref _showOscFrequency, value);
        }

        private bool _showOscPeriod;
        public bool ShowOscPeriod
        {
            get => _showOscPeriod;
            set => SetProperty(ref _showOscPeriod, value);
        }

        private bool _showOscVmax;
        public bool ShowOscVmax
        {
            get => _showOscVmax;
            set => SetProperty(ref _showOscVmax, value);
        }

        private bool _showOscVmin;
        public bool ShowOscVmin
        {
            get => _showOscVmin;
            set => SetProperty(ref _showOscVmin, value);
        }

        private bool _showOscVavg;
        public bool ShowOscVavg
        {
            get => _showOscVavg;
            set => SetProperty(ref _showOscVavg, value);
        }

        private bool _showOscVrms;
        public bool ShowOscVrms
        {
            get => _showOscVrms;
            set => SetProperty(ref _showOscVrms, value);
        }

        private bool _showOscPwidth;
        public bool ShowOscPwidth
        {
            get => _showOscPwidth;
            set => SetProperty(ref _showOscPwidth, value);
        }

        private bool _showOscNwidth;
        public bool ShowOscNwidth
        {
            get => _showOscNwidth;
            set => SetProperty(ref _showOscNwidth, value);
        }

        #endregion

        #region Commands

        public ICommand ConnectOscilloscopeCommand { get; private set; }
        public ICommand DisconnectOscilloscopeCommand { get; private set; }
        public ICommand ToggleDeviceCommand { get; private set; }
        public ICommand ToggleSwitchConnectionCommand { get; private set; }
        public ICommand SetWaveformCommand { get; private set; }
        public ICommand ToggleOutputCommand { get; private set; }
        public ICommand SearchVariableCommand { get; private set; }
        public ICommand RunCommand { get; private set; }
        public ICommand StopCommand { get; private set; }
        public ICommand AutoCommand { get; private set; }
        public ICommand TakeScreenshotCommand { get; private set; }
        public ICommand PreviewScreenshotCommand { get; private set; }
        public ICommand BrowseScreenshotSavePathCommand { get; private set; }
        public ICommand SaveScreenshotAsCommand { get; private set; }
        public ICommand SendScpiCommand { get; private set; }
        public ICommand RefreshMeasurementsCommand { get; private set; }
        public ICommand ToggleChannelDisplayCommand { get; private set; }

        #endregion

        #region Constructor

        public OscilloscopeTestPanelViewModel(IPxiChassisService pxiChassisService)
        {
            _pxiChassisService = pxiChassisService;
            try
            {
                _resourceManager = new NationalInstruments.Visa.ResourceManager();
            }
            catch
            {
                _resourceManager = null;
            }
            AvailableMatrixSwitches = new ObservableCollection<MatrixSwitchConfigItem>();
            ConnectionStatus = "离线";
            SwitchStatus = "未选择";

            OscilloscopeIpAddress = "192.168.1.18";
            ScreenshotSavePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ScreenShot.png");
            ScpiCommandText = "*IDN?";

            // 初始化模拟量输出通道列表（前两个通道）
            AnalogOutputChannels = new ObservableCollection<string> { "AO0", "AO1" };
            SelectedAnalogOutputChannel = AnalogOutputChannels[0]; // 默认选择AO0

            // 初始化波形类型列表
            WaveformTypes = new ObservableCollection<string> { "直流", "正弦", "方波" };
            SelectedWaveformType = WaveformTypes[0]; // 默认选择直流

            // 初始化频率列表（Hz）
            Frequencies = new ObservableCollection<string>
            {
                "1", "10", "100", "1000", "5000", "10000", "50000", "100000"
            };
            SelectedFrequency = Frequencies[3]; // 默认选择1000Hz

            // 初始化幅度列表（V）
            Amplitudes = new ObservableCollection<string>
            {
                "0.5", "1.0", "2.0", "3.0", "4.0", "5.0"
            };
            SelectedAmplitude = Amplitudes[2]; // 默认选择2.0V

            // 初始化其他参数
            Offset = "0";
            DutyCycle = "50";
            SampleRate = 100000; // 默认100kHz

            OutputEnabled = false;
            OutputStatus = "输出关闭";

            InitializeCommands();
        }

        public OscilloscopeTestPanelViewModel(string testTaskName, string configTableName, string chassisName,
            IPxiChassisService pxiChassisService) : this(pxiChassisService)
        {
            TestTaskName = testTaskName;
            ConfigTableName = configTableName;
            ChassisName = chassisName;

           
            LoadAnalogOutputDevice();
        }

        private void InitializeCommands()
        {
            ConnectOscilloscopeCommand = new DelegateCommand(async () => await ConnectOscilloscopeAsync(),
                () => !IsOscilloscopeConnected && !string.IsNullOrEmpty(OscilloscopeIpAddress))
                .ObservesProperty(() => IsOscilloscopeConnected)
                .ObservesProperty(() => OscilloscopeIpAddress);

            DisconnectOscilloscopeCommand = new DelegateCommand(async () => await DisconnectOscilloscopeAsync(),
                () => IsOscilloscopeConnected)
                .ObservesProperty(() => IsOscilloscopeConnected);

            ToggleDeviceCommand = new DelegateCommand(async () => await ToggleDeviceAsync(),
                () => true)
                .ObservesProperty(() => IsOscilloscopeConnected);

            

            SetWaveformCommand = new DelegateCommand(async () => await SetWaveformAsync(),
                () => IsAnalogOutputConnected && !string.IsNullOrEmpty(SelectedAnalogOutputChannel))
                .ObservesProperty(() => IsAnalogOutputConnected)
                .ObservesProperty(() => SelectedAnalogOutputChannel);

            ToggleOutputCommand = new DelegateCommand(async () => await ToggleOutputAsync(),
                () => IsAnalogOutputConnected && !string.IsNullOrEmpty(SelectedAnalogOutputChannel))
                .ObservesProperty(() => IsAnalogOutputConnected)
                .ObservesProperty(() => SelectedAnalogOutputChannel);

            SearchVariableCommand = new DelegateCommand(SearchVariable);

            RunCommand = new DelegateCommand(async () => await SendOscilloscopeCommandAsync(":RUN"), () => IsOscilloscopeConnected)
                .ObservesProperty(() => IsOscilloscopeConnected);
            StopCommand = new DelegateCommand(async () => await SendOscilloscopeCommandAsync(":STOP"), () => IsOscilloscopeConnected)
                .ObservesProperty(() => IsOscilloscopeConnected);
            AutoCommand = new DelegateCommand(async () => await AutoScaleAsync(), () => IsOscilloscopeConnected)
                .ObservesProperty(() => IsOscilloscopeConnected);
            TakeScreenshotCommand = new DelegateCommand(async () => await TakeScreenshotAsync(), () => IsOscilloscopeConnected)
                .ObservesProperty(() => IsOscilloscopeConnected);

            PreviewScreenshotCommand = new DelegateCommand(async () => await PreviewScreenshotAsync(), () => IsOscilloscopeConnected)
                .ObservesProperty(() => IsOscilloscopeConnected);
            BrowseScreenshotSavePathCommand = new DelegateCommand(BrowseScreenshotSavePath);
            SaveScreenshotAsCommand = new DelegateCommand(SaveScreenshotAs);
            SendScpiCommand = new DelegateCommand(async () => await SendScpiAsync(), () => IsOscilloscopeConnected)
                .ObservesProperty(() => IsOscilloscopeConnected);
            RefreshMeasurementsCommand = new DelegateCommand(async () => await RefreshMeasurementsOnceAsync(), () => IsOscilloscopeConnected)
                .ObservesProperty(() => IsOscilloscopeConnected);

            ToggleChannelDisplayCommand = new DelegateCommand<string>(async (p) =>
            {
                if (!int.TryParse(p, out var ch))
                    return;
                await ToggleOscilloscopeChannelDisplayAsync(ch);
            }, (p) => IsOscilloscopeConnected).ObservesProperty(() => IsOscilloscopeConnected);
        }

        private async Task AutoScaleAsync()
        {
            if (!IsOscilloscopeConnected)
                return;

            try
            {
                StopOscilloscopeUiAutoRefresh();
                await SendOscilloscopeCommandAsync(":AUToscale");
                try
                {
                    var opc = await QueryOscilloscopeAsync("*OPC?", CancellationToken.None, 20000);
                    _ = opc;
                }
                catch
                {
                }

                await Task.Delay(5000);

                try
                {
                    await PreviewScreenshotAsync(CancellationToken.None, true);
                    await RefreshOscilloscopeChannelMeasurementsOnceNoDelayAsync(CancellationToken.None);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[OscilloscopeTestPanel] AUTO后刷新失败: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[OscilloscopeTestPanel] AUTO失败: {ex.Message}");
            }
            finally
            {
                StartOscilloscopeUiAutoRefresh();
            }
        }

        private void UpdatePreviewWaveformPoints()
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                dispatcher.BeginInvoke(new Action(UpdatePreviewWaveformPoints));
                return;
            }

            try
            {
                var points = BuildPreviewWaveformPoints();
                WaveformPoints = points;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[OscilloscopeTestPanel] 预览波形生成失败: {ex.Message}");
            }
        }

        private PointCollection BuildPreviewWaveformPoints()
        {
            const double canvasW = 1000.0;
            const double canvasH = 400.0;
            const int sampleCount = 400;

            double amp = 0;
            double freq = 0;
            double offset = 0;
            double duty = 50;

            double.TryParse(SelectedAmplitude, NumberStyles.Float, CultureInfo.InvariantCulture, out amp);
            double.TryParse(SelectedFrequency, NumberStyles.Float, CultureInfo.InvariantCulture, out freq);
            double.TryParse(Offset, NumberStyles.Float, CultureInfo.InvariantCulture, out offset);
            double.TryParse(DutyCycle, NumberStyles.Float, CultureInfo.InvariantCulture, out duty);

            if (duty <= 0) duty = 1;
            if (duty >= 100) duty = 99;

            string type = SelectedWaveformType ?? "直流";

            double cycles;
            {
                double ratio = canvasH <= 0 ? 2.5 : (canvasW / canvasH);
                cycles = Math.Round(ratio, 0);
                if (cycles < 2) cycles = 2;
                if (cycles > 6) cycles = 6;
            }

            double timeWindow;
            if (freq > 0)
            {
                timeWindow = cycles / freq;
            }
            else
            {
                timeWindow = 1.0;
            }

            double vMax = Math.Max(10, Math.Abs(offset) + Math.Abs(amp));
            double vMin = -vMax;
            double vSpan = vMax - vMin;
            if (vSpan <= 0) vSpan = 1;

            var points = new PointCollection(sampleCount + 1);
            for (int i = 0; i <= sampleCount; i++)
            {
                double t = (double)i / sampleCount * timeWindow;
                double x = (double)i / sampleCount * canvasW;

                double v;
                if (string.Equals(type, "正弦", StringComparison.OrdinalIgnoreCase))
                {
                    v = offset + amp * Math.Sin(2 * Math.PI * freq * t);
                }
                else if (string.Equals(type, "方波", StringComparison.OrdinalIgnoreCase))
                {
                    if (freq <= 0)
                    {
                        v = offset;
                    }
                    else
                    {
                        double phase = (t * freq) % 1.0;
                        v = offset + (phase < (duty / 100.0) ? amp : -amp);
                    }
                }
                else
                {
                    v = offset;
                }

                double yNorm = (v - vMin) / vSpan;
                double y = (1.0 - yNorm) * canvasH;
                points.Add(new Point(x, y));
            }

            points.Freeze();
            return points;
        }

        private async Task SendScpiAsync()
        {
            var cmd = ScpiCommandText;
            if (string.IsNullOrWhiteSpace(cmd))
                return;

            try
            {
                if (cmd.TrimEnd().EndsWith("?", StringComparison.Ordinal))
                {
                    var resp = await QueryOscilloscopeAsync(cmd.Trim(), CancellationToken.None);
                    ScpiResponseText = resp;
                }
                else
                {
                    await SendOscilloscopeCommandAsync(cmd.Trim());
                    ScpiResponseText = "OK";
                }
            }
            catch (Exception ex)
            {
                ScpiResponseText = ex.Message;
            }
        }

        private async Task RefreshMeasurementsOnceAsync()
        {
            if (!IsOscilloscopeConnected)
                return;

            try
            {
                await RefreshOscilloscopeChannelMeasurementsOnceNoDelayAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[OscilloscopeTestPanel] 手动刷新测量失败: {ex.Message}");
            }
        }

        #endregion

        #region Methods


        /// <summary>
        /// 加载模拟量输出设备
        /// </summary>
        private void LoadAnalogOutputDevice()
        {
            try
            {
                var devices = _pxiChassisService?.GetChassisDevices(ChassisName);
                if (devices == null)
                {
                    System.Diagnostics.Debug.WriteLine($"[OscilloscopeTestPanel] 无法获取机箱设备: {ChassisName}");
                    return;
                }

                // 查找模拟量输出设备
                _analogOutputDevice = devices.OfType<AnalogOutputDevice>().FirstOrDefault();
                if (_analogOutputDevice != null)
                {
                    System.Diagnostics.Debug.WriteLine($"[OscilloscopeTestPanel] 找到模拟量输出设备: {_analogOutputDevice.CardName}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[OscilloscopeTestPanel] 未找到模拟量输出设备");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[OscilloscopeTestPanel] 加载模拟量输出设备失败: {ex.Message}");
            }
        }

       

        /// <summary>
        /// 获取板卡对应的拓扑配置
        /// </summary>
        private string GetTopologyForBoard(SwitchDevice switchDevice)
        {
            if (switchDevice == null)
                return null;

            try
            {
                string key = $"{TestTaskName}/{ConfigTableName}";
                var allConfigs = MatrixSwitchConfigTableViewModel.GetAllMatrixSwitchTableItems();

                if (!allConfigs.ContainsKey(key))
                    return null;

                var configs = allConfigs[key];

                var configForBoard = configs.FirstOrDefault(c =>
                    c != null && !c.IsEmpty &&
                    !string.IsNullOrEmpty(c.MatrixSwitchName) &&
                    !string.IsNullOrEmpty(c.Topology) &&
                    (c.MatrixSwitchName.Contains(switchDevice.CardName ?? "") ||
                     c.MatrixSwitchName.Contains(switchDevice.Model ?? "") ||
                     c.MatrixSwitchName.Contains(switchDevice.Name ?? "")));

                if (configForBoard != null)
                {
                    return GetTopologyString(configForBoard.Topology);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[OscilloscopeTestPanel] 获取板卡拓扑失败: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// 连接模拟量输出板卡
        /// </summary>
        private async Task ConnectAnalogOutputBoardAsync(AnalogOutputDevice aoDevice)
        {
            if (aoDevice == null)
                return;

            try
            {
                // 创建驱动
                _analogOutputDriver = DriverFactory.CreateDriver(aoDevice);
                if (_analogOutputDriver == null)
                {
                    System.Diagnostics.Debug.WriteLine($"[OscilloscopeTestPanel] 无法创建模拟量输出驱动");
                    return;
                }

                // 连接设备
                bool connected = await _analogOutputDriver.ConnectAsync();
                if (connected)
                {
                    _analogOutputDevice = aoDevice;
                    System.Diagnostics.Debug.WriteLine($"[OscilloscopeTestPanel] 模拟量输出板卡连接成功: {aoDevice.CardName}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[OscilloscopeTestPanel] 模拟量输出板卡连接失败: {aoDevice.CardName}");
                    _analogOutputDriver = null;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[OscilloscopeTestPanel] 连接模拟量输出板卡异常: {ex.Message}");
                _analogOutputDriver = null;
                throw;
            }
        }

        /// <summary>
        /// 连接示波器设备
        /// </summary>
        private async Task ToggleDeviceAsync()
        {
            if (IsOscilloscopeConnected)
            {
                await DisconnectOscilloscopeAsync();
            }
            else
            {
                await ConnectOscilloscopeAsync();
            }
        }

        private async Task ConnectOscilloscopeAsync()
        {
            if (string.IsNullOrEmpty(OscilloscopeIpAddress))
            {
                ReMessageBox.Show("请输入示波器设备IP地址", "提示",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            const int port = 5555;

            try
            {
                ConnectionStatus = "检测中";
                IsOscilloscopeConnected = false;

                // 第一步：连接示波器
                string oscilloscopeInfo = null;
                await Task.Run(async () =>
                {
                    try
                    {
                        _oscilloscopeTcpClient = new TcpClient();
                        var connectTask = _oscilloscopeTcpClient.ConnectAsync(OscilloscopeIpAddress.Trim(), port);
                        var timeoutTask = Task.Delay(5000);
                        var completed = await Task.WhenAny(connectTask, timeoutTask);
                        if (completed != connectTask)
                            throw new TimeoutException("连接超时");

                        _oscilloscopeTcpStream = _oscilloscopeTcpClient.GetStream();
                        _oscilloscopeTcpStream.ReadTimeout = 5000;
                        _oscilloscopeTcpStream.WriteTimeout = 5000;

                        var cmd = Encoding.ASCII.GetBytes("*IDN?\n");
                        _oscilloscopeTcpStream.Write(cmd, 0, cmd.Length);
                        oscilloscopeInfo = await ReadLineAsync(_oscilloscopeTcpStream, 5000, CancellationToken.None);
                    }
                    catch
                    {
                        SafeCloseNetworkStream(ref _oscilloscopeTcpStream);
                        SafeCloseTcpClient(ref _oscilloscopeTcpClient);

                        if (_resourceManager == null)
                            throw;

                        // 构建VISA资源字符串
                        string resourceString = $"TCPIP0::{OscilloscopeIpAddress}::{port}::SOCKET";

                        // 打开VISA会话（连接示波器）
                        _oscilloscopeSession = (NationalInstruments.Visa.MessageBasedSession)_resourceManager.Open(resourceString, 0, 5000);

                        try
                        {
                            _oscilloscopeSession.TerminationCharacterEnabled = true;
                            _oscilloscopeSession.TerminationCharacter = 0x0A;
                        }
                        catch
                        {
                        }

                        _oscilloscopeSession.RawIO.Write("*IDN?");
                        oscilloscopeInfo = _oscilloscopeSession.RawIO.ReadString().Trim();
                    }

                    System.Diagnostics.Debug.WriteLine($"[OscilloscopeTestPanel] 示波器设备信息: {oscilloscopeInfo}");

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        ConnectionStatus = "在线";
                    });
                });
                // 第三步：更新最终状态
                Application.Current.Dispatcher.Invoke(() =>
                {
                    IsOscilloscopeConnected = true;
                    IsAnalogOutputConnected = _analogOutputDriver != null && _analogOutputDriver.IsConnected;

                    string statusText = $"已连接: {oscilloscopeInfo}";

                    if (IsAnalogOutputConnected)
                    {
                        statusText += "，模拟量输出板卡已连接";
                    }

                    ConnectionStatus = "在线";
                });

                await InitializeOscilloscopeMeasurementSessionAsync();
                await RefreshOscilloscopeChannelMeasurementsOnceNoDelayAsync(CancellationToken.None);

                StartOscilloscopeUiAutoRefresh();

                if (OutputEnabled)
                {
                    StartOscilloscopeMeasurementMonitoring();
                }
                else
                {
                    await StopOscilloscopeMeasurementMonitoringAsync();
                    ClearOscilloscopeMeasurements();
                }
            }
            catch (Exception ex)
            {
                ConnectionStatus = "离线";
                IsOscilloscopeConnected = false;
                ReMessageBox.Show($"连接失败: {ex.Message}", "连接错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                System.Diagnostics.Debug.WriteLine($"[OscilloscopeTestPanel] 连接失败: {ex.Message}");

                // 清理已连接的资源
                try
                {
                    if (_oscilloscopeSession != null)
                    {
                        _oscilloscopeSession.Dispose();
                        _oscilloscopeSession = null;
                    }
                    SafeCloseNetworkStream(ref _oscilloscopeTcpStream);
                    SafeCloseTcpClient(ref _oscilloscopeTcpClient);
                }
                catch { }
            }
        }

        /// <summary>
        /// 断开示波器设备连接
        /// </summary>
        private async Task DisconnectOscilloscopeAsync()
        {
            try
            {
                StopOscilloscopeUiAutoRefresh();
                ConnectionStatus = "断开中";
                await StopOscilloscopeMeasurementMonitoringAsync();

                await Task.Run(async () =>
                {
                    // 1. 断开所有矩阵开关板卡和模拟量输出板卡
                    System.Diagnostics.Debug.WriteLine("[OscilloscopeTestPanel] 开始断开板卡...");
                    

                    // 2. 断开示波器
                    if (_oscilloscopeSession != null)
                    {
                        try { _oscilloscopeSession.Dispose(); } catch { }
                        _oscilloscopeSession = null;
                    }

                    SafeCloseNetworkStream(ref _oscilloscopeTcpStream);
                    SafeCloseTcpClient(ref _oscilloscopeTcpClient);
                });

                IsOscilloscopeConnected = false;
                IsAnalogOutputConnected = false;
                OutputEnabled = false;
                OutputStatus = "输出关闭";
                ConnectionStatus = "离线";
                ClearOscilloscopeMeasurements();

                // 清空当前开关连接状态
                if (IsSwitchConnected)
                {
                    IsSwitchConnected = false;
                    SwitchStatus = "离线";
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[OscilloscopeTestPanel] 断开连接失败: {ex.Message}");
                ConnectionStatus = "离线";

                Application.Current.Dispatcher.Invoke(() =>
                {
                    IsOscilloscopeConnected = false;
                    IsAnalogOutputConnected = false;
                    ConnectionStatus = "离线";
                });
            }
        }

        /// <summary>
        /// 搜索变量（示波器或模拟量输出）
        /// </summary>
        private void SearchVariable()
        {
            if (string.IsNullOrEmpty(VariableName))
            {
                ReMessageBox.Show("请输入变量名称", "提示",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // 在矩阵开关配置中查找匹配的变量（通过 InstrumentType 字段匹配）
            var matchedSwitches = AvailableMatrixSwitches
                .Where(s => !string.IsNullOrEmpty(s.InstrumentType) &&
                           s.InstrumentType.IndexOf(VariableName, StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();

            if (matchedSwitches.Count == 0)
            {
                ReMessageBox.Show($"未找到包含变量名称 '{VariableName}' 的矩阵开关配置（示波器或模拟量输出）", "未找到",
                    MessageBoxButton.OK, MessageBoxImage.Information);

                return;
            }

            if (matchedSwitches.Count > 1)
            {
                System.Diagnostics.Debug.WriteLine($"[OscilloscopeTestPanel] 找到 {matchedSwitches.Count} 个匹配项");
            }

            
            HasMatchedSwitch = true;


        }

        /// <summary>
        /// 更新当前开关信息到UI
        /// </summary>
        private void UpdateCurrentSwitchInfo(MatrixSwitchConfigItem switchConfig)
        {
            CurrentSwitchName = switchConfig.MatrixSwitchName;
            CurrentSwitchInput = switchConfig.MatrixInput;
            CurrentSwitchOutput = switchConfig.MatrixOutput;
        }

        /// <summary>
        /// 设置波形参数并配置模拟量输出通道
        /// </summary>
        private async Task SetWaveformAsync()
        {
            if (_analogOutputDriver == null || !_analogOutputDriver.IsConnected)
            {
                ReMessageBox.Show("模拟量输出板卡未连接", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            try
            {
                await Task.Run(async () =>
                {
                    string channel = SelectedAnalogOutputChannel; // "AO0" 或 "AO1"

                    // 解析波形类型
                    MTX532Driver.WaveformType waveformType;
                    if (SelectedWaveformType == "直流")
                    {
                        waveformType = MTX532Driver.WaveformType.Dc;
                    }
                    else if (SelectedWaveformType == "正弦")
                    {
                        waveformType = MTX532Driver.WaveformType.Sine;
                    }
                    else // 方波
                    {
                        waveformType = MTX532Driver.WaveformType.Square;
                    }

                    // 解析参数
                    double amplitude = 0;
                    double frequency = 0;
                    double offset = 0;
                    double dutyCycle = 50;

                    if (waveformType != MTX532Driver.WaveformType.Dc)
                    {
                        if (!double.TryParse(SelectedAmplitude, out amplitude))
                        {
                            amplitude = 0;
                        }
                        if (!double.TryParse(SelectedFrequency, out frequency))
                        {
                            frequency = 0;
                        }
                    }

                    if (!double.TryParse(Offset, out offset))
                    {
                        offset = 0;
                    }

                    if (waveformType == MTX532Driver.WaveformType.Square)
                    {
                        if (!double.TryParse(DutyCycle, out dutyCycle))
                        {
                            dutyCycle = 50;
                        }
                    }

                    // 校验电压范围：|Offset| + |Amplitude| <= 10V
                    if (Math.Abs(offset) + Math.Abs(amplitude) > 10.0)
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            ReMessageBox.Show("偏置和幅度的绝对值之和不能超过10V", "参数错误",
                                MessageBoxButton.OK, MessageBoxImage.Warning);
                        });
                        return;
                    }

                    // 配置通道
                    var config = new Dictionary<string, object>
                    {
                        ["Enabled"] = true,
                        ["SampleRate"] = SampleRate,
                        ["Waveform"] = waveformType,
                        ["Amplitude"] = amplitude,
                        ["Offset"] = offset,
                        ["Frequency"] = frequency,
                        ["DutyCycle"] = dutyCycle
                    };

                    bool success = await _analogOutputDriver.ConfigureChannelAsync(channel, config);

                    if (success)
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            OutputStatus = "波形设置成功";
                        });
                        System.Diagnostics.Debug.WriteLine($"[OscilloscopeTestPanel] 通道 {channel} 波形设置成功: {SelectedWaveformType}, 幅度={amplitude}V, 频率={frequency}Hz, 偏置={offset}V");
                    }
                    else
                    {
                        throw new Exception("通道配置失败");
                    }
                });

                ReMessageBox.Show($"波形设置成功\n通道: {SelectedAnalogOutputChannel}\n波形: {SelectedWaveformType}\n幅度: {SelectedAmplitude}V\n频率: {SelectedFrequency}Hz\n偏置: {Offset}V", "成功",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                ReMessageBox.Show($"设置波形失败: {ex.Message}", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                System.Diagnostics.Debug.WriteLine($"[OscilloscopeTestPanel] 设置波形失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 切换输出使能
        /// </summary>
        private async Task ToggleOutputAsync()
        {
            if (_analogOutputDriver == null || !_analogOutputDriver.IsConnected)
            {
                ReMessageBox.Show("模拟量输出板卡未连接", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            try
            {
                await Task.Run(async () =>
                {
                    if (!OutputEnabled)
                    {
                        // 启动输出
                        bool success = await _analogOutputDriver.StartAcquisitionAsync();
                        if (success)
                        {
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                OutputEnabled = true;
                                OutputStatus = "输出开启";
                            });
                            System.Diagnostics.Debug.WriteLine($"[OscilloscopeTestPanel] 输出开启: {SelectedAnalogOutputChannel}");
                        }
                        else
                        {
                            throw new Exception("启动输出失败");
                        }
                    }
                    else
                    {
                        // 停止输出
                        bool success = await _analogOutputDriver.StopAcquisitionAsync();
                        if (success)
                        {
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                OutputEnabled = false;
                                OutputStatus = "输出关闭";
                            });
                            System.Diagnostics.Debug.WriteLine($"[OscilloscopeTestPanel] 输出关闭");
                        }
                        else
                        {
                            throw new Exception("停止输出失败");
                        }
                    }
                });

                if (OutputEnabled)
                {
                    StartOscilloscopeMeasurementMonitoring();
                }
                else
                {
                    await StopOscilloscopeMeasurementMonitoringAsync();
                    ClearOscilloscopeMeasurements();
                }
            }
            catch (Exception ex)
            {
                ReMessageBox.Show($"切换输出状态失败: {ex.Message}", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                System.Diagnostics.Debug.WriteLine($"[OscilloscopeTestPanel] 切换输出状态失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 获取拓扑字符串
        /// </summary>
        private string GetTopologyString(string topology)
        {
            if (string.IsNullOrEmpty(topology))
                return null;

            switch (topology)
            {
                case "4*32Matrix":
                case "4x32 Matrix":
                    return artSwitchTopologies.ARTSWITCH_TOPOLOGY_2601_2_WIRE_4X32_MATRIX;
                case "8*16Matrix":
                case "8x16 Matrix":
                    return artSwitchTopologies.ARTSWITCH_TOPOLOGY_2601_2_WIRE_8X16_MATRIX;
                case "4*64Matrix":
                case "4x64 Matrix":
                    return artSwitchTopologies.ARTSWITCH_TOPOLOGY_2601_2_WIRE_4X32_MATRIX;
                default:
                    return null;
            }
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            lock (this)
            {
                if (_disposed)
                    return;
                try
                {
                    StopOscilloscopeUiAutoRefresh();
                    StopOscilloscopeMeasurementMonitoringAsync().Wait(TimeSpan.FromSeconds(1));
                    if (_analogOutputDriver != null && _analogOutputDriver.IsConnected)
                    {
                        try { _analogOutputDriver.StopAcquisitionAsync().Wait(TimeSpan.FromSeconds(1)); } catch { }
                        try { _analogOutputDriver.DisconnectAsync().Wait(TimeSpan.FromSeconds(2)); } catch { }
                    }

                    if (_oscilloscopeSession != null)
                    {
                        try { _oscilloscopeSession.Dispose(); } catch { }
                        _oscilloscopeSession = null;
                    }

                    SafeCloseNetworkStream(ref _oscilloscopeTcpStream);
                    SafeCloseTcpClient(ref _oscilloscopeTcpClient);

                    if (_resourceManager != null)
                    {
                        try { _resourceManager.Dispose(); } catch { }
                        _resourceManager = null;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[OscilloscopeTestPanel] Dispose失败: {ex.Message}");
                }

                _disposed = true;
            }
        }

        #endregion

        private async Task InitializeOscilloscopeMeasurementSessionAsync()
        {
            if (_oscilloscopeSession == null && _oscilloscopeTcpStream == null)
                return;

            try
            {
                UpdateOscilloscopeMeasurementVisibility();
                _oscilloscopeMeasureItemsDirty = true;

                await _oscilloscopeIoLock.WaitAsync(CancellationToken.None);
                try
                {
                    await Task.Run(() =>
                    {
                        WriteOscilloscopeUnsafe(":MEASure:SOURce CHANnel1");
                        WriteOscilloscopeUnsafe(":MEASure:CLEar");
                    });
                }
                finally
                {
                    _oscilloscopeIoLock.Release();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[OscilloscopeTestPanel] 初始化示波器测量失败: {ex.Message}");
            }
        }

        private string GetOscilloscopeMeasureCategory()
        {
            if (string.Equals(SelectedWaveformType, "方波", StringComparison.OrdinalIgnoreCase))
                return "SQUARE";
            if (string.Equals(SelectedWaveformType, "直流", StringComparison.OrdinalIgnoreCase))
                return "DC";
            return "SINE";
        }

        private void UpdateOscilloscopeMeasurementVisibility()
        {
            string category = GetOscilloscopeMeasureCategory();
            bool changed = !string.Equals(_oscilloscopeMeasureCategory, category, StringComparison.OrdinalIgnoreCase);
            _oscilloscopeMeasureCategory = category;

            ShowOscVpp = category != "DC";
            ShowOscFrequency = category != "DC";
            ShowOscPeriod = category != "DC";
            ShowOscVmax = true;
            ShowOscVmin = true;
            ShowOscVavg = true;
            ShowOscVrms = true;
            ShowOscPwidth = category == "SQUARE";
            ShowOscNwidth = category == "SQUARE";

            if (changed)
            {
                if (category == "SINE")
                {
                    System.Diagnostics.Debug.WriteLine("[OscilloscopeTestPanel] 当前波形=正弦，忽略测量项: PWIDth, NWIDth");
                }
                else if (category == "DC")
                {
                    System.Diagnostics.Debug.WriteLine("[OscilloscopeTestPanel] 当前波形=直流，忽略测量项: VPP, FREQuency, PERiod, PWIDth, NWIDth");
                }
                _oscilloscopeMeasureItemsDirty = true;
            }
        }

        private void StartOscilloscopeMeasurementMonitoring()
        {
            if (_oscilloscopeSession == null)
                return;
            if (_oscilloscopeMeasureTask != null && !_oscilloscopeMeasureTask.IsCompleted)
                return;

            _oscilloscopeMeasureCts?.Cancel();
            _oscilloscopeMeasureCts?.Dispose();
            _oscilloscopeMeasureCts = new CancellationTokenSource();
            var token = _oscilloscopeMeasureCts.Token;

            _oscilloscopeMeasureTask = Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        if (!IsOscilloscopeConnected || _oscilloscopeSession == null)
                        {
                            await Task.Delay(200, token);
                            continue;
                        }

                        await RefreshOscilloscopeMeasurementsOnceAsync(token);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[OscilloscopeTestPanel] 示波器测量轮询失败: {ex.Message}");
                        await Task.Delay(500, token);
                    }
                }
            }, token);
        }

        private async Task StopOscilloscopeMeasurementMonitoringAsync()
        {
            try { _oscilloscopeMeasureCts?.Cancel(); } catch { }
            try
            {
                if (_oscilloscopeMeasureTask != null)
                    await _oscilloscopeMeasureTask;
            }
            catch { }
            finally
            {
                _oscilloscopeMeasureTask = null;
                _oscilloscopeMeasureCts?.Dispose();
                _oscilloscopeMeasureCts = null;
            }
        }

        private async Task ConfigureOscilloscopeMeasurementItemsIfNeededAsync(CancellationToken token)
        {
            if (!_oscilloscopeMeasureItemsDirty)
                return;
            if (_oscilloscopeSession == null && _oscilloscopeTcpStream == null)
                return;

            await _oscilloscopeIoLock.WaitAsync(token);
            try
            {
                await Task.Run(() =>
                {
                    WriteOscilloscopeUnsafe(":MEASure:CLEar");
                    if (ShowOscVpp) WriteOscilloscopeUnsafe(":MEASure:ITEM VPP");
                    if (ShowOscFrequency) WriteOscilloscopeUnsafe(":MEASure:ITEM FREQuency");
                    if (ShowOscPeriod) WriteOscilloscopeUnsafe(":MEASure:ITEM PERiod");
                    WriteOscilloscopeUnsafe(":MEASure:ITEM VMAX");
                    WriteOscilloscopeUnsafe(":MEASure:ITEM VMIN");
                    WriteOscilloscopeUnsafe(":MEASure:ITEM VAVG");
                    WriteOscilloscopeUnsafe(":MEASure:ITEM VRMS");
                    if (ShowOscPwidth) WriteOscilloscopeUnsafe(":MEASure:ITEM PWIDth");
                    if (ShowOscNwidth) WriteOscilloscopeUnsafe(":MEASure:ITEM NWIDth");
                });
                _oscilloscopeMeasureItemsDirty = false;
            }
            finally
            {
                _oscilloscopeIoLock.Release();
            }
        }

        private async Task ToggleOscilloscopeChannelDisplayAsync(int channel)
        {
            if (!IsOscilloscopeConnected)
                return;
            if (channel < 1 || channel > 4)
                return;

            bool enabled;
            switch (channel)
            {
                case 1: enabled = Ch1DisplayEnabled; break;
                case 2: enabled = Ch2DisplayEnabled; break;
                case 3: enabled = Ch3DisplayEnabled; break;
                case 4: enabled = Ch4DisplayEnabled; break;
                default: return;
            }

            try
            {
                await SendOscilloscopeCommandAsync($":CHANnel{channel}:DISPlay {(enabled ? "ON" : "OFF")}");

                if (!enabled)
                {
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        switch (channel)
                        {
                            case 1: Ch1Vpp = ""; Ch1Frequency = ""; Ch1Vrms = ""; break;
                            case 2: Ch2Vpp = ""; Ch2Frequency = ""; Ch2Vrms = ""; break;
                            case 3: Ch3Vpp = ""; Ch3Frequency = ""; Ch3Vrms = ""; break;
                            case 4: Ch4Vpp = ""; Ch4Frequency = ""; Ch4Vrms = ""; break;
                        }
                        if (!Ch1DisplayEnabled && !Ch2DisplayEnabled && !Ch3DisplayEnabled && !Ch4DisplayEnabled)
                        {
                            WaveformPoints = new PointCollection();
                        }
                    });
                    return;
                }

                try
                {
                    await EnsureOscilloscopeChannelProbeOneXAsync(channel);
                    await RefreshOscilloscopeChannelMeasurementsOnceNoDelayAsync(CancellationToken.None);
                    await PreviewScreenshotAsync();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[OscilloscopeTestPanel] 自动刷新失败: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[OscilloscopeTestPanel] 设置通道显示失败 CH{channel}: {ex.Message}");
            }
        }

        private void StartOscilloscopeUiAutoRefresh()
        {
            if (_oscilloscopeUiRefreshTask != null && !_oscilloscopeUiRefreshTask.IsCompleted)
                return;

            _oscilloscopeUiRefreshCts?.Cancel();
            _oscilloscopeUiRefreshCts?.Dispose();
            _oscilloscopeUiRefreshCts = new CancellationTokenSource();
            var token = _oscilloscopeUiRefreshCts.Token;

            _oscilloscopeUiRefreshTask = Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        if (!IsOscilloscopeConnected || (_oscilloscopeSession == null && _oscilloscopeTcpStream == null))
                        {
                            await Task.Delay(500, token);
                            continue;
                        }

                        await PreviewScreenshotAsync(token, true);

                        bool any = Ch1DisplayEnabled || Ch2DisplayEnabled || Ch3DisplayEnabled || Ch4DisplayEnabled;
                        if (any)
                        {
                            if (Ch1DisplayEnabled) await EnsureOscilloscopeChannelProbeOneXAsync(1);
                            if (Ch2DisplayEnabled) await EnsureOscilloscopeChannelProbeOneXAsync(2);
                            if (Ch3DisplayEnabled) await EnsureOscilloscopeChannelProbeOneXAsync(3);
                            if (Ch4DisplayEnabled) await EnsureOscilloscopeChannelProbeOneXAsync(4);

                            await RefreshOscilloscopeChannelMeasurementsOnceNoDelayAsync(token);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[OscilloscopeTestPanel] UI自动刷新失败: {ex.Message}");
                    }

                    await Task.Delay(2000, token);
                }
            }, token);
        }

        private void StopOscilloscopeUiAutoRefresh()
        {
            try { _oscilloscopeUiRefreshCts?.Cancel(); } catch { }
            try { _oscilloscopeUiRefreshCts?.Dispose(); } catch { }
            _oscilloscopeUiRefreshCts = null;
            _oscilloscopeUiRefreshTask = null;
        }

        private async Task EnsureOscilloscopeChannelProbeOneXAsync(int channel)
        {
            if (_oscilloscopeSession == null && _oscilloscopeTcpStream == null)
                return;
            if (channel < 1 || channel > 4)
                return;

            try
            {
                await SendOscilloscopeCommandAsync($":CHANnel{channel}:PROBe 1");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[OscilloscopeTestPanel] 设置探头倍率失败 CH{channel}: {ex.Message}");
            }
        }

        private async Task ConfigureOscilloscopeChannelMeasurementItemsIfNeededAsync(int channel, CancellationToken token)
        {
            await _oscilloscopeIoLock.WaitAsync(token);
            try
            {
                await Task.Run(() =>
                {
                    WriteOscilloscopeUnsafe($":MEASure:SOURce CHANnel{channel}");
                    WriteOscilloscopeUnsafe(":MEASure:CLEar");
                    WriteOscilloscopeUnsafe(":MEASure:ITEM VPP");
                    WriteOscilloscopeUnsafe(":MEASure:ITEM FREQuency");
                    WriteOscilloscopeUnsafe(":MEASure:ITEM VRMS");
                });
            }
            finally
            {
                _oscilloscopeIoLock.Release();
            }
        }

        private async Task RefreshOscilloscopeChannelMeasurementsOnceNoDelayAsync(CancellationToken token)
        {
            if (_oscilloscopeSession == null && _oscilloscopeTcpStream == null)
                return;

            async Task RefreshOneAsync(int ch)
            {
                await SendOscilloscopeCommandAsync($":MEASure:SOURce CHANnel{ch}");
                string vpp = NormalizeOscilloscopeNumber(await QueryOscilloscopeAsync(":MEASure:ITEM? VPP", token));
                string vp = ConvertOscilloscopeVppToVp(vpp);
                string freq = NormalizeOscilloscopeNumber(await QueryOscilloscopeAsync(":MEASure:ITEM? FREQuency", token));
                string vrms = NormalizeOscilloscopeNumber(await QueryOscilloscopeAsync(":MEASure:ITEM? VRMS", token));

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    switch (ch)
                    {
                        case 1: Ch1Vpp = vp; Ch1Frequency = freq; Ch1Vrms = vrms; break;
                        case 2: Ch2Vpp = vp; Ch2Frequency = freq; Ch2Vrms = vrms; break;
                        case 3: Ch3Vpp = vp; Ch3Frequency = freq; Ch3Vrms = vrms; break;
                        case 4: Ch4Vpp = vp; Ch4Frequency = freq; Ch4Vrms = vrms; break;
                    }
                });
            }

            if (Ch1DisplayEnabled) await RefreshOneAsync(1);
            if (Ch2DisplayEnabled) await RefreshOneAsync(2);
            if (Ch3DisplayEnabled) await RefreshOneAsync(3);
            if (Ch4DisplayEnabled) await RefreshOneAsync(4);
        }

        private async Task RefreshOscilloscopeMeasurementsOnceNoDelayAsync(CancellationToken token)
        {
            await ConfigureOscilloscopeMeasurementItemsIfNeededAsync(token);

            string vpp = ShowOscVpp ? NormalizeOscilloscopeNumber(await QueryOscilloscopeAsync(":MEASure:ITEM? VPP", token)) : null;
            string freq = ShowOscFrequency ? NormalizeOscilloscopeNumber(await QueryOscilloscopeAsync(":MEASure:ITEM? FREQuency", token)) : null;
            string per = ShowOscPeriod ? NormalizeOscilloscopeNumber(await QueryOscilloscopeAsync(":MEASure:ITEM? PERiod", token)) : null;
            string vmax = NormalizeOscilloscopeNumber(await QueryOscilloscopeAsync(":MEASure:ITEM? VMAX", token));
            string vmin = NormalizeOscilloscopeNumber(await QueryOscilloscopeAsync(":MEASure:ITEM? VMIN", token));
            string vavg = NormalizeOscilloscopeNumber(await QueryOscilloscopeAsync(":MEASure:ITEM? VAVG", token));
            string vrms = NormalizeOscilloscopeNumber(await QueryOscilloscopeAsync(":MEASure:ITEM? VRMS", token));
            string pw = ShowOscPwidth ? NormalizeOscilloscopeNumber(await QueryOscilloscopeAsync(":MEASure:ITEM? PWIDth", token)) : null;
            string nw = ShowOscNwidth ? NormalizeOscilloscopeNumber(await QueryOscilloscopeAsync(":MEASure:ITEM? NWIDth", token)) : null;

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (ShowOscVpp) OscVpp = vpp;
                if (ShowOscFrequency) OscFrequency = freq;
                if (ShowOscPeriod) OscPeriod = per;
                OscVmax = vmax;
                OscVmin = vmin;
                OscVavg = vavg;
                OscVrms = vrms;
                if (ShowOscPwidth) OscPwidth = pw;
                if (ShowOscNwidth) OscNwidth = nw;
            });
        }

        private int GetFirstEnabledChannelOrDefault()
        {
            if (Ch1DisplayEnabled) return 1;
            if (Ch2DisplayEnabled) return 2;
            if (Ch3DisplayEnabled) return 3;
            if (Ch4DisplayEnabled) return 4;
            return 1;
        }

        private async Task RefreshWaveformPointsAsync(CancellationToken token)
        {
            if (_oscilloscopeSession == null && _oscilloscopeTcpStream == null)
                return;

            int channel = GetFirstEnabledChannelOrDefault();

            await _oscilloscopeIoLock.WaitAsync(token);
            try
            {
                var result = await Task.Run(() =>
                {
                    WriteOscilloscopeUnsafe($":WAVeform:SOURce CHANnel{channel}");
                    WriteOscilloscopeUnsafe(":WAVeform:MODE NORM");
                    WriteOscilloscopeUnsafe(":WAVeform:FORMat BYTE");

                    string preamble;
                    if (_oscilloscopeSession != null)
                    {
                        _oscilloscopeSession.RawIO.Write(":WAVeform:PREamble?");
                        preamble = _oscilloscopeSession.RawIO.ReadString().Trim();
                    }
                    else
                    {
                        var stream = _oscilloscopeTcpStream;
                        if (stream == null)
                            return (PointCollection)null;
                        var bytes = Encoding.ASCII.GetBytes(":WAVeform:PREamble?\n");
                        stream.Write(bytes, 0, bytes.Length);
                        preamble = ReadLineAsync(stream, 5000, token).GetAwaiter().GetResult();
                    }

                    var items = (preamble ?? string.Empty)
                        .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(s => double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : double.NaN)
                        .ToArray();
                    if (items.Length < 10)
                        return (PointCollection)null;

                    double xInc = items[4];
                    double xOrig = items[5];
                    double xRef = items[6];
                    double yInc = items[7];
                    double yOrig = items[8];
                    double yRef = items[9];

                    byte[] data;
                    if (_oscilloscopeSession != null)
                    {
                        _oscilloscopeSession.RawIO.Write(":WAVeform:DATA?");
                        data = ReadIeee4882DefiniteLengthBlock(_oscilloscopeSession, 50_000_000);
                    }
                    else
                    {
                        var stream = _oscilloscopeTcpStream;
                        if (stream == null)
                            return (PointCollection)null;
                        var cmd = Encoding.ASCII.GetBytes(":WAVeform:DATA?\n");
                        stream.Write(cmd, 0, cmd.Length);
                        data = ReadIeee4882DefiniteLengthBlock(stream, 50_000_000);
                    }

                    if (data == null || data.Length == 0)
                        return (PointCollection)null;

                    const double canvasW = 1000.0;
                    const double canvasH = 400.0;
                    int step = Math.Max(1, data.Length / 1000);
                    int count = (data.Length + step - 1) / step;

                    var volts = new double[count];
                    int idx = 0;
                    for (int i = 0; i < data.Length; i += step)
                    {
                        double raw = data[i];
                        volts[idx++] = (raw - yRef) * yInc + yOrig;
                    }

                    double vMin = volts.Min();
                    double vMax = volts.Max();
                    double vSpan = vMax - vMin;
                    if (Math.Abs(vSpan) < 1e-15)
                        vSpan = 1;

                    var points = new PointCollection(count);
                    for (int i = 0; i < count; i++)
                    {
                        double x = count <= 1 ? 0 : (i * (canvasW / (count - 1)));
                        double v = volts[i];
                        double yNorm = (v - vMin) / vSpan;
                        double y = (1.0 - yNorm) * canvasH;
                        points.Add(new Point(x, y));
                    }

                    points.Freeze();

                    _ = xInc + xOrig + xRef;
                    return points;
                }, token);

                if (result != null)
                {
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        WaveformPoints = result;
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[OscilloscopeTestPanel] 读取波形点失败: {ex.Message}");
            }
            finally
            {
                _oscilloscopeIoLock.Release();
            }
        }

        private async Task<string> QueryOscilloscopeAsync(string command, CancellationToken token)
        {
            return await QueryOscilloscopeAsync(command, token, 5000);
        }

        private async Task<string> QueryOscilloscopeAsync(string command, CancellationToken token, int timeoutMs)
        {
            if (_oscilloscopeSession == null && _oscilloscopeTcpStream == null)
                return null;

            await _oscilloscopeIoLock.WaitAsync(token);
            try
            {
                return await Task.Run(() =>
                {
                    if (_oscilloscopeSession != null)
                    {
                        _oscilloscopeSession.RawIO.Write(command);
                        return _oscilloscopeSession.RawIO.ReadString().Trim();
                    }

                    var stream = _oscilloscopeTcpStream;
                    if (stream == null)
                        return null;
                    var bytes = Encoding.ASCII.GetBytes(command.EndsWith("\n") ? command : command + "\n");
                    stream.Write(bytes, 0, bytes.Length);
                    try
                    {
                        return ReadLineAsync(stream, timeoutMs, token).GetAwaiter().GetResult();
                    }
                    catch (TimeoutException)
                    {
                        return null;
                    }
                }, token);
            }
            finally
            {
                _oscilloscopeIoLock.Release();
            }
        }

        private static void SafeCloseNetworkStream(ref NetworkStream stream)
        {
            try { stream?.Close(); } catch { }
            try { stream?.Dispose(); } catch { }
            stream = null;
        }

        private static void SafeCloseTcpClient(ref TcpClient client)
        {
            try { client?.Close(); } catch { }
            try { client?.Dispose(); } catch { }
            client = null;
        }

        private static string NormalizeOscilloscopeNumber(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return "--";

            raw = raw.Trim();
            int comma = raw.IndexOf(',');
            if (comma > 0)
                raw = raw.Substring(0, comma);

            var m = Regex.Match(raw, @"[-+]?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?");
            if (!m.Success)
                return "--";

            if (!double.TryParse(m.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                return "--";
            if (double.IsNaN(d) || double.IsInfinity(d) || Math.Abs(d) > 1e36)
                return "--";

            return d.ToString("G6", CultureInfo.InvariantCulture);
        }

        private static string ConvertOscilloscopeVppToVp(string vpp)
        {
            if (string.IsNullOrWhiteSpace(vpp) || vpp == "--")
                return "--";

            if (!double.TryParse(vpp, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                return "--";
            if (double.IsNaN(d) || double.IsInfinity(d) || Math.Abs(d) > 1e36)
                return "--";

            return (d / 2.0).ToString("G6", CultureInfo.InvariantCulture);
        }

        private async Task RefreshOscilloscopeMeasurementsOnceAsync(CancellationToken token)
        {
            await ConfigureOscilloscopeMeasurementItemsIfNeededAsync(token);

            string vpp = ShowOscVpp ? NormalizeOscilloscopeNumber(await QueryOscilloscopeAsync(":MEASure:ITEM? VPP", token)) : null;
            string freq = ShowOscFrequency ? NormalizeOscilloscopeNumber(await QueryOscilloscopeAsync(":MEASure:ITEM? FREQuency", token)) : null;
            string per = ShowOscPeriod ? NormalizeOscilloscopeNumber(await QueryOscilloscopeAsync(":MEASure:ITEM? PERiod", token)) : null;
            string vmax = NormalizeOscilloscopeNumber(await QueryOscilloscopeAsync(":MEASure:ITEM? VMAX", token));
            string vmin = NormalizeOscilloscopeNumber(await QueryOscilloscopeAsync(":MEASure:ITEM? VMIN", token));
            string vavg = NormalizeOscilloscopeNumber(await QueryOscilloscopeAsync(":MEASure:ITEM? VAVG", token));
            string vrms = NormalizeOscilloscopeNumber(await QueryOscilloscopeAsync(":MEASure:ITEM? VRMS", token));
            string pw = ShowOscPwidth ? NormalizeOscilloscopeNumber(await QueryOscilloscopeAsync(":MEASure:ITEM? PWIDth", token)) : null;
            string nw = ShowOscNwidth ? NormalizeOscilloscopeNumber(await QueryOscilloscopeAsync(":MEASure:ITEM? NWIDth", token)) : null;

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (ShowOscVpp) OscVpp = vpp;
                if (ShowOscFrequency) OscFrequency = freq;
                if (ShowOscPeriod) OscPeriod = per;
                OscVmax = vmax;
                OscVmin = vmin;
                OscVavg = vavg;
                OscVrms = vrms;
                if (ShowOscPwidth) OscPwidth = pw;
                if (ShowOscNwidth) OscNwidth = nw;
            });

            await Task.Delay(500, token);
        }

        private void ClearOscilloscopeMeasurements()
        {
            OscVpp = "";
            OscFrequency = "";
            OscPeriod = "";
            OscVmax = "";
            OscVmin = "";
            OscVavg = "";
            OscVrms = "";
            OscPwidth = "";
            OscNwidth = "";
        }
    }
}
