using System.Windows;
using System.Windows.Controls;
using SynologyPhotoFrame.Helpers;
using SynologyPhotoFrame.ViewModels;

namespace SynologyPhotoFrame.Views;

public partial class LoginView : UserControl
{
    public LoginView()
    {
        InitializeComponent();
        Loaded += LoginView_Loaded;
    }

    private void LoginView_Loaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is LoginViewModel vm && !string.IsNullOrEmpty(vm.Password))
        {
            PasswordBox.Password = vm.Password;
        }
    }

    private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is LoginViewModel vm)
        {
            vm.Password = PasswordBox.Password;
        }
    }
}
