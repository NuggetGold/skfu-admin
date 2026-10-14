using System.Windows;

namespace SkfuAdmin.Views;

public static class ConfirmDialog
{
    public static bool Ask(Window owner, string text) => MessageBox.Show(owner, text, "Подтверждение", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
}
