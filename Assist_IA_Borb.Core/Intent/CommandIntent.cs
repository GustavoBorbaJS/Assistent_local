namespace Assist_IA_Borb.Core.Intent;

/// <summary>
/// Resultado da classificação de intenção: qual ação executar e com qual parâmetro.
/// </summary>
public sealed class CommandIntent
{
    /// <summary>
    /// Chave da intenção reconhecida. Ex: "youtube", "agenda", "pesquisa", "sistema", "desconhecido".
    /// </summary>
    public string IntentKey { get; init; } = "desconhecido";

    /// <summary>
    /// Texto já "limpo" para passar ao handler (ex: só o termo de busca, sem "coloca um vídeo de").
    /// </summary>
    public string Query { get; init; } = string.Empty;

    /// <summary>
    /// Confiança da classificação (0 a 1). Usado para decidir se pede confirmação ao usuário.
    /// </summary>
    public double Confidence { get; init; }

    /// <summary>
    /// Texto original falado/digitado, guardado para log e fallback.
    /// </summary>
    public string RawInput { get; init; } = string.Empty;
}
