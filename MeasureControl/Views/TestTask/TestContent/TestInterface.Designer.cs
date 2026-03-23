// 设计器占位：XAML 未生成 .g.cs 时保证可编译
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MeasureControl.Views.TestContent
{
    public partial class TestInterface
    {
        private bool _contentLoaded;

        [System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [System.CodeDom.Compiler.GeneratedCodeAttribute("PresentationBuildTasks", "4.0.0.0")]
        public void InitializeComponent()
        {
            if (_contentLoaded) return;
            _contentLoaded = true;
            System.Windows.Application.LoadComponent(this, new Uri("/MeasureControl;component/views/testtask/testcontent/testinterface.xaml", UriKind.Relative));
        }

        internal Border ControlConfigPanel;
        internal ItemsControl ConfigItemsControl;
        internal Canvas DesignCanvas;
        internal Border DragPreview;
    }
}
