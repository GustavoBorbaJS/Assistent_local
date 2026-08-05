namespace Assist_IA_Borb.Core.Intent;

/// <summary>
/// Responsável por transformar um texto livre (voz transcrita ou digitado)
/// em uma intenção estruturada (CommandIntent).
/// </summary>
public interface IIntentClassifier
{
    Task<CommandIntent> ClassifyAsync(string userInput, CancellationToken cancellationToken = default);
}
