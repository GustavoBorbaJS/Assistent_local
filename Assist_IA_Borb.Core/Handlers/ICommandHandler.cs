namespace Assist_IA_Borb.Core.Handlers;

/// <summary>
/// Contrato que toda ação executável pelo assistente deve implementar.
/// Cada handler cuida de UM tipo de intenção (YouTube, pesquisa, agenda, configurações do sistema, etc).
/// </summary>
public interface ICommandHandler
{
    /// <summary>
    /// Nome curto da intenção que este handler resolve.
    /// Deve bater com o valor retornado pelo classificador de intenção (ex: "youtube", "agenda", "pesquisa", "sistema").
    /// </summary>
    string IntentKey { get; }

    /// <summary>
    /// Executa a ação com base no texto/parâmetro extraído do comando do usuário.
    /// Ex: para IntentKey = "youtube", o query seria "vídeo de música sertaneja".
    /// </summary>
    Task ExecuteAsync(string query, CancellationToken cancellationToken = default);
}
