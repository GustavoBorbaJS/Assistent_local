using Assist_IA_Borb.Core.Handlers;
using Assist_IA_Borb.Core.Intent;

namespace Assist_IA_Borb.Core;

/// <summary>
/// Ponto único de entrada: recebe texto (vindo de voz transcrita ou da caixa de digitação),
/// classifica a intenção e despacha para o handler correspondente.
/// </summary>
public sealed class CommandRouter
{
    private readonly IIntentClassifier _classifier;
    private readonly IReadOnlyDictionary<string, ICommandHandler> _handlers;
    private readonly ICommandHandler _fallbackHandler;

    public event Action<string>? OnFeedback; // usado pra a UI mostrar "Entendi: abrindo YouTube..."

    public CommandRouter(
        IIntentClassifier classifier,
        IEnumerable<ICommandHandler> handlers,
        ICommandHandler fallbackHandler)
    {
        _classifier = classifier;
        _handlers = handlers.ToDictionary(h => h.IntentKey, StringComparer.OrdinalIgnoreCase);
        _fallbackHandler = fallbackHandler;
    }

    public async Task ProcessAsync(string userInput, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userInput))
        {
            return;
        }

        var intent = await _classifier.ClassifyAsync(userInput, cancellationToken);

        var handler = _handlers.TryGetValue(intent.IntentKey, out var found)
            ? found
            : _fallbackHandler;

        OnFeedback?.Invoke($"Entendi: {DescribeIntent(intent.IntentKey)} \"{intent.Query}\"");

        await handler.ExecuteAsync(intent.Query, cancellationToken);
    }

    private static string DescribeIntent(string intentKey) => intentKey switch
    {
        "youtube" => "abrindo vídeo no YouTube sobre",
        "agenda" => "agendando",
        "sistema" => "abrindo no computador",
        "anotacao" => "criando uma anotação sobre",
        "git" => "sincronizando o projeto com o GitHub",
        "pesquisa" => "pesquisando",
        _ => "pesquisando"
    };
}
