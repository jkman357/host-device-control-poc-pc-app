using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace HostDeviceControl.App.Controls;

public sealed class WaveformControl : FrameworkElement
{
    public static readonly DependencyProperty SamplesProperty =
        DependencyProperty.Register(
            nameof(Samples),
            typeof(IReadOnlyList<double>),
            typeof(WaveformControl),
            new FrameworkPropertyMetadata(
                Array.Empty<double>(),
                FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MinimumValueProperty =
        DependencyProperty.Register(
            nameof(MinimumValue),
            typeof(double),
            typeof(WaveformControl),
            new FrameworkPropertyMetadata(
                -1.0,
                FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MaximumValueProperty =
        DependencyProperty.Register(
            nameof(MaximumValue),
            typeof(double),
            typeof(WaveformControl),
            new FrameworkPropertyMetadata(
                1.0,
                FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<double> Samples
    {
        get => (IReadOnlyList<double>)GetValue(SamplesProperty);
        set => SetValue(SamplesProperty, value);
    }

    public double MinimumValue
    {
        get => (double)GetValue(MinimumValueProperty);
        set => SetValue(MinimumValueProperty, value);
    }

    public double MaximumValue
    {
        get => (double)GetValue(MaximumValueProperty);
        set => SetValue(MaximumValueProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        Rect bounds = new(0, 0, ActualWidth, ActualHeight);
        drawingContext.DrawRectangle(
            new SolidColorBrush(Color.FromRgb(16, 24, 32)),
            null,
            bounds);

        DrawGrid(drawingContext, bounds);
        DrawWaveform(drawingContext, bounds);
    }

    private static void DrawGrid(DrawingContext drawingContext, Rect bounds)
    {
        var gridPen = new Pen(
            new SolidColorBrush(Color.FromArgb(70, 180, 190, 200)),
            1.0);
        gridPen.Freeze();

        const int verticalDivisions = 10;
        const int horizontalDivisions = 8;

        for (int index = 1; index < verticalDivisions; index++)
        {
            double x = bounds.Width * index / verticalDivisions;
            drawingContext.DrawLine(gridPen, new Point(x, 0), new Point(x, bounds.Height));
        }

        for (int index = 1; index < horizontalDivisions; index++)
        {
            double y = bounds.Height * index / horizontalDivisions;
            drawingContext.DrawLine(gridPen, new Point(0, y), new Point(bounds.Width, y));
        }
    }

    private void DrawWaveform(DrawingContext drawingContext, Rect bounds)
    {
        IReadOnlyList<double> samples = Samples;
        if ((samples.Count < 2) ||
            (bounds.Width <= 1) ||
            (bounds.Height <= 1) ||
            (MaximumValue <= MinimumValue))
        {
            return;
        }

        var linePen = new Pen(
            new SolidColorBrush(Color.FromRgb(64, 220, 160)),
            1.5);
        linePen.Freeze();

        var geometry = new StreamGeometry();
        using (StreamGeometryContext context = geometry.Open())
        {
            double firstY = ConvertY(samples[0], bounds.Height);
            context.BeginFigure(new Point(0, firstY), false, false);

            for (int index = 1; index < samples.Count; index++)
            {
                double x = bounds.Width * index / (samples.Count - 1);
                double y = ConvertY(samples[index], bounds.Height);
                context.LineTo(new Point(x, y), true, false);
            }
        }

        geometry.Freeze();
        drawingContext.DrawGeometry(null, linePen, geometry);
    }

    private double ConvertY(double value, double height)
    {
        double clamped = Math.Clamp(value, MinimumValue, MaximumValue);
        double normalized =
            (clamped - MinimumValue) / (MaximumValue - MinimumValue);
        return height - (normalized * height);
    }
}
