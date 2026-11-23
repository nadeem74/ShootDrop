using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;

namespace ShootDrop.App.Views.Controls;

public partial class DateButton : UserControl
{
    public static readonly StyledProperty<DateTime?> SelectedDateProperty =
        AvaloniaProperty.Register<DateButton, DateTime?>(nameof(SelectedDate), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<string> PlaceholderProperty =
        AvaloniaProperty.Register<DateButton, string>(nameof(Placeholder), "Pick a day");

    /// <summary>Shown when no date is chosen, and offered by "Clear" (e.g. "same day").</summary>
    public string Placeholder
    {
        get => GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    public DateTime? SelectedDate
    {
        get => GetValue(SelectedDateProperty);
        set => SetValue(SelectedDateProperty, value);
    }

    /// <summary>Show "Clear" in the calendar (for optional dates like the end of a range).</summary>
    public bool CanClear { get; set; }

    private bool _syncing;

    public DateButton()
    {
        InitializeComponent();
        Picker.SelectedDatesChanged += (_, _) =>
        {
            if (_syncing) return;
            SelectedDate = Picker.SelectedDate;
            Popup.Hide();
        };
        TodayButton.Click += (_, _) =>
        {
            SelectedDate = DateTime.Today;
            Popup.Hide();
        };
        ClearButton.Click += (_, _) =>
        {
            SelectedDate = null;
            Popup.Hide();
        };
        Popup.Opening += (_, _) =>
        {
            ClearButton.IsVisible = CanClear;
            _syncing = true;
            Picker.SelectedDate = SelectedDate;
            Picker.DisplayDate = SelectedDate ?? DateTime.Today;
            _syncing = false;
        };
        Refresh();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SelectedDateProperty || change.Property == PlaceholderProperty) Refresh();
    }

    private void Refresh()
    {
        if (Label is null) return;
        Label.Text = SelectedDate is { } d ? d.ToString("ddd d MMM yyyy", CultureInfo.CurrentCulture) : Placeholder;
        Label.Foreground = SelectedDate is null ? new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#8A8C93")) : null;
    }
}
