using Assist_IA_Borb.Core;
using Assist_IA_Borb.Core.Handlers;

namespace Assist_IA_Borb.Handlers;

/// <summary>Responde "que alarmes eu tenho" / "listar alarmes" com os alarmes ainda ativos.</summary>
public sealed class ListAlarmsHandler : ICommandHandler
{
    public string IntentKey => "listaralarmes";

    public async Task ExecuteAsync(string query, CancellationToken cancellationToken = default)
    {
        var alarms = await AlarmStore.GetActiveAsync(cancellationToken);

        if (alarms.Count == 0)
        {
            AssistantFeedback.Notify("Você não tem nenhum alarme agendado no momento.");
            return;
        }

        var lines = alarms
            .OrderBy(a => a.FirstOccurrence)
            .Select(a => string.IsNullOrEmpty(a.RecurrenceDescription)
                ? $"• \"{a.Label}\" em {a.FirstOccurrence:dd/MM 'às' HH:mm}"
                : $"• \"{a.Label}\" {a.RecurrenceDescription} às {a.FirstOccurrence:HH:mm}");

        AssistantFeedback.Notify("Seus alarmes:\n" + string.Join("\n", lines));
    }
}
