using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShareQ.Storage.Items;

namespace ShareQ.App.ViewModels;

/// <summary>Settings → Categories tab. Lists every clipboard category with its name, icon and
/// retention caps; lets the user add, rename, remove, and reorder. The default
/// <see cref="Category.Default"/> bucket is read-only — it cannot be renamed or deleted (its
/// items would have nowhere to go).</summary>
public sealed partial class CategoriesViewModel : ObservableObject, IDisposable
{
    private readonly ICategoryStore _store;

    public CategoriesViewModel(ICategoryStore store)
    {
        _store = store;
        Categories = [];
        _store.Changed += OnStoreChanged;
        _ = ReloadAsync();
    }

    public void Dispose() => _store.Changed -= OnStoreChanged;

    public ObservableCollection<CategoryRowViewModel> Categories { get; }

    [ObservableProperty]
    private string _newCategoryName = string.Empty;

    [ObservableProperty]
    private string _newCategoryIcon = string.Empty;

    private void OnStoreChanged(object? sender, EventArgs e)
        => Application.Current?.Dispatcher.InvokeAsync(() => _ = ReloadAsync());

    public async Task ReloadAsync()
    {
        var list = await _store.ListAsync(CancellationToken.None).ConfigureAwait(true);
        Categories.Clear();
        foreach (var c in list)
        {
            var isDefault = string.Equals(c.Name, Category.Default, StringComparison.Ordinal);
            Categories.Add(new CategoryRowViewModel(c, isDefault, this));
        }
    }

    [RelayCommand]
    private async Task AddCategoryAsync()
    {
        var name = NewCategoryName.Trim();
        if (string.IsNullOrEmpty(name)) return;
        // Skip duplicates: storing two categories with the same name violates the PK and the
        // user would just get a noisy SQL exception. Silent no-op is friendlier.
        if (Categories.Any(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase))) return;

        var sortOrder = Categories.Count;   // append at the end
        var icon = string.IsNullOrWhiteSpace(NewCategoryIcon) ? null : NewCategoryIcon.Trim();
        await _store.AddAsync(new Category(name, icon, sortOrder), CancellationToken.None).ConfigureAwait(true);
        NewCategoryName = string.Empty;
        NewCategoryIcon = string.Empty;
    }

    public Task RenameAsync(string oldName, string newName)
        => _store.RenameAsync(oldName, newName, CancellationToken.None);

    public Task DeleteAsync(string name)
        => _store.DeleteAsync(name, CancellationToken.None);

    public Task UpdateAsync(Category category)
        => _store.UpdateAsync(category, CancellationToken.None);
}

public sealed partial class CategoryRowViewModel : ObservableObject
{
    private readonly CategoriesViewModel _owner;
    private readonly Category _original;

    public CategoryRowViewModel(Category category, bool isDefault, CategoriesViewModel owner)
    {
        _original = category;
        _owner = owner;
        IsDefault = isDefault;
        _name = category.Name;
        _icon = category.Icon ?? string.Empty;
        _maxItems = category.MaxItems;
        _autoCleanupDays = category.AutoCleanupDays;
    }

    public bool IsDefault { get; }
    public bool CanModify => !IsDefault;

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private string _icon;

    [ObservableProperty]
    private int _maxItems;

    [ObservableProperty]
    private int _autoCleanupDays;

    [RelayCommand(CanExecute = nameof(CanModify))]
    private async Task SaveAsync()
    {
        // Two-step when renaming: rename first (re-routes items at the same time), then
        // update icon/caps separately since RenameAsync only touches the name.
        if (!string.Equals(Name, _original.Name, StringComparison.Ordinal))
        {
            await _owner.RenameAsync(_original.Name, Name).ConfigureAwait(true);
        }
        var updated = new Category(Name, string.IsNullOrWhiteSpace(Icon) ? null : Icon.Trim(),
            _original.SortOrder, MaxItems, AutoCleanupDays);
        await _owner.UpdateAsync(updated).ConfigureAwait(true);
    }

    [RelayCommand(CanExecute = nameof(CanModify))]
    private async Task DeleteAsync()
    {
        var ok = MessageBox.Show(
            $"Delete category '{_original.Name}'?\n\nItems in this category will be moved to '{Category.Default}'. Pinned items keep their pin.",
            "Delete category",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning,
            MessageBoxResult.Cancel);
        if (ok != MessageBoxResult.OK) return;
        await _owner.DeleteAsync(_original.Name).ConfigureAwait(true);
    }
}
