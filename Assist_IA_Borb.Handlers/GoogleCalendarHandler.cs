using Assist_IA_Borb.Core.Handlers;
using Assist_IA_Borb.Core.Intent;

namespace Assist_IA_Borb.Handlers;

/// <summary>
/// Abre o Google Agenda no navegador padrão com um evento pré-preenchido, incluindo
/// data e hora REAIS interpretadas do que a pessoa falou ("15 horas de hoje" vira
/// a data de hoje do computador às 15:00).
///
/// Usa a URL de template do Google Agenda, que não exige OAuth nem chave de API -
/// se a pessoa já estiver logada na conta Google no navegador, o evento aparece
/// pronto pra confirmar, na agenda dela.
///
/// Evolução futura possível: Google Calendar API (Google.Apis.Calendar.v3) com OAuth
/// pra criar o evento direto, sem a etapa de confirmação no navegador.
/// </summary>
public sealed class GoogleCalendarHandler : ICommandHandler
{
    /// <summary>Duração assumida quando a pessoa não diz quanto tempo dura.</summary>
    private static readonly TimeSpan DefaultDuration = TimeSpan.FromHours(1);

    public string IntentKey => "agenda";

    public Task ExecuteAsync(string query, CancellationToken cancellationToken = default)
    {
        var parsed = PtBrDateTimeParser.Parse(query);

        var title = string.IsNullOrWhiteSpace(parsed.RemainingText)
            ? "Novo compromisso"
            : parsed.RemainingText;

        var url = "https://calendar.google.com/calendar/render" +
                  $"?action=TEMPLATE&text={Uri.EscapeDataString(title)}";

        if (parsed.DateTime is not null)
        {
            var start = parsed.DateTime.Value;

            // Sem hora explícita ("reunião amanhã"), agenda às 9h como padrão
            // em vez de usar o horário atual, que seria arbitrário.
            if (!parsed.HasExplicitTime)
            {
                start = start.Date.AddHours(9);
            }

            var end = start.Add(DefaultDuration);

            // Sem sufixo "Z": o Google interpreta no fuso da agenda do usuário,
            // que é o comportamento esperado aqui (a pessoa falou no horário dela).
            url += "&dates=" +
                   PtBrDateTimeParser.ToGoogleCalendarFormat(start) +
                   "/" +
                   PtBrDateTimeParser.ToGoogleCalendarFormat(end);
        }

        YouTubeHandler.OpenInDefaultBrowser(url);
        return Task.CompletedTask;
    }
}
