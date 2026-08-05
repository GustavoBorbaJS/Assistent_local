using System.Windows;

namespace Assist_IA_Borb.UI;

public partial class GitConfigDialog : Window
{
    public string EnteredName { get; private set; } = string.Empty;
    public string EnteredEmail { get; private set; } = string.Empty;

    public GitConfigDialog(string currentName, string currentEmail)
    {
        InitializeComponent();
        NameTextBox.Text = currentName;
        EmailTextBox.Text = currentEmail;
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        EnteredName = NameTextBox.Text.Trim();
        EnteredEmail = EmailTextBox.Text.Trim();
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
