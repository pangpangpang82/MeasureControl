using System.Windows;
using System.Windows.Controls;
using MeasureControl.Models;
using MeasureControl.Views;
using MeasureControl.ViewModels.TestTask;
using Prism.Services.Dialogs;
using MeasureControl.Views.Dialogs;

namespace MeasureControl.Services
{
    public class DialogService : IDialogService
    {
        public class CreateSingleBoardTestTaskResult
        {
            public string BoardType { get; set; }
            public string TaskName { get; set; }
        }

        public string ShowRenameDialog(string currentName, string title = "重命名")
        {
            return ShowInputDialogInternal(currentName, title, requireDifferentName: true);
        }

        /// <summary>
        /// 显示输入对话框（用于创建场景，允许使用默认名称）
        /// </summary>
        /// <param name="defaultName">默认名称</param>
        /// <param name="title">对话框标题</param>
        /// <returns>用户输入的名称，取消返回null</returns>
        public string ShowInputDialog(string defaultName, string title = "输入名称")
        {
            return ShowInputDialogInternal(defaultName, title, requireDifferentName: false);
        }

        public CreateSingleBoardTestTaskResult ShowCreateSingleBoardTestTaskDialog(string title = "创建测试任务")
        {
            var dialog = new CreateSingleBoardTestTaskDialog
            {
                Title = title,
                Owner = Application.Current.MainWindow,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };

            var ok = dialog.ShowDialog();
            if (ok != true)
            {
                return null;
            }

            return new CreateSingleBoardTestTaskResult
            {
                BoardType = dialog.SelectedBoardType,
                TaskName = dialog.TaskName
            };
        }

        private string ShowInputDialogInternal(string currentName, string title, bool requireDifferentName)
        {
            var inputDialog = new Window
            {
                Title = title,
                Width = 300,
                Height = 140,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = Application.Current.MainWindow,
                WindowStyle = WindowStyle.None,
                BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(28, 28, 28)),
                BorderThickness = new Thickness(1),
                ResizeMode = ResizeMode.NoResize
            };

            var mainGrid = new Grid();
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(30) }); // 标题栏
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // 内容区
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 按钮区

            // 标题栏
            var titleBorder = new Border
            {
                Height = 30,
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(28, 28, 28))
            };

            var titleGrid = new Grid();
            titleGrid.ColumnDefinitions.Add(new ColumnDefinition());
            titleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });

            var titleText = new TextBlock
            {
                Text = title,
                Margin = new Thickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 14,
                Foreground = System.Windows.Media.Brushes.White
            };

            var closeButton = new Button
            {
                Content = "✖",
                Width = 30,
                Height = 30,
                HorizontalAlignment = HorizontalAlignment.Right,
                FontSize = 14,
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(28, 28, 28)),
                Foreground = System.Windows.Media.Brushes.White,
                BorderThickness = new Thickness(0)
            };

            Grid.SetColumn(titleText, 0);
            Grid.SetColumn(closeButton, 1);
            titleGrid.Children.Add(titleText);
            titleGrid.Children.Add(closeButton);
            titleBorder.Child = titleGrid;

            // 内容区
            var contentGrid = new Grid();
            contentGrid.Margin = new Thickness(15, 10, 15, 5);

            var textBox = new TextBox
            {
                Text = currentName,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 14,
                Padding = new Thickness(8)
            };

            contentGrid.Children.Add(textBox);

            // 按钮区
            var buttonBorder = new Border
            {
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(240, 240, 240))
            };

            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(5)
            };

            var okButton = new Button
            {
                Content = "确定",
                Width = 50,
                Height = 20,
                Margin = new Thickness(0, 5, 5, 5),
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(28, 28, 28)),
                Foreground = System.Windows.Media.Brushes.White,
                BorderThickness = new Thickness(0)
            };

            var cancelButton = new Button
            {
                Content = "取消",
                Width = 50,
                Height = 20,
                Margin = new Thickness(5, 5, 5, 5),
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(28, 28, 28)),
                Foreground = System.Windows.Media.Brushes.White,
                BorderThickness = new Thickness(0)
            };

            buttonPanel.Children.Add(okButton);
            buttonPanel.Children.Add(cancelButton);
            buttonBorder.Child = buttonPanel;

            Grid.SetRow(titleBorder, 0);
            Grid.SetRow(contentGrid, 1);
            Grid.SetRow(buttonBorder, 2);

            mainGrid.Children.Add(titleBorder);
            mainGrid.Children.Add(contentGrid);
            mainGrid.Children.Add(buttonBorder);

            inputDialog.Content = mainGrid;

            string result = null;

            // 事件处理
            titleBorder.MouseLeftButtonDown += (s, e) => inputDialog.DragMove();

            okButton.Click += (s, args) =>
            {
                var newName = textBox.Text.Trim();
                if (!string.IsNullOrEmpty(newName))
                {
                    if (requireDifferentName)
                    {
                        // 重命名模式：名称必须不同
                        if (newName != currentName)
                        {
                            result = newName;
                        }
                    }
                    else
                    {
                        // 创建模式：只要名称不为空即可
                        result = newName;
                    }
                }
                inputDialog.Close();
            };

            cancelButton.Click += (s, args) => inputDialog.Close();
            closeButton.Click += (s, args) => inputDialog.Close();

            // 按钮悬停效果
            okButton.MouseEnter += (s, e) => okButton.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(79, 92, 135));
            okButton.MouseLeave += (s, e) => okButton.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(28, 28, 28));
            
            cancelButton.MouseEnter += (s, e) => cancelButton.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(79, 92, 135));
            cancelButton.MouseLeave += (s, e) => cancelButton.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(28, 28, 28));
            
            closeButton.MouseEnter += (s, e) => closeButton.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(79, 92, 135));
            closeButton.MouseLeave += (s, e) => closeButton.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(28, 28, 28));

            inputDialog.ShowDialog();
            textBox.Focus();
            textBox.SelectAll();

            return result;
        }

        public MessageBoxResult ShowConfirmDialog(string message, string title = "确认")
        {
            return ReMessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question);
        }

        public bool ShowConfirmationDialog(string message, string title = "确认")
        {
            var result = ReMessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question);
            return result == MessageBoxResult.Yes;
        }

        public void ShowErrorDialog(string message, string title = "错误")
        {
            ReMessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
        }

        public void ShowInfoDialog(string message, string title = "信息")
        {
            ReMessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
        }

        public void ShowWarningDialog(string message, string title = "警告")
        {
            ReMessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        public IcdMappingItem ShowAddIcdMappingDialog(System.Collections.ObjectModel.ObservableCollection<string> availableIcdTabels, System.Collections.ObjectModel.ObservableCollection<IcdFrameItem> availableFrames)
        {
            var dialog = new Views.Dialogs.AddIcdMappingDialog();
            var viewModel = new ViewModels.Dialogs.AddIcdMappingDialogViewModel(this);

            // 设置对话框参数
            var parameters = new DialogParameters();
            parameters.Add("AvailableIcdTabels", availableIcdTabels);
            parameters.Add("AvailableFrames", availableFrames);

            viewModel.OnDialogOpened(parameters);
            dialog.DataContext = viewModel;

            // 设置对话框属性
            dialog.Owner = Application.Current.MainWindow;
            dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;

            IcdMappingItem result = null;

            void OnDialogClosed(IDialogResult dialogResult)
            {
                if (dialogResult.Result == ButtonResult.OK)
                {
                    result = dialogResult.Parameters.GetValue<IcdMappingItem>("MappingItem");
                }
                dialog.Close();
            }

            viewModel.RequestClose += OnDialogClosed;

            dialog.ShowDialog();

            return result;
        }

    }
}
