using System.Windows;
using System.Windows.Input;

// Aliases explícitos por causa do UseWindowsForms habilitado no projeto (NotifyIcon).
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using MouseButtonState = System.Windows.Input.MouseButtonState;

namespace Assist_IA_Borb.UI;

public partial class StickyNoteWindow : Window
{
    public StickyNoteWindow(string initialText)
    {
        InitializeComponent();
        NoteTextBox.Text = initialText;
        NoteTextBox.Focus();
        NoteTextBox.CaretIndex = NoteTextBox.Text.Length;
    }

    // Arrastar a janela pela barra superior - DragMove() é o jeito padrão do WPF
    // pra janelas sem WindowStyle (sem a barra de título nativa do Windows).
    private void DragHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
