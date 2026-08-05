namespace Assist_IA_Borb.Speech;

public interface IVoiceRecognitionService
{
    /// <summary>Disparado quando um trecho de fala foi reconhecido definitivamente.</summary>
    event Action<string>? OnRecognized;

    /// <summary>Disparado durante a fala, com o texto parcial (útil pra dar feedback visual em tempo real).</summary>
    event Action<string>? OnRecognizing;

    /// <summary>Disparado em caso de erro (mic sem permissão, sem internet, etc).</summary>
    event Action<string>? OnError;

    bool IsListening { get; }

    Task StartListeningAsync(CancellationToken cancellationToken = default);

    Task StopListeningAsync();
}
