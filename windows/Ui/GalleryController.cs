using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using Wallpepper.Desktop.Performance;
using Wallpepper.Library;
using Wallpepper.Settings;
using Wallpepper.Web;

namespace Wallpepper.Ui;

/// <summary>The gallery of the library window: its cards, the category chips, search, sort order and the selection.</summary>
internal sealed class GalleryController
{
    private const string AllChip = "all";
    private const string FavouritesChip = "favourites";

    private static readonly (string Id, string Label)[] ChipDefinitions =
    {
        (AllChip, "All"), (FavouritesChip, "Favourites"), ("featured", "Featured"), ("space", "Space"), ("water", "Water"),
        ("nature", "Nature"), ("calming", "Calming"), ("meditative", "Meditative"), ("abstract", "Abstract"), ("3d", "3D"),
        ("math", "Math"), ("tech", "Tech"), ("fun", "Fun"), ("retro", "Retro"), (ShaderMetadata.MultiMonitorTag, "Multi-monitor"),
        (ShaderMetadata.WebTag, "Web"), ("benchmark", "Benchmark"), (ShaderMetadata.CustomTag, "Custom"),
        (ShaderMetadata.TerminalOnlyTag, "Terminal effects"),
    };

    private static readonly (LibrarySort Sort, string Label)[] SortChoices =
    {
        (LibrarySort.Name, "Name"), (LibrarySort.Recent, "Recently used"), (LibrarySort.Cost, "Lightest on the GPU"),
    };

    private readonly LibraryWindow _view;
    private readonly App _app;
    private readonly ThumbnailCache _thumbnails;
    private readonly ObservableCollection<ShaderItem> _items = new ObservableCollection<ShaderItem>();
    private readonly Dictionary<string, ShaderItem> _byPath = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _costRequested = new(StringComparer.OrdinalIgnoreCase);
    private readonly ICollectionView _visible;
    private string _chip = AllChip;
    private bool _selecting;

    public GalleryController(LibraryWindow view, App app, ThumbnailCache thumbnails)
    {
        _view = view;
        _app = app;
        _thumbnails = thumbnails;
        _visible = CollectionViewSource.GetDefaultView(_items);
        _visible.Filter = item => Matches((ShaderItem)item);
        view.Gallery.ItemsSource = _visible;
        view.Gallery.SelectionChanged += OnSelectionChanged;
        view.Search.TextChanged += (_, _) =>
        {
            if (view.IsLoaded)
            {
                ApplyFilter();
            }
        };
    }

    /// <summary>The selected item changed (path or null).</summary>
    public event Action<string?>? SelectionChanged;

    public string? Selected { get; private set; }

    public ShaderItem? SelectedItem => ItemFor(Selected);

    /// <summary>Paths of the cards the filter lets through, in display order.</summary>
    public IReadOnlyList<string> VisiblePaths => _visible.Cast<ShaderItem>().Select(item => item.Path).ToList();

    /// <summary>Columns of the card grid right now (for moving the selection up and down).</summary>
    public int Columns => FindChild<CardPanel>(_view.Gallery)?.Columns ?? 1;

    public ShaderItem? ItemFor(string? path) => path != null && _byPath.TryGetValue(path, out ShaderItem? item) ? item : null;

    /// <summary>The card under a mouse event's source, if any.</summary>
    public static ShaderItem? ItemAt(object source)
    {
        var element = source as DependencyObject;
        while (element != null && element is not ListBoxItem)
        {
            element = element is Visual ? VisualTreeHelper.GetParent(element) : LogicalTreeHelper.GetParent(element);
        }
        return (element as ListBoxItem)?.DataContext as ShaderItem;
    }

    /// <summary>Brings the cards in line with the library: new items, changed names and tags, the sort order.</summary>
    public void Rebuild()
    {
        var config = _app.Settings.Config;
        var wanted = new List<ShaderItem>();
        foreach (string path in _app.Library.Items)
        {
            if (!_byPath.TryGetValue(path, out ShaderItem? item))
            {
                item = new ShaderItem(path, WebInfo.IsWeb(path));
                _byPath[path] = item;
            }
            ShaderMetadata metadata = _app.Library.Metadata(path);
            item.Name = _app.Library.Title(path);
            item.Tags = metadata.Tags;
            item.TerminalOnly = metadata.IsTerminalOnly;
            item.IsFavourite = _app.Library.IsFavourite(path);
            item.IsLive = _app.Wallpaper.IsLive(path);
            item.HasError = !item.IsWeb && _app.Status.HasError(path);
            item.LastUsed = config.LastUsed.TryGetValue(Path.GetFileName(path), out long used) ? used : 0;
            UpdateCost(item);
            wanted.Add(item);
        }
        foreach (string gone in _byPath.Keys.Where(path => !_app.Library.Contains(path)).ToList())
        {
            _byPath.Remove(gone);
        }

        List<ShaderItem> sorted = Sort(wanted, config.Sort);
        if (!sorted.SequenceEqual(_items))
        {
            WithoutSelectionEvents(() =>
            {
                _items.Clear();
                foreach (ShaderItem item in sorted)
                {
                    _items.Add(item);
                }
                _view.Gallery.SelectedItem = SelectedItem;
            });
        }
        foreach (ShaderItem item in sorted.Where(item => item.Thumb == null && !item.HasError))
        {
            _thumbnails.Ensure(item);
        }
        BuildChips();
        UpdateEmptyState();
        if (Selected == null || !_byPath.ContainsKey(Selected))
        {
            Select(VisiblePaths.FirstOrDefault());
        }
    }

    /// <summary>Selects a card and scrolls to it; null (or a path that is not there) clears the selection.</summary>
    public void Select(string? path)
    {
        ShaderItem? item = ItemFor(path);
        WithoutSelectionEvents(() => _view.Gallery.SelectedItem = item);
        if (item != null)
        {
            _view.Gallery.ScrollIntoView(item);
        }
        SetSelected(item?.Path);
    }

    /// <summary>Moves the selection by a number of cards (keyboard).</summary>
    public void MoveSelection(int delta)
    {
        var paths = VisiblePaths;
        if (paths.Count == 0)
        {
            return;
        }
        int index = paths.ToList().FindIndex(path => string.Equals(path, Selected, StringComparison.OrdinalIgnoreCase));
        Select(paths[Math.Clamp(Math.Max(0, index) + delta, 0, paths.Count - 1)]);
    }

    /// <summary>The path to select once the selected card is deleted: its neighbour.</summary>
    public string? NeighbourOf(string path)
    {
        var paths = VisiblePaths.ToList();
        int index = paths.FindIndex(candidate => string.Equals(candidate, path, StringComparison.OrdinalIgnoreCase));
        if (paths.Count <= 1 || index < 0)
        {
            return null;
        }
        return paths[index + 1 < paths.Count ? index + 1 : index - 1];
    }

    /// <summary>Shows everything again (after files were added) and selects one of them.</summary>
    public void ShowAllAndSelect(string path)
    {
        _chip = AllChip;
        _view.Search.Text = "";
        BuildChips();
        ApplyFilter();
        Select(path);
    }

    public void ToggleFavourite(ShaderItem item)
    {
        item.IsFavourite = !item.IsFavourite;
        _app.Library.SetFavourite(item.Path, item.IsFavourite);
        if (_chip == FavouritesChip)
        {
            ApplyFilter();
        }
    }

    public void ShowSortMenu()
    {
        var menu = new ContextMenu
        {
            PlacementTarget = _view.SortBtn,
            Placement = PlacementMode.Bottom,
            HorizontalOffset = -14,
            VerticalOffset = -8,
        };
        foreach (var (sort, label) in SortChoices)
        {
            var entry = new MenuItem { Header = label, IsChecked = _app.Settings.Config.Sort == sort };
            entry.Click += (_, _) =>
            {
                _app.Settings.Change(SettingsArea.None, config => config.Sort = sort);
                Rebuild();
            };
            menu.Items.Add(entry);
        }
        menu.IsOpen = true;
    }

    /// <summary>What is live changed: the badges follow.</summary>
    public void RefreshLive()
    {
        foreach (ShaderItem item in _items)
        {
            item.IsLive = _app.Wallpaper.IsLive(item.Path);
            item.LastUsed = _app.Settings.Config.LastUsed.TryGetValue(Path.GetFileName(item.Path), out long used) ? used : 0;
        }
    }

    /// <summary>Costs, the resource level or the light settings changed: the cost badges follow.</summary>
    public void RefreshCosts()
    {
        foreach (ShaderItem item in _items)
        {
            UpdateCost(item);
        }
    }

    /// <summary>A shader was compiled or failed to: its card follows.</summary>
    public void OnCompiled(string path)
    {
        ShaderItem? item = ItemFor(path);
        if (item == null || item.IsWeb || _app.Status.IsCompiling(path))
        {
            return;
        }
        bool hasError = _app.Status.HasError(path);
        if (hasError)
        {
            item.IsLoading = false;
            item.Thumb = null;
        }
        else if (item.HasError || item.Thumb == null)
        {
            _thumbnails.Ensure(item);
        }
        item.HasError = hasError;
    }

    private void UpdateCost(ShaderItem item)
    {
        if (item.IsWeb)
        {
            return;
        }
        float cost = (float)_app.Shaders.KnownCost(item.Path, false);
        item.Cost = cost > 0 ? cost : -1;
        item.TooHeavy = HeavyCheck.IsTooHeavy(_app, item.Path);
        if (cost <= 0 && !item.HasError && _costRequested.Add(item.Path))
        {
            _app.RequestCost(item.Path);   // once per window: the badge appears when the measurement is in
        }
    }

    private static List<ShaderItem> Sort(List<ShaderItem> items, LibrarySort sort)
    {
        var byName = StringComparer.OrdinalIgnoreCase;
        return sort switch
        {
            LibrarySort.Recent => items.OrderByDescending(item => item.LastUsed).ThenBy(item => item.Name, byName).ToList(),
            LibrarySort.Cost => items.OrderBy(item => item.Cost < 0 ? float.MaxValue : item.Cost).ThenBy(item => item.Name, byName).ToList(),
            _ => items.OrderBy(item => item.Name, byName).ToList(),
        };
    }

    private bool Matches(ShaderItem item)
    {
        bool inCategory = _chip switch
        {
            AllChip => !item.TerminalOnly,
            FavouritesChip => item.IsFavourite,
            ShaderMetadata.TerminalOnlyTag => item.TerminalOnly,
            _ => item.Tags.Contains(_chip) && !item.TerminalOnly,
        };
        string query = _view.Search.Text.Trim();
        bool found = query.Length == 0 || item.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
            || item.Tags.Any(tag => tag.Contains(query, StringComparison.OrdinalIgnoreCase));
        return inCategory && found;
    }

    private void BuildChips()
    {
        _view.Chips.Children.Clear();
        foreach (var (id, label) in ChipDefinitions)
        {
            bool always = id == AllChip || id == FavouritesChip || id == _chip;
            if (!always && !_items.Any(item => item.Tags.Contains(id)))
            {
                continue;
            }
            var chip = new RadioButton
            {
                Style = Theme.Style("Chip"),
                Content = label,
                GroupName = "chips",
                IsChecked = _chip == id,
                Margin = new Thickness(0, 0, 6, 6),
            };
            chip.Click += (_, _) =>
            {
                _chip = id;
                ApplyFilter();
            };
            _view.Chips.Children.Add(chip);
        }
    }

    private void ApplyFilter()
    {
        WithoutSelectionEvents(() =>
        {
            _visible.Refresh();
            _view.Gallery.SelectedItem = SelectedItem != null && Matches(SelectedItem) ? SelectedItem : null;
        });
        if (_view.Gallery.SelectedItem == null)
        {
            Select(VisiblePaths.FirstOrDefault());
        }
        UpdateEmptyState();
    }

    private void UpdateEmptyState()
    {
        bool empty = _visible.IsEmpty;
        _view.Empty.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        if (!empty)
        {
            return;
        }
        bool searching = _view.Search.Text.Trim().Length > 0;
        bool filtered = searching || _chip != AllChip;
        _view.EmptyTitle.Text = filtered ? "No matching shaders" : "Nothing here yet";
        _view.EmptyBtn.Visibility = filtered ? Visibility.Collapsed : Visibility.Visible;
        if (searching)
        {
            _view.EmptyText.Text = "Try a different search.";
        }
        else if (_chip == FavouritesChip)
        {
            _view.EmptyText.Text = "Star a wallpaper to keep it here.";
        }
        else
        {
            _view.EmptyText.Text = filtered ? "Nothing in this category." : "Add a .hlsl pixel shader or drop files onto this window.";
        }
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_selecting)
        {
            return;
        }
        var item = _view.Gallery.SelectedItem as ShaderItem;
        if (item != null)
        {
            _view.Gallery.ScrollIntoView(item);
        }
        SetSelected(item?.Path);
    }

    private void SetSelected(string? path)
    {
        if (string.Equals(path, Selected, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        Selected = path;
        SelectionChanged?.Invoke(path);
    }

    /// <summary>Changes the list or its selection without reporting a selection made by the user.</summary>
    private void WithoutSelectionEvents(Action change)
    {
        _selecting = true;
        try
        {
            change();
        }
        finally
        {
            _selecting = false;
        }
    }

    private static T? FindChild<T>(DependencyObject parent)
        where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match)
            {
                return match;
            }
            if (FindChild<T>(child) is T nested)
            {
                return nested;
            }
        }
        return null;
    }
}
