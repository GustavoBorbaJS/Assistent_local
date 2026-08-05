using System.Windows;

namespace Assist_IA_Borb.UI;

public partial class RepoUrlDialog : Window
{
    public string EnteredUrl { get; private set; } = string.Empty;

    public RepoUrlDialog(string suggestedUrl)
    {
        InitializeComponent();
        UrlTextBox.Text = suggestedUrl;
        UrlTextBox.Focus();
        UrlTextBox.SelectAll();
    }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        EnteredUrl = UrlTextBox.Text.Trim();
        DialogResult = !string.IsNullOrWhiteSpace(EnteredUrl);
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
