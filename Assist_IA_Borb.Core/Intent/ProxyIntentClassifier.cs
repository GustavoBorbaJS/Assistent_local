using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Assist_IA_Borb.Core.Intent;

/// <summary>
/// Classificador de intenção que NÃO fala diretamente com a API do LLM.
/// Ele chama um backend fino (proxy) que você controla, que por sua vez guarda a chave
/// do LLM em segredo (variável de ambiente do servidor, Key Vault, etc).
///
/// Motivo: se o app desktop chamasse a API do LLM diretamente, a chave precisaria
/// estar embutida no cliente e qualquer decompilação (dnSpy/ILSpy) a exporia.
/// Com o proxy, o pior caso é alguém descobrir a URL do seu proxy — mitigado com
/// rate limiting e um token de instalação (ver InstallationTokenProvider).
/// </summary>
public sealed class ProxyIntentClassifier : IIntentClassifier
{
    private readonly HttpClient _httpClient;
    private readonly string _installationToken;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public ProxyIntentClassifier(HttpClient httpClient, string installationToken)
    {
        _httpClient = httpClient;
        _installationToken = installationToken;
    }

    public async Task<CommandIntent> ClassifyAsync(string userInput, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userInput))
        {
            return new CommandIntent { IntentKey = "desconhecido", RawInput = userInput };
        }

        var request = new HttpRequestMessage(HttpMethod.Post, "api/classify-intent")
        {
            Content = JsonContent.Create(new ClassifyRequest(userInput), options: JsonOptions)
        };
        request.Headers.Add("X-Installation-Token", _installationToken);

        try
        {
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<ClassifyResponse>(JsonOptions, cancellationToken);
            if (result is null)
            {
                return Fallback(userInput);
            }

            return new CommandIntent
            {
                IntentKey = result.Intent,
                Query = result.Query,
                Confidence = result.Confidence,
                RawInput = userInput
            };
        }
        catch (Exception)
        {
            // Sem internet, proxy fora do ar, etc. Degrada para uma pesquisa geral
            // em vez de travar o app - importante para o público-alvo (idosos).
            return Fallback(userInput);
        }
    }

    private static CommandIntent Fallback(string userInput) => new()
    {
        IntentKey = "pesquisa",
        Query = userInput,
        Confidence = 0,
        RawInput = userInput
    };

    private sealed record ClassifyRequest([property: JsonPropertyName("text")] string Text);

    private sealed record ClassifyResponse(
        [property: JsonPropertyName("intent")] string Intent,
        [property: JsonPropertyName("query")] string Query,
        [property: JsonPropertyName("confidence")] double Confidence);
}
