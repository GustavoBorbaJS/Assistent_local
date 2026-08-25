namespace Assist_IA_Borb.Core;

/// <summary>
/// Canal simples pra handlers "de fundo" (sem referência direta à janela) avisarem
/// a UI sobre o resultado de uma ação assíncrona - o mesmo padrão que o AlarmHandler
/// já usava com seu próprio evento OnResult, só que compartilhado, pra não precisar
/// injetar cada novo handler no construtor da MainWindow só pra ligar um evento.
/// </summary>
public static class AssistantFeedback
{
    public static event Action<string>? Raised;

    public static void Notify(string message) => Raised?.Invoke(message);
}
