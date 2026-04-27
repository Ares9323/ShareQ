using System.ComponentModel;
using System.IO;
using System.Windows.Media.Imaging;
using ShareQ.App.ViewModels;
using Wpf.Ui.Controls;

namespace ShareQ.App;

public partial class MainWindow : FluentWindow
{
    private readonly MainWindowViewModel _vm;

    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        _vm = viewModel;
        DataContext = viewModel;
        viewModel.PropertyChanged += OnVmPropertyChanged;
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.SelectedItemPayload))
        {
            UpdatePreviewImage();
        }
        else if (e.PropertyName == nameof(MainWindowViewModel.SelectedItem))
        {
            FocusSelectedListBoxItem();
        }
    }

    /// <summary>Bring the selected row into view AND give it keyboard focus, so arrow-down/up
    /// navigation continues from the actually-selected item (not from the top of the list, which is
    /// where WPF's internal keyboard cursor parks itself after Items.Clear/Add).</summary>
    private void FocusSelectedListBoxItem()
    {
        var item = _vm.SelectedItem;
        if (item is null) return;
        // Container generation is async on virtualized lists — defer until layout settles.
        ItemsList.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
        {
            ItemsList.ScrollIntoView(item);
            if (ItemsList.ItemContainerGenerator.ContainerFromItem(item) is System.Windows.Controls.ListBoxItem lbi)
            {
                lbi.Focus();
            }
        });
    }

    private void UpdatePreviewImage()
    {
        var bytes = _vm.SelectedItemPayload;
        if (bytes is null || bytes.Length == 0)
        {
            PreviewImage.Source = null;
            return;
        }
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.StreamSource = new MemoryStream(bytes);
        bmp.EndInit();
        bmp.Freeze();
        PreviewImage.Source = bmp;
    }
}
