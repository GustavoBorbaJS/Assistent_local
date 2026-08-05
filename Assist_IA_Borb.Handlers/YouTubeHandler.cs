using System.Diagnostics;
using Assist_IA_Borb.Core.Handlers;

namespace Assist_IA_Borb.Handlers;

public sealed class YouTubeHandler : ICommandHandler
{
    public string IntentKey => "youtube";

    public Task ExecuteAsync(string query, CancellationToken cancellationToken = default)
    {
        var url = $"https://www.youtube.com/results?search_query={Uri.EscapeDataString(query)}";
        OpenInDefaultBrowser(url);
        return Task.CompletedTask;
    }

    internal static void OpenInDefaultBrowser(string url)
    {
        // UseShellExecute = true é o que garante que o Windows abra o navegador
        // configurado como padrão pelo usuário, não um navegador fixo no código.
        Process.Start(new ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        });
    }
}
