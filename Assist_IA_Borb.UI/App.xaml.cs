using System.Windows;
using Assist_IA_Borb.Core;
using Assist_IA_Borb.Core.Handlers;
using Assist_IA_Borb.Core.Intent;
using Assist_IA_Borb.Handlers;
using Assist_IA_Borb.Speech;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

// Alias explícito por causa do UseWindowsForms habilitado no projeto (NotifyIcon da
// bandeja) - sem isso, "Application" fica ambíguo entre WPF e WinForms.
using Application = System.Windows.Application;

namespace Assist_IA_Borb.UI;

public partial class App : Application
{
    private ServiceProvider? _serviceProvider;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // appsettings.json guarda só valores NÃO sensíveis (ex: URL do backend proxy, região da Azure).
        // Segredos (subscription key da Azure Speech, token de instalação) vêm de User Secrets
        // em desenvolvimento, e de variável de ambiente em produção. NUNCA committar chave real.
        // em desenvolvimento, e de variável de ambiente em produção. NUNCA committar chave real.
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .AddUserSecrets<App>(optional: true)
            .AddEnvironmentVariables(prefix: "ASSIST_IA_BORB_")
            .Build();

        var services = new ServiceCollection();
        ConfigureServices(services, configuration);
        _serviceProvider = services.BuildServiceProvider();

        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    private static void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(configuration);

        // --- Voz (Azure Speech, trial) ---
        var speechKey = configuration["Azure:SpeechSubscriptionKey"]
            ?? throw new InvalidOperationException(
                "Azure:SpeechSubscriptionKey não configurada. Use 'dotnet user-secrets' em dev " +
                "ou variável de ambiente ASSIST_IA_BORB_Azure__SpeechSubscriptionKey em produção.");
        var speechRegion = configuration["Azure:SpeechRegion"] ?? "eastus";

        services.AddSingleton<IVoiceRecognitionService>(
            new AzureVoiceRecognitionService(speechKey, speechRegion));

        // --- Classificador de intenção ---
        // ATIVO AGORA: 100% local, baseado em regras/palavras-chave - sem custo,
        // sem depender de nenhuma API paga nem de internet pra classificar.
        services.AddSingleton<IIntentClassifier, LocalKeywordIntentClassifier>();

        // GUARDADO PRA REATIVAR DEPOIS: classificação via LLM através do Proxy
        // (Assist_IA_Borb.Proxy). O código continua no projeto, intacto - se algum
        // dia fizer sentido reativar (ex: comandos mais soltos, sem depender de
        // palavras-chave fixas), é só comentar o AddSingleton acima e descomentar
        // o bloco abaixo.
        //
        // var proxyBaseUrl = configuration["Proxy:BaseUrl"]
        //     ?? throw new InvalidOperationException("Proxy:BaseUrl não configurada em appsettings.json.");
        // var installationToken = configuration["Proxy:InstallationToken"] ?? string.Empty;
        //
        // services.AddHttpClient("proxy", client =>
        // {
        //     client.BaseAddress = new Uri(proxyBaseUrl);
        //     client.Timeout = TimeSpan.FromSeconds(8);
        // });
        //
        // services.AddSingleton<IIntentClassifier>(sp =>
        // {
        //     var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient("proxy");
        //     return new ProxyIntentClassifier(httpClient, installationToken);
        // });

        // --- Handlers de comando ---
        services.AddSingleton<ICommandHandler, YouTubeHandler>();
        services.AddSingleton<ICommandHandler, GoogleCalendarHandler>();
        services.AddSingleton<ICommandHandler, SystemSettingsHandler>();
        services.AddSingleton<ICommandHandler, NoteHandler>();
        services.AddSingleton<GitSyncHandler>(); // também usado direto pelo botão dedicado de Git
        services.AddSingleton<ICommandHandler>(sp => sp.GetRequiredService<GitSyncHandler>());
        services.AddSingleton<WebSearchHandler>(); // também usado como fallback explícito
        services.AddSingleton<ICommandHandler>(sp => sp.GetRequiredService<WebSearchHandler>());

        services.AddSingleton(sp => new CommandRouter(
            sp.GetRequiredService<IIntentClassifier>(),
            sp.GetServices<ICommandHandler>(),
            fallbackHandler: sp.GetRequiredService<WebSearchHandler>()));

        services.AddTransient<MainWindow>();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _serviceProvider?.Dispose();
        base.OnExit(e);
    }
}
