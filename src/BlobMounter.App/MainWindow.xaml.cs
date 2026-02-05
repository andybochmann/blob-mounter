using System.Windows;
using System.Windows.Controls;
using BlobMounter.App.ViewModels;

namespace BlobMounter.App;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        viewModel.SettingsLoaded += key =>
        {
            AccountKeyBox.Password = key;
        };
    }

    private void AccountKeyBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.AccountKey = ((PasswordBox)sender).Password;
        }
    }
}
