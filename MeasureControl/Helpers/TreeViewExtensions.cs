using System;
using System.Windows.Controls;
using System.Windows.Threading;

namespace MeasureControl.Helpers
{
    public static class TreeViewExtensions
    {
        public static void ExpandAll(this TreeView treeView)
        {
            if (treeView == null)
            {
                return;
            }

            treeView.Dispatcher.BeginInvoke(new Action(() =>
            {
                foreach (var item in treeView.Items)
                {
                    if (treeView.ItemContainerGenerator.ContainerFromItem(item) is TreeViewItem treeViewItem)
                    {
                        ExpandAll(treeViewItem);
                    }
                }
            }), DispatcherPriority.Loaded);
        }

        private static void ExpandAll(TreeViewItem treeViewItem)
        {
            if (treeViewItem == null)
            {
                return;
            }

            treeViewItem.IsExpanded = true;
            treeViewItem.UpdateLayout();

            foreach (var child in treeViewItem.Items)
            {
                if (treeViewItem.ItemContainerGenerator.ContainerFromItem(child) is TreeViewItem childItem)
                {
                    ExpandAll(childItem);
                }
            }
        }
    }
}

