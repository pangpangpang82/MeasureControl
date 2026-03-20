using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MeasureControl.ViewModels;
using MeasureControl.Views.Dialogs;
using Prism.DryIoc;

namespace MeasureControl.Views
{
    public partial class Login : Window
    {
        // ViewModel
        private readonly LoginViewModel vm;
        private void OpenDropdown() => vm.IsDropdownOpen = true;
        private void CloseDropdown() => vm.IsDropdownOpen = false;

        public Login(LoginViewModel vm)
        {
            InitializeComponent();
            this.vm = vm;
            this.DataContext = vm;

            this.PreviewMouseDown += Window_PreviewMouseDown;
            this.Deactivated += Window_Deactivated;
            vm.RequestClose += () => this.Close();

            vm.ShowMessageRequested += message =>
            {
                ReMessageBox.Show(message);
            };

            // 后台裁剪 待优化
            UidList.SizeChanged += (s, e) =>
            {
                var border = UidList.Parent as Border;
                if (border != null)
                {
                    border.Clip = new RectangleGeometry
                    {
                        RadiusX = 13,
                        RadiusY = 13,
                        Rect = new Rect(0, 0, border.ActualWidth, border.ActualHeight)
                    };
                }
            };
        }

        // 关闭下拉框
        private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            var element = e.OriginalSource as DependencyObject;

            while (element != null)
            {
                if (element == DropdownPopup || element == User || element == ShowDown || element == UidList)
                    return;
                element = VisualTreeHelper.GetParent(element);
            }

            if (vm != null)
            {
                CloseDropdown();
            }
        }

        // 清除焦点
        private void ClosePopupAndClearFocus(object sender, MouseButtonEventArgs e)
        {
            ((UIElement)sender).Focus();
        }

        // 窗口拖动
        private void DragWindow(object sender, MouseButtonEventArgs e)
        {
            this.DragMove();
        }

        // 工号选择行为
        private void UidList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {

            if (UidList.SelectedItem != null)
            {
                vm.UserId = UidList.SelectedItem.ToString();
                CloseDropdown();
                UidList.SelectedItem = null;
            }
        }

        // 焦点提示工号 刚输入的情况
        private void User_GotFocus(object sender, RoutedEventArgs e)
        {
            var textBox = sender as TextBox;
            if (string.IsNullOrWhiteSpace(textBox.Text))
            {
                DropdownPopup.IsOpen = true;
            }
        }

        // 输入为空提示工号 删除后为空的情况
        private void User_TextChanged(object sender, TextChangedEventArgs e)
        {
            var textBox = sender as TextBox;
            if (string.IsNullOrWhiteSpace(textBox.Text))
            {
                OpenDropdown();
            }
            else
            {
                CloseDropdown();
            }
        }

        // 关闭下拉框
        private void Window_Deactivated(object sender, EventArgs e)
        {
            CloseDropdown();
        }
    }
}