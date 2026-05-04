using System.Collections.ObjectModel;
using System.Globalization;
using System.Reflection;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using ShareQ.ImageEffects;
using ShareQ.ImageEffects.Drawing;
using ShareQ.ImageEffects.Parameters;
using SkiaSharp;

namespace ShareQ.App.ViewModels.ImageEffects;

/// <summary>One row in the property grid: a single tunable property exposed by an
/// <see cref="ImageEffect"/>. The view-model is discriminated by <see cref="Kind"/>: the
/// XAML template branches on <c>IsFloat / IsBool / IsColor / IsPadding / IsEnum</c> to pick
/// the right control (slider / checkbox / colour swatch / 4-up grid / combo). Float and int
/// effects share the same slider+textbox path through <see cref="Value"/>; the other types
/// store their state in dedicated properties so we don't have to box through object.</summary>
public sealed partial class EffectParameterViewModel : ObservableObject
{
    private readonly ImageEffect _effect;
    private readonly PropertyInfo _property;
    private bool _suppress;

    public string Label { get; set; }
    public double Min { get; set; }
    public double Max { get; set; }
    public double Step { get; set; }
    public string ValueFormat { get; set; }

    public EffectParameterKind Kind { get; }
    public bool IsNumber => Kind == EffectParameterKind.Number;
    public bool IsBool => Kind == EffectParameterKind.Bool;
    public bool IsColor => Kind == EffectParameterKind.Color;
    public bool IsPadding => Kind == EffectParameterKind.Padding;
    public bool IsEnum => Kind == EffectParameterKind.Enum;
    public bool IsString => Kind == EffectParameterKind.Text;

    public Action? Changed { get; set; }

    [ObservableProperty] private double _value;
    [ObservableProperty] private bool _boolValue;
    [ObservableProperty] private SKColor _colorValue;
    [ObservableProperty] private int _paddingLeft;
    [ObservableProperty] private int _paddingTop;
    [ObservableProperty] private int _paddingRight;
    [ObservableProperty] private int _paddingBottom;
    [ObservableProperty] private string? _enumValue;
    [ObservableProperty] private string _stringValue = string.Empty;

    /// <summary>Frozen <see cref="SolidColorBrush"/> form of <see cref="ColorValue"/> — bound
    /// to the swatch <c>Background</c> in XAML. Recomputed on every colour change so the swatch
    /// stays in sync after the picker dialog writes back. We deliberately use System.Windows.Media
    /// types here (rather than a string + converter) to keep the data path single-hop.</summary>
    public Brush ColorBrush => BuildBrush(ColorValue);

    private static Brush BuildBrush(SKColor c)
    {
        var brush = new SolidColorBrush(Color.FromArgb(c.Alpha, c.Red, c.Green, c.Blue));
        brush.Freeze();
        return brush;
    }

    public ObservableCollection<string> EnumOptions { get; } = new();

    public EffectParameterViewModel(ImageEffect effect, PropertyInfo property)
    {
        _effect = effect;
        _property = property;
        var attr = property.GetCustomAttribute<EffectParameterAttribute>();
        Label = attr?.DisplayName ?? property.Name;
        Min = attr?.Min ?? -100;
        Max = attr?.Max ?? 100;
        Step = attr?.Step ?? 1;
        ValueFormat = (attr?.Decimals ?? 0) > 0 ? $"F{attr!.Decimals}" : "F0";

        Kind = ResolveKind(property.PropertyType);

        _suppress = true;
        var current = property.GetValue(effect);
        switch (Kind)
        {
            case EffectParameterKind.Number:
                _value = current is null ? 0 : Convert.ToDouble(current, CultureInfo.InvariantCulture);
                break;
            case EffectParameterKind.Bool:
                _boolValue = (bool?)current ?? false;
                break;
            case EffectParameterKind.Color:
                _colorValue = current is SKColor c ? c : SKColors.Transparent;
                OnPropertyChanged(nameof(ColorBrush));
                break;
            case EffectParameterKind.Padding:
                if (current is Padding p)
                {
                    _paddingLeft = p.Left; _paddingTop = p.Top; _paddingRight = p.Right; _paddingBottom = p.Bottom;
                }
                break;
            case EffectParameterKind.Enum:
                foreach (var name in Enum.GetNames(property.PropertyType)) EnumOptions.Add(name);
                _enumValue = current?.ToString() ?? EnumOptions.FirstOrDefault();
                break;
            case EffectParameterKind.Text:
                _stringValue = current as string ?? string.Empty;
                break;
        }
        _suppress = false;
    }

    private static EffectParameterKind ResolveKind(Type t)
    {
        if (t == typeof(float) || t == typeof(double) || t == typeof(int)) return EffectParameterKind.Number;
        if (t == typeof(bool)) return EffectParameterKind.Bool;
        if (t == typeof(SKColor)) return EffectParameterKind.Color;
        if (t == typeof(Padding)) return EffectParameterKind.Padding;
        if (t.IsEnum) return EffectParameterKind.Enum;
        if (t == typeof(string)) return EffectParameterKind.Text;
        return EffectParameterKind.Unknown;
    }

    partial void OnValueChanged(double value)
    {
        if (_suppress || Kind != EffectParameterKind.Number) return;
        // Cast back to the property's CLR type. Effects use float almost universally; ints and
        // doubles are tolerated through Convert.ChangeType so an integer parameter still
        // round-trips cleanly through the double-typed Slider.
        var typed = Convert.ChangeType(value, _property.PropertyType, CultureInfo.InvariantCulture);
        _property.SetValue(_effect, typed);
        Changed?.Invoke();
    }

    partial void OnBoolValueChanged(bool value)
    {
        if (_suppress || Kind != EffectParameterKind.Bool) return;
        _property.SetValue(_effect, value);
        Changed?.Invoke();
    }

    partial void OnColorValueChanged(SKColor value)
    {
        if (_suppress || Kind != EffectParameterKind.Color) return;
        _property.SetValue(_effect, value);
        OnPropertyChanged(nameof(ColorBrush));
        Changed?.Invoke();
    }

    private void PushPadding()
    {
        if (_suppress || Kind != EffectParameterKind.Padding) return;
        _property.SetValue(_effect, new Padding(PaddingLeft, PaddingTop, PaddingRight, PaddingBottom));
        Changed?.Invoke();
    }
    partial void OnPaddingLeftChanged(int value) => PushPadding();
    partial void OnPaddingTopChanged(int value) => PushPadding();
    partial void OnPaddingRightChanged(int value) => PushPadding();
    partial void OnPaddingBottomChanged(int value) => PushPadding();

    partial void OnEnumValueChanged(string? value)
    {
        if (_suppress || Kind != EffectParameterKind.Enum || value is null) return;
        if (Enum.TryParse(_property.PropertyType, value, out var typed))
        {
            _property.SetValue(_effect, typed);
            Changed?.Invoke();
        }
    }

    partial void OnStringValueChanged(string value)
    {
        if (_suppress || Kind != EffectParameterKind.Text) return;
        _property.SetValue(_effect, value);
        Changed?.Invoke();
    }
}

public enum EffectParameterKind
{
    Unknown,
    /// <summary>Float / double / int — rendered as slider + numeric textbox.</summary>
    Number,
    Bool,
    Color,
    Padding,
    Enum,
    /// <summary>Free-form string — rendered as a TextBox. Used for watermark text, font
    /// descriptors, image paths. Named "Text" instead of "String" so the analyzer doesn't
    /// flag the enum value as colliding with the CLR type name.</summary>
    Text,
}
