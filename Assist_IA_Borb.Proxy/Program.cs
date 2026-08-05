// Backend PROXY - roda no servidor (Azure App Service free tier, Fly.io, etc), NUNCA no cliente.
// É aqui, e só aqui, que a chave real da API (DeepSeek) deve existir.
//
// Fluxo:
// App desktop -> POST /api/classify-intent (com X-Installation-Token) -> este backend
// -> chama a API do DeepSeek usando a chave guardada em variável de ambiente do servidor
// -> devolve só o JSON { intent, query, confidence } pro cliente.
//
// Por que DeepSeek aqui: essa tarefa é só classificação de intenção em 4 categorias
// fixas (youtube, agenda, sistema, pesquisa) - um modelo caro tipo Claude/GPT-4 é
// overkill e custa muito mais por chamada. O DeepSeek (deepseek-chat) resolve isso
// com uma fração do custo, o que importa bastante num projeto de portfólio grátis
// com potencialmente muitos usuários fazendo várias chamadas por dia.
//
// O cliente nunca vê a chave do DeepSeek. Na pior hipótese, alguém descompila o app
// e acha só a URL deste proxy - por isso o rate limiting e o token de instalação abaixo.

using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpClient("deepseek", client =>
{
    client.BaseAddress = new Uri("https://api.deepseek.com/");
    client.Timeout = TimeSpan.FromSeconds(15);
});

var app = builder.Build();

// Rate limiting simples por token de instalação (em memória; trocar por Redis/DB se escalar
// pra múltiplas instâncias do servidor).
var requestCounts = new ConcurrentDictionary<string, (int Count, DateTime WindowStart)>();
const int MaxRequestsPerHour = 60;

const string SystemPrompt = """
    Você classifica comandos em português (Brasil) ditos por pessoas idosas para um assistente
    de computador. Responda APENAS com um objeto JSON, sem nenhum texto antes ou depois,
    no formato exato: {"intent": "...", "query": "...", "confidence": 0.0}

    Valores possíveis para "intent":
    - "youtube": pedidos para assistir/tocar vídeo, música, filme no YouTube.
    - "agenda": pedidos para marcar, lembrar ou consultar compromisso/hora/data.
    - "sistema": pedidos para abrir configurações do Windows (wifi, volume, tela, impressora,
      bluetooth, mouse, teclado, atualização) ou abrir um programa/app do computador.
    - "pesquisa": qualquer outra pergunta ou pedido de busca geral.

    "query" é o termo de busca já limpo (sem as palavras de comando, ex: "coloca um vídeo de" vira
    só o assunto do vídeo). "confidence" é sua confiança de 0 a 1 na classificação.
    """;

app.MapPost("/api/classify-intent", async (HttpRequest request, IHttpClientFactory httpClientFactory) =>
{
    var token = request.Headers["X-Installation-Token"].ToString();
    if (string.IsNullOrWhiteSpace(token))
    {
        return Results.Unauthorized();
    }

    if (!IsWithinRateLimit(token))
    {
        return Results.StatusCode(StatusCodes.Status429TooManyRequests);
    }

    var body = await request.ReadFromJsonAsync<ClassifyRequest>();
    if (body is null || string.IsNullOrWhiteSpace(body.Text))
    {
        return Results.BadRequest();
    }

    // A chave do DeepSeek vem SÓ de variável de ambiente do servidor - nunca de appsettings versionado.
    var deepSeekApiKey = Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY")
        ?? throw new InvalidOperationException(
            "DEEPSEEK_API_KEY não configurada no servidor. Defina a variável de ambiente antes de rodar.");

    var client = httpClientFactory.CreateClient("deepseek");
    client.DefaultRequestHeaders.Authorization =
        new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", deepSeekApiKey);

    var deepSeekRequest = new DeepSeekChatRequest(
        Model: "deepseek-chat",
        Messages:
        [
            new DeepSeekMessage("system", SystemPrompt),
            new DeepSeekMessage("user", body.Text)
        ],
        ResponseFormat: new DeepSeekResponseFormat("json_object"),
        Temperature: 0.1,
        MaxTokens: 150);

    HttpResponseMessage deepSeekResponse;
    try
    {
        deepSeekResponse = await client.PostAsJsonAsync("chat/completions", deepSeekRequest);
        deepSeekResponse.EnsureSuccessStatusCode();
    }
    catch (Exception ex)
    {
        // Não derruba o app do usuário por causa disso - devolve uma intenção de fallback
        // (pesquisa) pro cliente decidir o que fazer, e loga o erro real no servidor.
        app.Logger.LogError(ex, "Falha ao chamar a API do DeepSeek");
        return Results.Ok(new ClassifyResponse("pesquisa", body.Text, 0));
    }

    var completion = await deepSeekResponse.Content.ReadFromJsonAsync<DeepSeekChatCompletion>();
    var rawContent = completion?.Choices.FirstOrDefault()?.Message.Content;

    if (string.IsNullOrWhiteSpace(rawContent))
    {
        return Results.Ok(new ClassifyResponse("pesquisa", body.Text, 0));
    }

    try
    {
        var parsed = JsonSerializer.Deserialize<ClassifyResponse>(
            rawContent, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        return Results.Ok(parsed ?? new ClassifyResponse("pesquisa", body.Text, 0));
    }
    catch (JsonException ex)
    {
        // O modelo às vezes foge do formato - registra pra ajustar o prompt depois,
        // mas não quebra a experiência do usuário.
        app.Logger.LogWarning(ex, "Resposta do DeepSeek fora do formato esperado: {Raw}", rawContent);
        return Results.Ok(new ClassifyResponse("pesquisa", body.Text, 0));
    }
});

bool IsWithinRateLimit(string token)
{
    var now = DateTime.UtcNow;
    var entry = requestCounts.AddOrUpdate(
        token,
        _ => (1, now),
        (_, existing) => now - existing.WindowStart > TimeSpan.FromHours(1)
            ? (1, now)
            : (existing.Count + 1, existing.WindowStart));

    return entry.Count <= MaxRequestsPerHour;
}

app.Run();

internal sealed record ClassifyRequest(string Text);

internal sealed record ClassifyResponse(
    [property: JsonPropertyName("intent")] string Intent,
    [property: JsonPropertyName("query")] string Query,
    [property: JsonPropertyName("confidence")] double Confidence);

// --- Modelos da API do DeepSeek (formato compatível com o padrão OpenAI chat/completions) ---

internal sealed record DeepSeekChatRequest(
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("messages")] DeepSeekMessage[] Messages,
    [property: JsonPropertyName("response_format")] DeepSeekResponseFormat ResponseFormat,
    [property: JsonPropertyName("temperature")] double Temperature,
    [property: JsonPropertyName("max_tokens")] int MaxTokens);

internal sealed record DeepSeekMessage(
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("content")] string Content);

internal sealed record DeepSeekResponseFormat(
    [property: JsonPropertyName("type")] string Type);

internal sealed record DeepSeekChatCompletion(
    [property: JsonPropertyName("choices")] DeepSeekChoice[] Choices);

internal sealed record DeepSeekChoice(
    [property: JsonPropertyName("message")] DeepSeekMessage Message);
