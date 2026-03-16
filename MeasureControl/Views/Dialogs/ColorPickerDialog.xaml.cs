using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MeasureControl.Views.Dialogs
{
    /// <summary>
    /// 颜色选择器对话框
    /// </summary>
    public partial class ColorPickerDialog : Window
    {
        private bool _isUpdating = false;
        private bool _isDragging = false;
        private WriteableBitmap _colorBoardBitmap;

        /// <summary>
        /// 选中的颜色（十六进制格式）
        /// </summary>
        public string SelectedColor { get; private set; }

        public ColorPickerDialog(string initialColor = "#e8ebed")
        {
            InitializeComponent();
            SelectedColor = initialColor;
            
            // 窗口加载完成后初始化色板
            Loaded += (s, e) =>
            {
                GenerateColorBoard();
                SetColorFromHex(initialColor);
            };

            // 窗口大小变化时重新生成色板
            ColorCanvas.SizeChanged += (s, e) =>
            {
                if (ColorCanvas.ActualWidth > 0 && ColorCanvas.ActualHeight > 0)
                {
                    GenerateColorBoard();
                }
            };
        }

        /// <summary>
        /// 生成HSV色板
        /// 横轴：色相 Hue (0-360)
        /// 纵轴：亮度 Value (1-0，上亮下暗)
        /// 饱和度固定为1
        /// </summary>
        private void GenerateColorBoard()
        {
            int width = (int)ColorCanvas.ActualWidth;
            int height = (int)ColorCanvas.ActualHeight;
            
            if (width <= 0 || height <= 0) return;

            _colorBoardBitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
            byte[] pixels = new byte[width * height * 4];

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    // 横轴：色相 0-360
                    double hue = (double)x / width * 360;
                    // 纵轴：亮度 1(上) -> 0(下)
                    double value = 1.0 - (double)y / height;
                    // 饱和度固定为1
                    double saturation = 1.0;

                    var color = HsvToRgb(hue, saturation, value);
                    int idx = (y * width + x) * 4;
                    pixels[idx] = color.B;         // Blue
                    pixels[idx + 1] = color.G;     // Green
                    pixels[idx + 2] = color.R;     // Red
                    pixels[idx + 3] = 255;         // Alpha
                }
            }

            _colorBoardBitmap.WritePixels(new Int32Rect(0, 0, width, height), pixels, width * 4, 0);
            ColorBoardImage.Source = _colorBoardBitmap;
            ColorBoardImage.Width = width;
            ColorBoardImage.Height = height;
        }

        /// <summary>
        /// HSV转RGB
        /// </summary>
        private Color HsvToRgb(double h, double s, double v)
        {
            double c = v * s;
            double x = c * (1 - Math.Abs((h / 60) % 2 - 1));
            double m = v - c;

            double r, g, b;
            if (h < 60) { r = c; g = x; b = 0; }
            else if (h < 120) { r = x; g = c; b = 0; }
            else if (h < 180) { r = 0; g = c; b = x; }
            else if (h < 240) { r = 0; g = x; b = c; }
            else if (h < 300) { r = x; g = 0; b = c; }
            else { r = c; g = 0; b = x; }

            return Color.FromRgb(
                (byte)((r + m) * 255),
                (byte)((g + m) * 255),
                (byte)((b + m) * 255)
            );
        }

        /// <summary>
        /// RGB转HSV
        /// </summary>
        private (double H, double S, double V) RgbToHsv(Color color)
        {
            double r = color.R / 255.0;
            double g = color.G / 255.0;
            double b = color.B / 255.0;

            double max = Math.Max(r, Math.Max(g, b));
            double min = Math.Min(r, Math.Min(g, b));
            double delta = max - min;

            double h = 0;
            if (delta != 0)
            {
                if (max == r)
                    h = 60 * (((g - b) / delta) % 6);
                else if (max == g)
                    h = 60 * ((b - r) / delta + 2);
                else
                    h = 60 * ((r - g) / delta + 4);
            }
            if (h < 0) h += 360;

            double s = max == 0 ? 0 : delta / max;
            double v = max;

            return (h, s, v);
        }

        /// <summary>
        /// 色板鼠标按下
        /// </summary>
        private void ColorCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _isDragging = true;
            ColorCanvas.CaptureMouse();
            UpdateColorFromPosition(e.GetPosition(ColorCanvas));
        }

        /// <summary>
        /// 色板鼠标释放
        /// </summary>
        private void ColorCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _isDragging = false;
            ColorCanvas.ReleaseMouseCapture();
        }

        /// <summary>
        /// 色板鼠标移动
        /// </summary>
        private void ColorCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isDragging)
            {
                UpdateColorFromPosition(e.GetPosition(ColorCanvas));
            }
        }

        /// <summary>
        /// 根据鼠标位置更新颜色
        /// </summary>
        private void UpdateColorFromPosition(Point position)
        {
            double width = ColorCanvas.ActualWidth;
            double height = ColorCanvas.ActualHeight;
            
            if (width <= 0 || height <= 0) return;

            // 限制在色板范围内
            double x = Math.Max(0, Math.Min(position.X, width - 1));
            double y = Math.Max(0, Math.Min(position.Y, height - 1));

            // 计算HSV值
            double hue = x / width * 360;
            double value = 1.0 - y / height;
            double saturation = 1.0;

            var color = HsvToRgb(hue, saturation, value);

            // 更新选择器位置
            Canvas.SetLeft(ColorSelector, x - ColorSelector.Width / 2);
            Canvas.SetTop(ColorSelector, y - ColorSelector.Height / 2);

            // 更新颜色值
            _isUpdating = true;
            RInput.Text = color.R.ToString();
            GInput.Text = color.G.ToString();
            BInput.Text = color.B.ToString();
            HexInput.Text = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
            UpdatePreview(color);
            SelectedColor = HexInput.Text;
            _isUpdating = false;
        }

        /// <summary>
        /// 更新选择器位置（根据颜色）
        /// </summary>
        private void UpdateSelectorPosition(Color color)
        {
            double width = ColorCanvas.ActualWidth;
            double height = ColorCanvas.ActualHeight;
            
            if (width <= 0 || height <= 0) return;

            var (h, s, v) = RgbToHsv(color);
            
            double x = h / 360 * width;
            double y = (1.0 - v) * height;

            Canvas.SetLeft(ColorSelector, x - ColorSelector.Width / 2);
            Canvas.SetTop(ColorSelector, y - ColorSelector.Height / 2);
        }

        /// <summary>
        /// 标题栏拖动
        /// </summary>
        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        /// <summary>
        /// 关闭按钮
        /// </summary>
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        /// <summary>
        /// 十六进制输入变化
        /// </summary>
        private void HexInput_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdating) return;
            if (HexInput == null || RInput == null || GInput == null || BInput == null) return;

            var hex = HexInput.Text.Trim();
            if (!hex.StartsWith("#"))
            {
                hex = "#" + hex;
            }

            try
            {
                var color = (Color)ColorConverter.ConvertFromString(hex);
                _isUpdating = true;
                RInput.Text = color.R.ToString();
                GInput.Text = color.G.ToString();
                BInput.Text = color.B.ToString();
                UpdatePreview(color);
                UpdateSelectorPosition(color);
                SelectedColor = hex;
                _isUpdating = false;
            }
            catch
            {
                // 无效的颜色格式，忽略
            }
        }

        /// <summary>
        /// RGB输入变化
        /// </summary>
        private void RgbInput_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdating) return;
            if (RInput == null || GInput == null || BInput == null || HexInput == null) return;

            if (byte.TryParse(RInput.Text, out byte r) &&
                byte.TryParse(GInput.Text, out byte g) &&
                byte.TryParse(BInput.Text, out byte b))
            {
                var color = Color.FromRgb(r, g, b);
                _isUpdating = true;
                HexInput.Text = $"#{r:X2}{g:X2}{b:X2}";
                UpdatePreview(color);
                UpdateSelectorPosition(color);
                SelectedColor = HexInput.Text;
                _isUpdating = false;
            }
        }

        /// <summary>
        /// 从十六进制设置颜色
        /// </summary>
        private void SetColorFromHex(string hex)
        {
            _isUpdating = true;
            try
            {
                if (!hex.StartsWith("#"))
                {
                    hex = "#" + hex;
                }

                var color = (Color)ColorConverter.ConvertFromString(hex);
                HexInput.Text = hex;
                RInput.Text = color.R.ToString();
                GInput.Text = color.G.ToString();
                BInput.Text = color.B.ToString();
                UpdatePreview(color);
                UpdateSelectorPosition(color);
                SelectedColor = hex;
            }
            catch
            {
                // 无效的颜色格式，使用默认值
                HexInput.Text = "#e8ebed";
                RInput.Text = "232";
                GInput.Text = "235";
                BInput.Text = "237";
                var defaultColor = Color.FromRgb(232, 235, 237);
                UpdatePreview(defaultColor);
                UpdateSelectorPosition(defaultColor);
                SelectedColor = "#e8ebed";
            }
            _isUpdating = false;
        }

        /// <summary>
        /// 更新预览
        /// </summary>
        private void UpdatePreview(Color color)
        {
            ColorPreview.Background = new SolidColorBrush(color);
        }

        /// <summary>
        /// 确定按钮
        /// </summary>
        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        /// <summary>
        /// 取消按钮
        /// </summary>
        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
