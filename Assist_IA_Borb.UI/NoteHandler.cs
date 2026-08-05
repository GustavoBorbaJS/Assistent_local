using System.Windows;
using Assist_IA_Borb.Core.Handlers;

// Aliases explícitos: com UseWindowsForms habilitado (necessário pro NotifyIcon da
// bandeja), os nomes Application e MessageBox existem em WPF e em WinForms ao mesmo
// tempo. Estes aliases deixam claro que aqui usamos sempre a versão do WPF.
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace Assist_IA_Borb.UI;

/// <summary>
/// Cria uma anotação flutuante na área de trabalho com o texto ditado/digitado.
/// Fica no projeto de UI (não em Handlers) porque precisa criar uma janela WPF -
/// os outros handlers (YouTube, Agenda, Sistema) não têm essa dependência.
/// </summary>
public sealed class NoteHandler : ICommandHandler
{
    private static int _cascadeCount;

    public string IntentKey => "anotacao";

    public Task ExecuteAsync(string query, CancellationToken cancellationToken = default)
    {
        // ExecuteAsync pode ser chamado a partir do evento de voz reconhecida,
        // que dispara fora da thread de UI - por isso o Dispatcher.Invoke aqui.
        Application.Current.Dispatcher.Invoke(() =>
        {
            var note = new StickyNoteWindow(query);

            // Cada nova nota aparece um pouco deslocada da anterior (efeito cascata),
            // pra não abrir todas exatamente empilhadas no mesmo lugar.
            var workArea = SystemParameters.WorkArea;
            var offset = (_cascadeCount % 6) * 28;
            _cascadeCount++;

            note.Left = workArea.Left + 80 + offset;
            note.Top = workArea.Top + 80 + offset;

            note.Show();
        });

        return Task.CompletedTask;
    }
}
