using System.Collections;
using System.Windows;
using System.Windows.Media;

namespace ServerControlCenter.Controls;

public class SparklineChart : FrameworkElement
{
    public static readonly DependencyProperty ValuesProperty =
        DependencyProperty.Register(
            nameof(Values),
            typeof(IEnumerable),
            typeof(SparklineChart),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeProperty =
        DependencyProperty.Register(
            nameof(Stroke),
            typeof(Brush),
            typeof(SparklineChart),
            new FrameworkPropertyMetadata(Brushes.DodgerBlue, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FillProperty =
        DependencyProperty.Register(
            nameof(Fill),
            typeof(Brush),
            typeof(SparklineChart),
            new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ShowGridProperty =
        DependencyProperty.Register(
            nameof(ShowGrid),
            typeof(bool),
            typeof(SparklineChart),
            new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    public IEnumerable? Values
    {
        get => (IEnumerable?)GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
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

    public bool ShowGrid
    {
        get => (bool)GetValue(ShowGridProperty);
        set => SetValue(ShowGridProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        return new Size(
            double.IsInfinity(availableSize.Width) ? 120 : availableSize.Width,
            double.IsInfinity(availableSize.Height) ? 56 : availableSize.Height);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        var width = ActualWidth;
        var height = ActualHeight;

        if (width <= 2 || height <= 2)
        {
            return;
        }

        if (ShowGrid)
        {
            DrawGrid(drawingContext, width, height);
        }

        var values = GetValues();
        if (values.Count == 0)
        {
            return;
        }

        if (values.Count == 1)
        {
            values.Add(values[0]);
        }

        var points = BuildPoints(values, width, height);
        DrawArea(drawingContext, points, height);
        DrawLine(drawingContext, points);
    }

    private static void DrawGrid(DrawingContext drawingContext, double width, double height)
    {
        var gridPen = new Pen(new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)), 1);
        gridPen.Freeze();

        for (var i = 1; i <= 2; i++)
        {
            var y = Math.Round(height * i / 3) + 0.5;
            drawingContext.DrawLine(gridPen, new Point(0, y), new Point(width, y));
        }
    }

    private List<double> GetValues()
    {
        var result = new List<double>();

        if (Values is null)
        {
            return result;
        }

        foreach (var value in Values)
        {
            if (value is null)
            {
                continue;
            }

            if (double.TryParse(Convert.ToString(value), out var number))
            {
                result.Add(Math.Clamp(number, 0, 100));
            }
        }

        return result;
    }

    private static List<Point> BuildPoints(IReadOnlyList<double> values, double width, double height)
    {
        const double topPadding = 4;
        const double bottomPadding = 5;

        var drawableHeight = Math.Max(1, height - topPadding - bottomPadding);
        var step = values.Count == 1 ? width : width / (values.Count - 1);
        var points = new List<Point>(values.Count);

        for (var i = 0; i < values.Count; i++)
        {
            var x = i * step;
            var y = topPadding + (100 - values[i]) / 100 * drawableHeight;
            points.Add(new Point(x, y));
        }

        return points;
    }

    private void DrawArea(DrawingContext drawingContext, IReadOnlyList<Point> points, double height)
    {
        if (points.Count < 2)
        {
            return;
        }

        var geometry = new StreamGeometry();

        using (var context = geometry.Open())
        {
            context.BeginFigure(new Point(points[0].X, height), true, true);
            context.LineTo(points[0], true, false);

            for (var i = 1; i < points.Count; i++)
            {
                context.LineTo(points[i], true, false);
            }

            context.LineTo(new Point(points[^1].X, height), true, false);
        }

        geometry.Freeze();
        drawingContext.DrawGeometry(Fill, null, geometry);
    }

    private void DrawLine(DrawingContext drawingContext, IReadOnlyList<Point> points)
    {
        if (points.Count < 2)
        {
            return;
        }

        var geometry = new StreamGeometry();

        using (var context = geometry.Open())
        {
            context.BeginFigure(points[0], false, false);

            for (var i = 1; i < points.Count; i++)
            {
                context.LineTo(points[i], true, false);
            }
        }

        geometry.Freeze();
        var pen = new Pen(Stroke, 1.8)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round
        };
        drawingContext.DrawGeometry(null, pen, geometry);
    }
}
