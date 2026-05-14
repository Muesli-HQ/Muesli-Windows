using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MediaBrushes = System.Windows.Media.Brushes;
using MediaColor = System.Windows.Media.Color;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;
using WpfOrientation = System.Windows.Controls.Orientation;

namespace Muesli.Windows.Services;

public sealed class ToastNotificationService
{
    private const double CompactIdleWidth = 44;
    private const double CompactIdleHeight = 28;
    private const double DragThreshold = 10;
    private static readonly MediaColor GlassColor = MediaColor.FromRgb(30, 30, 46);
    private readonly DispatcherTimer _timer = new();
    private Window? _window;
    private string _idleHotkey = "F8";
    private bool _idleVisible;
    private bool _showIdleIndicator = true;
    private bool _hovered;
    private bool _suppressIdleHoverUntilMouseLeaves;
    private System.Windows.Point? _dragStart;
    private double _dragWindowLeft;
    private double _dragWindowTop;
    private bool _dragged;
    private string _indicatorAnchor = "Top Center";
    private ToastState _currentState = ToastState.Idle;
    private Func<Task>? _stopRecording;
    private Func<Task>? _cancelRecording;
    private double? _savedLeft;
    private double? _savedTop;

    public event EventHandler<IndicatorPositionChangedEventArgs>? PositionChanged;

    public ToastNotificationService()
    {
        _timer.Tick += (_, _) =>
        {
            _timer.Stop();
            if (_idleVisible)
            {
                ShowIdle(_idleHotkey);
            }
            else
            {
                _window?.Hide();
            }
        };
    }

    public void ShowIdle(string hotkey)
    {
        _idleHotkey = string.IsNullOrWhiteSpace(hotkey) ? "F8" : hotkey;
        if (!_showIdleIndicator)
        {
            _idleVisible = false;
            _timer.Stop();
            if (_currentState == ToastState.Idle)
            {
                _window?.Hide();
            }
            return;
        }

        _idleVisible = true;
        _timer.Stop();
        _hovered = false;
        _suppressIdleHoverUntilMouseLeaves = true;
        ShowIndicator(ToastState.Idle, "Muesli", $"Hold {_idleHotkey} to dictate", 0);
    }

    public void SetIdleIndicatorVisible(bool visible, bool showNow = true)
    {
        _showIdleIndicator = visible;
        if (visible)
        {
            if (showNow)
            {
                ShowIdle(_idleHotkey);
            }
            return;
        }

        _idleVisible = false;
        if (_currentState == ToastState.Idle)
        {
            _timer.Stop();
            _window?.Hide();
        }
    }

    public void ConfigureActions(Func<Task> stopRecording, Func<Task> cancelRecording)
    {
        _stopRecording = stopRecording;
        _cancelRecording = cancelRecording;
    }

    public void SetSavedPosition(double? left, double? top)
    {
        _savedLeft = left;
        _savedTop = top;
    }

    public void SetIndicatorAnchor(string anchor, bool clearCustomPosition)
    {
        _indicatorAnchor = string.IsNullOrWhiteSpace(anchor) ? "Top Center" : anchor;
        if (clearCustomPosition)
        {
            _savedLeft = null;
            _savedTop = null;
        }

        if (_window?.IsVisible == true)
        {
            PositionWindow(_window);
        }
    }

    public void Show(string title, string message, ToastState state, int durationMs = 2200)
    {
        _timer.Stop();
        ShowIndicator(state, title, message, durationMs);
    }

    public void Hide()
    {
        _timer.Stop();
        if (_idleVisible)
        {
            ShowIdle(_idleHotkey);
            return;
        }

        _window?.Hide();
    }

    private void ShowIndicator(ToastState state, string title, string message, int durationMs)
    {
        if (state == ToastState.Idle && !_showIdleIndicator)
        {
            return;
        }

        _window ??= CreateWindow();
        EnsureMouseHandlers(_window);
        if (state != ToastState.Idle)
        {
            _hovered = false;
            _suppressIdleHoverUntilMouseLeaves = true;
        }

        _currentState = state;

        var size = IndicatorSizeFor(state, _hovered);
        _window.Width = size.Width;
        _window.Height = size.Height;
        _window.Content = CreateContent(state, title, message, _hovered);
        PositionWindow(_window);
        _window.Show();

        if (durationMs > 0)
        {
            _timer.Interval = TimeSpan.FromMilliseconds(durationMs);
            _timer.Start();
        }
    }

    private static Window CreateWindow()
    {
        return new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = MediaBrushes.Transparent,
            ShowInTaskbar = false,
            Topmost = true,
            ResizeMode = ResizeMode.NoResize,
            Focusable = false,
            ShowActivated = false,
            SnapsToDevicePixels = true,
            UseLayoutRounding = true
        };
    }

    private void EnsureMouseHandlers(Window window)
    {
        if (window.Tag as string == "muesli-indicator-handlers")
        {
            return;
        }

        window.Tag = "muesli-indicator-handlers";
        window.MouseEnter += (_, _) =>
        {
            if (!_idleVisible || _currentState != ToastState.Idle || _suppressIdleHoverUntilMouseLeaves)
            {
                return;
            }

            _hovered = true;
            ShowIndicator(ToastState.Idle, "Muesli", $"Hold {_idleHotkey} to dictate", 0);
        };
        window.MouseLeave += (_, _) =>
        {
            if (!_idleVisible)
            {
                return;
            }

            _suppressIdleHoverUntilMouseLeaves = false;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(140) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                if (_window?.IsMouseOver == true)
                {
                    return;
                }

                _hovered = false;
                ShowIndicator(ToastState.Idle, "Muesli", $"Hold {_idleHotkey} to dictate", 0);
            };
            timer.Start();
        };
        window.MouseLeftButtonDown += (_, args) =>
        {
            _dragStart = window.PointToScreen(args.GetPosition(window));
            _dragWindowLeft = window.Left;
            _dragWindowTop = window.Top;
            _dragged = false;
            window.CaptureMouse();
        };
        window.MouseMove += (_, args) =>
        {
            if (_dragStart is null || args.LeftButton != MouseButtonState.Pressed)
            {
                return;
            }

            var position = window.PointToScreen(args.GetPosition(window));
            var dx = position.X - _dragStart.Value.X;
            var dy = position.Y - _dragStart.Value.Y;
            if (!_dragged && Math.Sqrt(dx * dx + dy * dy) < DragThreshold)
            {
                return;
            }

            _dragged = true;
            window.Left = Math.Round(_dragWindowLeft + dx);
            window.Top = Math.Round(_dragWindowTop + dy);
        };
        window.MouseLeftButtonUp += (_, args) =>
        {
            var x = args.GetPosition(window).X;
            _dragStart = null;
            window.ReleaseMouseCapture();
            if (_dragged)
            {
                _savedLeft = window.Left + window.Width / 2;
                _savedTop = window.Top + window.Height / 2;
                PositionChanged?.Invoke(this, new IndicatorPositionChangedEventArgs(_savedLeft.Value, _savedTop.Value));
                return;
            }

            if (_currentState == ToastState.Recording)
            {
                _ = x < 30
                    ? (_cancelRecording?.Invoke() ?? Task.CompletedTask)
                    : (_stopRecording?.Invoke() ?? Task.CompletedTask);
            }
        };
        window.MouseRightButtonUp += (_, _) =>
        {
            if (_currentState == ToastState.Recording)
            {
                _ = _cancelRecording?.Invoke() ?? Task.CompletedTask;
            }
        };
    }

    private static System.Windows.Size IndicatorSizeFor(ToastState state, bool hovered)
    {
        return state switch
        {
            ToastState.Idle => hovered ? new System.Windows.Size(220, 36) : new System.Windows.Size(CompactIdleWidth, CompactIdleHeight),
            ToastState.Recording => new System.Windows.Size(76, 22),
            ToastState.Transcribing => new System.Windows.Size(120, 32),
            ToastState.Success => new System.Windows.Size(220, 36),
            ToastState.Error => new System.Windows.Size(260, 36),
            _ => new System.Windows.Size(44, 28)
        };
    }

    private static FrameworkElement CreateContent(ToastState state, string title, string message, bool hovered)
    {
        return state switch
        {
            ToastState.Idle => CreateIdlePill(message, hovered),
            ToastState.Recording => CreateRecordingPill(),
            ToastState.Transcribing => CreateTranscribingPill(string.IsNullOrWhiteSpace(title) ? "Transcribing" : title),
            ToastState.Success => CreateStatusPill(title, MediaColor.FromRgb(52, 211, 153), 18),
            ToastState.Error => CreateStatusPill(title, MediaColor.FromRgb(248, 113, 113), 18),
            _ => CreateIdlePill(message, hovered)
        };
    }

    private static FrameworkElement CreateIdlePill(string message, bool hovered)
    {
        var root = CreatePill(GlassColor, hovered ? 0.72 : 0.44, hovered ? 18 : 14, hovered ? 0.14 : 0.22);
        root.Width = hovered ? 220 : CompactIdleWidth;
        root.Height = hovered ? 36 : CompactIdleHeight;
        root.Padding = new Thickness(hovered ? 12 : 0, 0, hovered ? 12 : 0, 0);

        if (!hovered)
        {
            root.Child = CreateMuesliGlyph(18, new Thickness(0));
            return root;
        }

        root.Child = new StackPanel
        {
            Orientation = WpfOrientation.Horizontal,
            HorizontalAlignment = WpfHorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                CreateMuesliGlyph(15, new Thickness(0, 0, 7, 0)),
                CreateInlineText(message, 11, FontWeights.SemiBold, new Thickness(0), 0.75)
            }
        };
        return root;
    }

    private static FrameworkElement CreateRecordingPill()
    {
        var root = CreatePill(MediaColor.FromRgb(239, 68, 68), 0.85, 11, 0.16);
        root.Width = 76;
        root.Height = 22;
        root.Padding = new Thickness(7, 0, 8, 0);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });

        var cancel = CreateInlineText("x", 8, FontWeights.SemiBold, new Thickness(0, -1, 0, 0), 0.45);
        Grid.SetColumn(cancel, 0);
        grid.Children.Add(cancel);

        var waveform = new StackPanel
        {
            Orientation = WpfOrientation.Horizontal,
            HorizontalAlignment = WpfHorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        var heights = new[] { 5d, 10d, 10d, 5d };
        for (var i = 0; i < heights.Length; i++)
        {
            var bar = new Border
            {
                Width = 2.5,
                Height = heights[i],
                Margin = new Thickness(1.5, 0, 1.5, 0),
                CornerRadius = new CornerRadius(1.25),
                Background = new SolidColorBrush(MediaColor.FromArgb(220, 255, 255, 255)),
                VerticalAlignment = VerticalAlignment.Center
            };
            bar.BeginAnimation(FrameworkElement.HeightProperty, new DoubleAnimation
            {
                From = Math.Max(4, heights[i] * 0.55),
                To = heights[i],
                Duration = TimeSpan.FromMilliseconds(260 + i * 60),
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever
            });
            waveform.Children.Add(bar);
        }

        Grid.SetColumn(waveform, 1);
        grid.Children.Add(waveform);

        var stop = new Border
        {
            Width = 6,
            Height = 6,
            CornerRadius = new CornerRadius(1),
            Background = new SolidColorBrush(MediaColor.FromArgb(217, 255, 255, 255)),
            HorizontalAlignment = WpfHorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(stop, 2);
        grid.Children.Add(stop);

        root.Child = grid;
        return root;
    }

    private static FrameworkElement CreateTranscribingPill(string title)
    {
        var root = CreatePill(GlassColor, 0.62, 16, 0.16);
        root.Width = 120;
        root.Height = 32;
        root.Padding = new Thickness(12, 0, 12, 0);
        root.Child = new StackPanel
        {
            Orientation = WpfOrientation.Horizontal,
            HorizontalAlignment = WpfHorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                CreateInlineText("\uE895", 12, FontWeights.SemiBold, new Thickness(0, 0, 5, 0)),
                CreateInlineText(title, 11, FontWeights.SemiBold, new Thickness(0), 0.82)
            }
        };
        return root;
    }

    private static FrameworkElement CreateStatusPill(string title, MediaColor accent, double radius)
    {
        var root = CreatePill(GlassColor, 0.72, radius, 0.14);
        root.Width = title.Length > 24 ? 260 : 220;
        root.Height = 36;
        root.Padding = new Thickness(12, 0, 12, 0);
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var dot = new Border
        {
            Width = 7,
            Height = 7,
            CornerRadius = new CornerRadius(99),
            Background = new SolidColorBrush(accent),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(dot, 0);
        grid.Children.Add(dot);

        var label = CreateInlineText(title, 11, FontWeights.SemiBold, new Thickness(8, 0, 0, 0), 0.82);
        label.TextTrimming = TextTrimming.CharacterEllipsis;
        Grid.SetColumn(label, 1);
        grid.Children.Add(label);

        root.Child = grid;
        return root;
    }

    private static TextBlock CreateCenteredText(string text, double fontSize, FontWeight weight)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = fontSize,
            FontWeight = weight,
            HorizontalAlignment = WpfHorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
    }

    private static FrameworkElement CreateMuesliGlyph(double size, Thickness margin)
    {
        var assetPath = Path.Combine(AppContext.BaseDirectory, "Assets", "menu_m_template@2x.png");
        if (File.Exists(assetPath))
        {
            return new System.Windows.Controls.Image
            {
                Source = new BitmapImage(new Uri(assetPath, UriKind.Absolute)),
                Width = size,
                Height = size,
                Margin = margin,
                Stretch = Stretch.Uniform,
                Opacity = 0.9,
                HorizontalAlignment = WpfHorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        return CreateInlineText("m", 13, FontWeights.Bold, margin, 0.9);
    }

    private static TextBlock CreateInlineText(string text, double fontSize, FontWeight weight, Thickness margin, double alpha = 1)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = fontSize,
            FontWeight = weight,
            Margin = margin,
            Foreground = new SolidColorBrush(MediaColor.FromArgb((byte)(255 * alpha), 255, 255, 255)),
            HorizontalAlignment = WpfHorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
    }

    private static Border CreatePill(MediaColor color, double opacity, double radius, double borderOpacity)
    {
        return new Border
        {
            CornerRadius = new CornerRadius(radius),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(MediaColor.FromArgb((byte)(255 * borderOpacity), 255, 255, 255)),
            Background = new SolidColorBrush(MediaColor.FromArgb((byte)(255 * opacity), color.R, color.G, color.B)),
            Effect = new DropShadowEffect
            {
                BlurRadius = 18,
                ShadowDepth = 0,
                Opacity = 0.28,
                Color = MediaColor.FromRgb(0, 0, 0)
            }
        };
    }

    private void PositionWindow(Window window)
    {
        if (_indicatorAnchor.Equals("Custom", StringComparison.OrdinalIgnoreCase) &&
            _savedLeft is not null &&
            _savedTop is not null)
        {
            window.Left = _savedLeft.Value - window.Width / 2;
            window.Top = _savedTop.Value - window.Height / 2;
            return;
        }

        var area = SystemParameters.WorkArea;
        var anchor = _indicatorAnchor.Trim();
        var left = anchor switch
        {
            "Top Left" or "Bottom Left" => area.Left + 18,
            "Top Right" or "Bottom Right" => area.Right - window.Width - 18,
            "Top Center" or "Bottom Center" => CenteredLeft(area, window.Width),
            _ => area.Left + (area.Width - window.Width) / 2
        };
        var top = anchor switch
        {
            "Bottom Left" or "Bottom Center" or "Bottom Right" => area.Bottom - window.Height - 18,
            _ => area.Top + 18
        };

        window.Left = Math.Round(left);
        window.Top = Math.Round(top);
    }

    private static double CenteredLeft(Rect area, double width)
    {
        return area.Left + area.Width / 2 - width / 2;
    }
}

public sealed class IndicatorPositionChangedEventArgs(double left, double top) : EventArgs
{
    public double Left { get; } = left;
    public double Top { get; } = top;
}

public enum ToastState
{
    Idle,
    Recording,
    Transcribing,
    Success,
    Error
}
