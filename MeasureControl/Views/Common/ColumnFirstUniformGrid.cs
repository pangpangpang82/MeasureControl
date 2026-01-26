using System;
using System.Windows;
using System.Windows.Controls;

namespace MeasureControl.Views.Common
{
    public class ColumnFirstUniformGrid : Panel
    {
        public static readonly DependencyProperty RowsProperty =
            DependencyProperty.Register(
                nameof(Rows),
                typeof(int),
                typeof(ColumnFirstUniformGrid),
                new FrameworkPropertyMetadata(1, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange),
                ValidatePositive);

        public static readonly DependencyProperty ColumnsProperty =
            DependencyProperty.Register(
                nameof(Columns),
                typeof(int),
                typeof(ColumnFirstUniformGrid),
                new FrameworkPropertyMetadata(1, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange),
                ValidatePositive);

        public int Rows
        {
            get => (int)GetValue(RowsProperty);
            set => SetValue(RowsProperty, value);
        }

        public int Columns
        {
            get => (int)GetValue(ColumnsProperty);
            set => SetValue(ColumnsProperty, value);
        }

        private static bool ValidatePositive(object value)
        {
            return value is int i && i > 0;
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            // Compute per-cell size hint
            double cellWidth = double.IsInfinity(availableSize.Width) ? double.PositiveInfinity : availableSize.Width / Columns;
            double cellHeight = double.IsInfinity(availableSize.Height) ? double.PositiveInfinity : availableSize.Height / Rows;
            var cellSize = new Size(cellWidth, cellHeight);

            foreach (UIElement child in InternalChildren)
            {
                if (child == null) continue;
                child.Measure(cellSize);
            }

            // Desired as the available size (uniform grid fills its space)
            return availableSize;
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            double cellWidth = finalSize.Width / Columns;
            double cellHeight = finalSize.Height / Rows;

            int childCount = InternalChildren.Count;
            for (int i = 0; i < childCount; ++i)
            {
                UIElement child = InternalChildren[i];
                if (child == null) continue;

                // Column-first order: fill rows top-to-bottom, then move to next column
                int column = i / Rows;
                int row = i % Rows;

                if (column >= Columns)
                {
                    // Extra children beyond grid capacity; don't arrange them (collapse)
                    child.Arrange(new Rect(0, 0, 0, 0));
                    continue;
                }

                double x = column * cellWidth;
                double y = row * cellHeight;
                var rect = new Rect(new Point(x, y), new Size(cellWidth, cellHeight));
                child.Arrange(rect);
            }

            return finalSize;
        }
    }
}

