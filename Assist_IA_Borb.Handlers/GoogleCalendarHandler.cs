using Assist_IA_Borb.Core.Handlers;

namespace Assist_IA_Borb.Handlers;

/// <summary>
/// Versão 1: abre o Google Agenda no navegador padrão com um evento pré-preenchido
/// via URL de template. Não exige login OAuth nem chave de API.
///
/// Evolução futura (v2): integrar a Google Calendar API (Google.Apis.Calendar.v3)
/// com OAuth2 para criar o evento diretamente, sem precisar o usuário confirmar
/// no navegador. Fica mais robusto, mas exige tela de login/consentimento do Google
/// na primeira vez — mais complexo pro público-alvo, por isso deixamos pra depois.
/// </summary>
public sealed class GoogleCalendarHandler : ICommandHandler
{
    public string IntentKey => "agenda";

    public Task ExecuteAsync(string query, CancellationToken cancellationToken = default)
    {
        // "query" aqui já vem interpretado pelo classificador, ex: "consulta médica amanhã às 15h"
        var url = "https://calendar.google.com/calendar/render" +
                   $"?action=TEMPLATE&text={Uri.EscapeDataString(query)}";

        YouTubeHandler.OpenInDefaultBrowser(url);
        return Task.CompletedTask;
    }
}
