using System.Diagnostics;
using Assist_IA_Borb.Core;
using Assist_IA_Borb.Core.Handlers;

namespace Assist_IA_Borb.Handlers;

/// <summary>
/// Abre painéis de configuração do Windows (ms-settings:), aplicativos conhecidos
/// específicos (VS Code, Visual Studio, Antigravity, AnyDesk, Parsec, WhatsApp) ou
/// tenta iniciar qualquer outro app pelo nome, com base no que o classificador extraiu.
///
/// Ordem de resolução, da mais rápida/confiável pra mais genérica: cache de buscas
/// anteriores -> configuração do Windows -> apps conhecidos com lógica dedicada ->
/// registro "App Paths" do Windows -> nome literal -> varredura do PATH -> atalho do
/// Menu Iniciar -> pesquisa na web como último recurso (com aviso explícito pro usuário
/// de que o app não foi encontrado instalado).
/// </summary>
public sealed class SystemSettingsHandler : ICommandHandler
{
    public string IntentKey => "sistema";

    // Mapa de termos comuns em fala natural -> URI de configuração do Windows.
    private static readonly Dictionary<string, string> SettingsMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["wifi"] = "ms-settings:network-wifi",
        ["internet"] = "ms-settings:network-wifi",
        ["volume"] = "ms-settings:sound",
        ["som"] = "ms-settings:sound",
        ["tela"] = "ms-settings:display",
        ["brilho"] = "ms-settings:display",
        ["impressora"] = "ms-settings:printers",
        ["bluetooth"] = "ms-settings:bluetooth",
        ["mouse"] = "ms-settings:mousetouchpad",
        ["teclado"] = "ms-settings:easeofaccess-keyboard",
        ["atualização"] = "ms-settings:windowsupdate",
        ["atualizacao"] = "ms-settings:windowsupdate",
    };

    // Apps de uso pessoal com lógica dedicada de localização. Cada um tenta,
    // em ordem: caminho fixo conhecido -> comando de PATH -> atalho do Menu Iniciar.
    private static readonly List<KnownApp> KnownApps =
    [
        new KnownApp(
            CanonicalKey: "vscode",
            Aliases: ["vscode", "vs code", "visual studio code", "código", "codigo"],
            CandidatePaths: [@"%LOCALAPPDATA%\Programs\Microsoft VS Code\Code.exe"],
            ProcessCommand: "code",
            ShortcutSearchTerms: ["Visual Studio Code"]),

        new KnownApp(
            CanonicalKey: "visual_studio",
            Aliases: ["visual studio", "devenv", "vs2022", "vs 2022"],
            CandidatePaths: [],
            ProcessCommand: null,
            ShortcutSearchTerms: ["Visual Studio 2022", "Visual Studio"],
            SpecialLauncher: TryLaunchVisualStudioViaVsWhere),

        new KnownApp(
            CanonicalKey: "antigravity",
            Aliases: ["antigravity", "google antigravity"],
            CandidatePaths: [@"%LOCALAPPDATA%\Programs\Antigravity\Antigravity.exe"],
            ProcessCommand: null,
            ShortcutSearchTerms: ["Antigravity"]),

        new KnownApp(
            CanonicalKey: "anydesk",
            Aliases: ["anydesk", "any desk"],
            CandidatePaths:
            [
                @"%ProgramFiles(x86)%\AnyDesk\AnyDesk.exe",
                @"%ProgramFiles%\AnyDesk\AnyDesk.exe",
                @"%APPDATA%\AnyDesk\AnyDesk.exe"
            ],
            ProcessCommand: null,
            ShortcutSearchTerms: ["AnyDesk"]),

        new KnownApp(
            CanonicalKey: "parsec",
            Aliases: ["parsec"],
            CandidatePaths:
            [
                @"%LOCALAPPDATA%\Parsec\parsecd.exe",
                @"%ProgramFiles%\Parsec\parsecd.exe"
            ],
            ProcessCommand: null,
            ShortcutSearchTerms: ["Parsec"]),

        new KnownApp(
            CanonicalKey: "claude_cli",
            Aliases: ["cli do claude", "claude cli", "claude code cli", "terminal do claude", "linha de comando do claude"],
            CandidatePaths: [],
            ProcessCommand: null,
            ShortcutSearchTerms: [],
            SpecialLauncher: TryLaunchClaudeCli),

        // Fica DEPOIS do claude_cli na lista de propósito: como o alias "claude" sozinho
        // é substring de "cli do claude" também, se esse item viesse antes, "abre o cli
        // do claude" acabaria batendo aqui em vez de no claude_cli. A ordem da lista
        // decide qual KnownApp "ganha" quando os dois têm alias compatível.
        new KnownApp(
            CanonicalKey: "claude_desktop",
            Aliases: ["claude", "aplicativo claude", "app claude", "claude desktop", "abre o claude"],
            CandidatePaths: [@"%LOCALAPPDATA%\Programs\Claude\Claude.exe"],
            ProcessCommand: null,
            ShortcutSearchTerms: ["Claude"]),

        new KnownApp(
            CanonicalKey: "whatsapp",
            Aliases: ["whatsapp", "whats app", "whatsapp desktop"],
            CandidatePaths: [@"%LOCALAPPDATA%\WhatsApp\WhatsApp.exe"],
            ProcessCommand: null,
            ShortcutSearchTerms: ["WhatsApp"],
            UriScheme: "whatsapp://"),
    ];

    public Task ExecuteAsync(string query, CancellationToken cancellationToken = default)
    {
        var normalized = query.Trim();

        // 1) Configurações do Windows
        foreach (var (keyword, uri) in SettingsMap)
        {
            if (normalized.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                Process.Start(new ProcessStartInfo { FileName = uri, UseShellExecute = true });
                return Task.CompletedTask;
            }
        }

        // 2) Cache de uma busca anterior bem-sucedida pra esse mesmo termo - evita
        // repetir a varredura de PATH/Menu Iniciar toda vez que o mesmo app é pedido.
        var cached = AppLocationCache.TryGet(normalized);
        if (cached is not null && TryStart(cached))
        {
            return Task.CompletedTask;
        }

        // 3) Apps conhecidos de uso pessoal
        var matchedApp = KnownApps.FirstOrDefault(app =>
            string.Equals(app.CanonicalKey, normalized, StringComparison.OrdinalIgnoreCase) ||
            app.Aliases.Any(alias => normalized.Contains(alias, StringComparison.OrdinalIgnoreCase)));

        if (matchedApp is not null && TryLaunchKnownApp(matchedApp, normalized))
        {
            return Task.CompletedTask;
        }

        // 4) Registro "App Paths" do Windows - cobre a maioria dos apps de terceiros
        // instalados normalmente (Chrome, Spotify, Steam, Discord, VLC, Office, etc.)
        // sem precisar cadastrar cada um manualmente na lista de apps conhecidos.
        var registryMatch = AppPathsFinder.FindByNameContains(normalized);
        if (registryMatch is not null && TryStart(registryMatch))
        {
            AppLocationCache.Save(normalized, registryMatch);
            return Task.CompletedTask;
        }

        // 5) Qualquer outro app - tenta abrir pelo nome literal (cobre comandos já
        // registrados exatamente no PATH do sistema)
        if (TryStart(normalized))
        {
            return Task.CompletedTask;
        }

        // 6) Varre todas as pastas do PATH atrás de um executável cujo nome CONTENHA
        // o termo pedido - cobre ferramentas de linha de comando com nome parecido,
        // mas não idêntico, ao que a pessoa falou (ex: pediu "claude" e o executável
        // real chama-se claude.exe numa pasta custom do PATH).
        var pathMatch = PathExecutableFinder.FindByNameContains(normalized);
        if (pathMatch is not null && TryStart(pathMatch))
        {
            AppLocationCache.Save(normalized, pathMatch);
            return Task.CompletedTask;
        }

        // 7) Última tentativa: procurar um atalho no Menu Iniciar com esse nome -
        // cobre apps com interface gráfica, que normalmente não entram no PATH.
        var shortcutTarget = ShortcutFinder.FindByNameContains([normalized]);
        if (shortcutTarget is not null && TryStart(shortcutTarget))
        {
            AppLocationCache.Save(normalized, shortcutTarget);
            return Task.CompletedTask;
        }

        // Nada funcionou: avisa a pessoa explicitamente (em vez de silenciosamente cair
        // pra web) e pesquisa o termo no navegador padrão - melhor mostrar algo útil do
        // que travar/dar erro.
        AssistantFeedback.Notify(
            $"Não encontrei o aplicativo \"{normalized}\" instalado. Pesquisando na web sobre isso...");

        var fallbackUrl = $"https://www.google.com/search?q={Uri.EscapeDataString(normalized)}";
        YouTubeHandler.OpenInDefaultBrowser(fallbackUrl);
        return Task.CompletedTask;
    }

    private static bool TryLaunchKnownApp(KnownApp app, string searchTermForCache)
    {
        if (app.SpecialLauncher is not null && app.SpecialLauncher())
        {
            return true;
        }

        if (app.UriScheme is not null && TryStart(app.UriScheme))
        {
            return true;
        }

        foreach (var candidate in app.CandidatePaths)
        {
            var expanded = Environment.ExpandEnvironmentVariables(candidate);
            if (File.Exists(expanded) && TryStart(expanded))
            {
                AppLocationCache.Save(searchTermForCache, expanded);
                return true;
            }
        }

        if (app.ProcessCommand is not null && TryStart(app.ProcessCommand))
        {
            return true;
        }

        var registryMatch = AppPathsFinder.FindByNameContains(app.CanonicalKey);
        if (registryMatch is not null && TryStart(registryMatch))
        {
            AppLocationCache.Save(searchTermForCache, registryMatch);
            return true;
        }

        var shortcutTarget = ShortcutFinder.FindByNameContains(app.ShortcutSearchTerms);
        if (shortcutTarget is not null && TryStart(shortcutTarget))
        {
            AppLocationCache.Save(searchTermForCache, shortcutTarget);
            return true;
        }

        return false;
    }

    private static bool TryStart(string fileName)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = fileName, UseShellExecute = true });
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Visual Studio não tem um caminho fixo previsível (varia por edição/versão),
    /// então usamos o vswhere.exe - ferramenta oficial da Microsoft instalada junto
    /// com qualquer Visual Studio 2017+ - pra perguntar onde ele está de verdade.
    /// </summary>
    private static bool TryLaunchVisualStudioViaVsWhere()
    {
        var vswherePath = Environment.ExpandEnvironmentVariables(
            @"%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe");

        if (!File.Exists(vswherePath))
        {
            return false;
        }

        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = vswherePath,
                Arguments = "-latest -property productPath",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (process is null)
            {
                return false;
            }

            var devenvPath = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(5000);

            return !string.IsNullOrWhiteSpace(devenvPath)
                && File.Exists(devenvPath)
                && TryStart(devenvPath);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// O Claude Code CLI é uma ferramenta interativa de linha de comando - não faz
    /// sentido só "executar e fechar", precisa abrir um terminal de verdade com ele
    /// rodando dentro. Usa /K (não /C) pra manter a janela do cmd aberta depois que
    /// o comando `claude` iniciar, exatamente como se o usuário tivesse digitado
    /// `claude` manualmente num terminal novo.
    ///
    /// Tenta primeiro o caminho fixo de instalação nativa do Claude Code
    /// (%USERPROFILE%\.local\bin\claude.exe) em vez de confiar cegamente no PATH -
    /// o instalador nativo avisa explicitamente que essa pasta pode não estar no
    /// PATH do usuário, então não dá pra assumir que o comando "claude" sozinho
    /// vai funcionar num cmd novo.
    /// </summary>
    private static bool TryLaunchClaudeCli()
    {
        var knownPath = Environment.ExpandEnvironmentVariables(@"%USERPROFILE%\.local\bin\claude.exe");
        var command = File.Exists(knownPath) ? $"\"{knownPath}\"" : "claude";

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/K {command}",
                UseShellExecute = true
            });
            return true;
        }
        catch
        {
            return false;
        }
    }

    private sealed record KnownApp(
        string CanonicalKey,
        string[] Aliases,
        string[] CandidatePaths,
        string? ProcessCommand,
        string[] ShortcutSearchTerms,
        string? UriScheme = null,
        Func<bool>? SpecialLauncher = null);
}
