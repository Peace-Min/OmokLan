using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using OmokLan.Engine.Core;
using OmokLan.Engine.Net;
using OmokLan.Services;

namespace OmokLan.Views;

public sealed class BoolToVisibility : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool flag = value is bool b && b;
        if (parameter as string == "!") flag = !flag;
        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class NotNullToVisibility : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool present = value is not null;
        if (value is string s) present = !string.IsNullOrWhiteSpace(s);
        if (parameter as string == "!") present = !present;
        return present ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class PresenceToBrush : IValueConverter
{
    private static readonly Brush Available = Frozen("#34D399");
    private static readonly Brush Dnd = Frozen("#FB7185");
    private static readonly Brush InGame = Frozen("#FBBF24");

    private static Brush Frozen(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        PresenceStatus.Dnd => Dnd,
        PresenceStatus.InGame => InGame,
        _ => Available,
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class PresenceToText : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        PresenceStatus.Dnd => "방해금지",
        PresenceStatus.InGame => "게임중",
        _ => "대기중",
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class AvatarToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        int id = value is int i ? i : 0;
        var info = AvatarCatalog.Get(id);
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1),
            GradientStops =
            {
                new GradientStop((Color)ColorConverter.ConvertFromString(info.GradientStart), 0),
                new GradientStop((Color)ColorConverter.ConvertFromString(info.GradientEnd), 1)
            }
        };
        brush.Freeze();
        return brush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class AvatarToSymbolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        int id = value is int i ? i : 0;
        return AvatarCatalog.Get(id).Symbol;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class AvatarToNameConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        int id = value is int i ? i : 0;
        return AvatarCatalog.Get(id).Name;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class StoneToTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is Stone s)
        {
            return s switch
            {
                Stone.Black => "흑 (선공)",
                Stone.White => "백 (후공)",
                _ => "미정"
            };
        }
        return "-";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class TimerColorConverter : IValueConverter
{
    private static readonly Brush NormalBrush = Frozen("#38BDF8");
    private static readonly Brush WarningBrush = Frozen("#F87171");

    private static Brush Frozen(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        int sec = value is int s ? s : 30;
        return sec <= 10 ? WarningBrush : NormalBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
