using Assist_IA_Borb.Core.Handlers;

namespace Assist_IA_Borb.Handlers;

/// <summary>
/// Handler "coringa": qualquer coisa que não bata com uma intenção específica
/// cai aqui e vira uma pesquisa no Google.
/// </summary>
public sealed class WebSearchHandler : ICommandHandler
{
    public string IntentKey => "pesquisa";

    public Task ExecuteAsync(string query, CancellationToken cancellationToken = default)
    {
        var url = $"https://www.google.com/search?q={Uri.EscapeDataString(query)}";
        YouTubeHandler.OpenInDefaultBrowser(url);
        return Task.CompletedTask;
    }
}
