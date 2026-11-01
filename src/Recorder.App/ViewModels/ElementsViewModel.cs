using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Recorder.App.Resources;
using Recorder.App.Services;
using Recorder.Core.Model;

namespace Recorder.App.ViewModels;

/// <summary>One row of the Elements tab: variable name, strategy badge and uniqueness tick (spec 11.2).</summary>
public sealed class ElementItemViewModel : ObservableObject
{
    private readonly ElementLocating _locating;

    public ElementItemViewModel(PageElement element, ElementLocating locating)
    {
        ArgumentNullException.ThrowIfNull(element);
        Element = element;
        _locating = locating;
        element.Changed += (_, _) => Refresh();
    }

    public PageElement Element { get; }

    public string Label => Element.Label;

    public string VariableName => Element.VariableName;

    public string PageName => Element.Page.DisplayName;

    public string ControlType => Element.Snapshot.ControlType;

    public string? Badge => Element.Selected is { } selected ? LocatorText.Badge(selected.Strategy) : null;

    public bool IsChecking => _locating.IsChecking(Element);

    public LocatorQuality Quality => LocatorText.Quality(Element.Selected);

    /// <summary>Says in words what the tick shows: the chosen locator's quality.</summary>
    public string TickTip => IsChecking ? Strings.Elements_Checking_Tip : Quality switch
    {
        LocatorQuality.Unique or LocatorQuality.Medium => Strings.Elements_Unique_Tip,
        LocatorQuality.None when Element.Locators?.HasUniqueLocator == true => Strings.Elements_ChosenNotUnique_Tip,
        LocatorQuality.None => Strings.Elements_NotUnique_Tip,
        _ => Strings.Elements_NotChecked_Tip,
    };

    /// <summary>Everything at once for screen readers: "amountInput, ID, Unique locator".</summary>
    public string AccessibleName => string.Join(", ", new[] { VariableName, Badge, TickTip }.Where(part => !string.IsNullOrEmpty(part)));

    /// <summary>Re-reads everything; called when the element or its check changes.</summary>
    public void Refresh() => OnPropertyChanged(string.Empty);
}

/// <summary>The Elements tab: captured controls grouped by window, with a filter box.</summary>
public sealed partial class ElementsViewModel : ObservableObject
{
    private readonly ElementLocating _locating;

    public ElementsViewModel(ElementLocating locating)
    {
        ArgumentNullException.ThrowIfNull(locating);
        _locating = locating;
        View = CollectionViewSource.GetDefaultView(Items);
        View.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ElementItemViewModel.PageName)));
        View.Filter = item => Matches((ElementItemViewModel)item);
        locating.CheckingChanged += (_, element) => Items.FirstOrDefault(item => item.Element == element)?.Refresh();
    }

    public event EventHandler<ElementItemViewModel?>? SelectionChanged;

    /// <summary>F2 on a row: the view moves focus to the Inspector's name box.</summary>
    public event EventHandler? RenameRequested;

    public ObservableCollection<ElementItemViewModel> Items { get; } = [];

    public ICollectionView View { get; }

    [ObservableProperty]
    public partial string FilterText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial ElementItemViewModel? Selected { get; set; }

    [ObservableProperty]
    public partial bool IsEmpty { get; set; } = true;

    [ObservableProperty]
    public partial bool HasNoMatch { get; set; }

    /// <summary>Adds a newly captured element, or finds it if it is already listed, and selects it.</summary>
    public ElementItemViewModel Show(PageElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        var item = Items.FirstOrDefault(existing => existing.Element.Id == element.Id);
        if (item is null)
        {
            item = new ElementItemViewModel(element, _locating);
            Items.Add(item);
            IsEmpty = false;
        }

        if (!Matches(item))
        {
            FilterText = string.Empty;
        }

        Selected = item;
        return item;
    }

    /// <summary>Drops an element that was merged into another one.</summary>
    public void Remove(PageElement element)
    {
        var item = Items.FirstOrDefault(existing => existing.Element == element);
        if (item is not null)
        {
            Items.Remove(item);
            IsEmpty = Items.Count == 0;
        }
    }

    [RelayCommand]
    private void Rename() => RenameRequested?.Invoke(this, EventArgs.Empty);

    partial void OnSelectedChanged(ElementItemViewModel? value) => SelectionChanged?.Invoke(this, value);

    partial void OnFilterTextChanged(string value)
    {
        View.Refresh();
        HasNoMatch = Items.Count > 0 && View.IsEmpty;
    }

    private bool Matches(ElementItemViewModel item)
    {
        var filter = FilterText.Trim();
        return filter.Length == 0
            || item.VariableName.Contains(filter, StringComparison.CurrentCultureIgnoreCase)
            || item.Label.Contains(filter, StringComparison.CurrentCultureIgnoreCase)
            || item.PageName.Contains(filter, StringComparison.CurrentCultureIgnoreCase)
            || (item.Element.Snapshot.Name?.Contains(filter, StringComparison.CurrentCultureIgnoreCase) ?? false);
    }
}
