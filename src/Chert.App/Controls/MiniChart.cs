using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Chert.App.Controls;

/// <summary>
/// 轻量实时折线图（bug2.txt #7 性能页）：绑定一列 double 历史值，自动按高度纵向缩放，
/// 可选固定上限 <see cref="Max"/>（百分比场景传 100）。无第三方图表库依赖。
/// </summary>
public class MiniChart : UserControl
{
    public static readonly DependencyProperty ValuesProperty =
        DependencyProperty.Register(nameof(Values), typeof(IEnumerable<double>), typeof(MiniChart),
            new PropertyMetadata(null, OnValuesChanged));

    public static readonly DependencyProperty MaxProperty =
        DependencyProperty.Register(nameof(Max), typeof(double), typeof(MiniChart),
            new PropertyMetadata(0.0, (d, _) => ((MiniChart)d).Render()));

    public static readonly DependencyProperty StrokeProperty =
        DependencyProperty.Register(nameof(Stroke), typeof(Brush), typeof(MiniChart),
            new PropertyMetadata(Brushes.White, OnAppearanceChanged));

    public static readonly DependencyProperty FillProperty =
        DependencyProperty.Register(nameof(Fill), typeof(Brush), typeof(MiniChart),
            new PropertyMetadata(Brushes.White, OnAppearanceChanged));

    private readonly Canvas _canvas = new();
    private readonly Polyline _line = new();
    private readonly Polygon _area = new();

    public MiniChart()
    {
        _line.StrokeThickness = 2;
        _line.StrokeEndLineCap = PenLineCap.Round;
        _line.StrokeStartLineCap = PenLineCap.Round;
        _area.Opacity = 0.16;
        _canvas.Children.Add(_area);
        _canvas.Children.Add(_line);
        Content = _canvas;
        SizeChanged += (_, _) => Render();
        ApplyBrushes();
    }

    public IEnumerable<double>? Values
    {
        get => (IEnumerable<double>?)GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    public double Max
    {
        get => (double)GetValue(MaxProperty);
        set => SetValue(MaxProperty, value);
    }

    public Brush Stroke
    {
        get => (Brush)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public Brush Fill
    {
        get => (Brush)GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    private static void OnValuesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var self = (MiniChart)d;
        if (e.OldValue is INotifyCollectionChanged oldC) oldC.CollectionChanged -= self.OnChanged;
        if (e.NewValue is INotifyCollectionChanged newC) newC.CollectionChanged += self.OnChanged;
        self.Render();
    }

    private static void OnAppearanceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var self = (MiniChart)d;
        self.ApplyBrushes();
        self.Render();
    }

    private void ApplyBrushes()
    {
        _line.Stroke = Stroke;
        _area.Fill = Fill;
    }

    private void OnChanged(object? sender, NotifyCollectionChangedEventArgs e) => Render();

    private void Render()
    {
        var w = ActualWidth;
        var h = ActualHeight;
        if (w < 2 || h < 2) return;

        var vals = Values?.ToList() ?? new List<double>();
        if (vals.Count == 0)
        {
            _line.Points = new PointCollection();
            _area.Points = new PointCollection();
            return;
        }

        var max = Max > 0 ? Max : vals.Max();
        if (max <= 0) max = 1;

        var n = vals.Count;
        var pts = new PointCollection();
        for (var i = 0; i < n; i++)
        {
            var x = n == 1 ? w : w * i / (n - 1);
            var y = h - (vals[i] / max) * h;
            pts.Add(new Point(x, y));
        }
        _line.Points = pts;

        var areaPts = new PointCollection(pts) { new Point(w, h), new Point(0, h) };
        _area.Points = areaPts;
    }
}
