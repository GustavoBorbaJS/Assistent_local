using Microsoft.CognitiveServices.Speech;

namespace Assist_IA_Borb.Speech;

public sealed class AzureVoiceRecognitionService : IVoiceRecognitionService, IAsyncDisposable
{
    private readonly SpeechConfig _speechConfig;
    private SpeechRecognizer? _recognizer;

    public event Action<string>? OnRecognized;
    public event Action<string>? OnRecognizing;
    public event Action<string>? OnError;

    public bool IsListening { get; private set; }

    public AzureVoiceRecognitionService(string subscriptionKey, string region)
    {
        _speechConfig = SpeechConfig.FromSubscription(subscriptionKey, region);
        _speechConfig.SpeechRecognitionLanguage = "pt-BR";
    }

    public async Task StartListeningAsync(CancellationToken cancellationToken = default)
    {
        if (IsListening)
        {
            return;
        }

        _recognizer = new SpeechRecognizer(_speechConfig);

        _recognizer.Recognizing += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Result.Text))
            {
                OnRecognizing?.Invoke(e.Result.Text);
            }
        };

        _recognizer.Recognized += (_, e) =>
        {
            if (e.Result.Reason == ResultReason.RecognizedSpeech && !string.IsNullOrWhiteSpace(e.Result.Text))
            {
                OnRecognized?.Invoke(e.Result.Text);
            }
        };

        _recognizer.Canceled += (_, e) =>
        {
            OnError?.Invoke($"Reconhecimento cancelado: {e.Reason} - {e.ErrorDetails}");
        };

        try
        {
            await _recognizer.StartContinuousRecognitionAsync();
            IsListening = true;
        }
        catch (ApplicationException ex) when (ex.Message.Contains("SPXERR_MIC_NOT_AVAILABLE"))
        {
            _recognizer.Dispose();
            _recognizer = null;
            IsListening = false;
            OnError?.Invoke(
                "Não encontrei um microfone disponível neste computador. " +
                "Use o ícone de teclado para digitar seu comando.");
        }
        catch (Exception ex)
        {
            _recognizer?.Dispose();
            _recognizer = null;
            IsListening = false;
            OnError?.Invoke($"Não consegui ativar o microfone: {ex.Message}");
        }
    }

    public async Task StopListeningAsync()
    {
        if (_recognizer is null || !IsListening)
        {
            return;
        }

        await _recognizer.StopContinuousRecognitionAsync();
        _recognizer.Dispose();
        _recognizer = null;
        IsListening = false;
    }

    public async ValueTask DisposeAsync()
    {
        await StopListeningAsync();
    }
}
