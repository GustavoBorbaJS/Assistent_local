using System.Windows;
using System.Windows.Input;
using Assist_IA_Borb.Core;
using Assist_IA_Borb.Handlers;
using Assist_IA_Borb.Speech;

// Aliases explícitos: com UseWindowsForms habilitado (necessário pro NotifyIcon da
// bandeja), vários nomes existem em WPF e em WinForms ao mesmo tempo. Estes aliases
// deixam claro que aqui usamos sempre a versão do WPF.
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace Assist_IA_Borb.UI;

public partial class MainWindow : Window
{
    private readonly CommandRouter _router;
    private readonly IVoiceRecognitionService _voiceService;
    private readonly GitSyncHandler _gitSyncHandler;
    private readonly AlarmHandler _alarmHandler;
    private System.Windows.Forms.NotifyIcon? _trayIcon;

    public MainWindow(CommandRouter router, IVoiceRecognitionService voiceService, GitSyncHandler gitSyncHandler, AlarmHandler alarmHandler)
    {
        InitializeComponent();

        _router = router;
        _voiceService = voiceService;
        _gitSyncHandler = gitSyncHandler;
        _alarmHandler = alarmHandler;

        // O alarme roda fora da thread de UI, então o resultado volta por evento.
        _alarmHandler.OnResult += message =>
            Dispatcher.Invoke(() => ShowFeedback(message));

        _router.OnFeedback += message => ShowFeedback(message);
        _voiceService.OnRecognizing += ShowPartialFeedback;
        _voiceService.OnRecognized += HandleVoiceRecognized;
        _voiceService.OnError += HandleVoiceError;

        Loaded += (_, _) =>
        {
            PositionWindowBottomRight();
            SetupTrayIcon();
        };

        Closing += (_, _) => _trayIcon?.Dispose();
    }

    // ─────────────────────────────────────────────
    //  Bandeja do sistema (system tray)
    // ─────────────────────────────────────────────

    /// <summary>
    /// Cria o ícone na bandeja do sistema (ao lado do relógio). O WPF não tem um
    /// componente nativo pra isso, então usamos o NotifyIcon do WinForms - prática
    /// comum e suportada em apps WPF.
    /// </summary>
    private void SetupTrayIcon()
    {
        var iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "app_icon.ico");

        _trayIcon = new System.Windows.Forms.NotifyIcon
        {
            Icon = System.IO.File.Exists(iconPath)
                ? new System.Drawing.Icon(iconPath)
                : System.Drawing.SystemIcons.Application,
            Visible = true,
            Text = "Assist_IA_Borb"
        };

        // Clique simples (esquerdo) traz a janela de volta.
        _trayIcon.MouseClick += (_, e) =>
        {
            if (e.Button == System.Windows.Forms.MouseButtons.Left)
            {
                RestoreFromTray();
            }
        };

        // Menu do botão direito: Abrir / Fechar.
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Abrir", null, (_, _) => RestoreFromTray());
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("Fechar", null, (_, _) => ExitApplication());
        _trayIcon.ContextMenuStrip = menu;
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) => MinimizeToTray();

    private void MinimizeToTray()
    {
        Hide();
        _trayIcon?.ShowBalloonTip(
            2000,
            "Assist_IA_Borb",
            "Continuo aqui na bandeja. Clique no ícone para me chamar de volta.",
            System.Windows.Forms.ToolTipIcon.Info);
    }

    private void RestoreFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
        Topmost = true; // reforça o "sempre no topo" ao voltar
    }

    private void ExitApplication()
    {
        _trayIcon?.Dispose();
        _trayIcon = null;
        System.Windows.Application.Current.Shutdown();
    }

    /// <summary>
    /// Posiciona a janela no canto inferior direito, um pouco acima de onde fica
    /// o relógio/hora do Windows (a barra de tarefas tem ~40-48px de altura em telas comuns,
    /// deixamos uma margem extra pra não sobrepor notificações do sistema).
    /// </summary>
    private void PositionWindowBottomRight()
    {
        var workArea = SystemParameters.WorkArea; // já desconta a barra de tarefas
        Left = workArea.Right - Width - 12;
        Top = workArea.Bottom - Height - 12;
    }

    private async void RobotButton_Click(object sender, RoutedEventArgs e)
    {
        if (_voiceService.IsListening)
        {
            await _voiceService.StopListeningAsync();
            ListeningIndicator.Visibility = Visibility.Collapsed;
            RobotImage.Source = new System.Windows.Media.Imaging.BitmapImage(
                new Uri("Assets/robot_idle.png", UriKind.Relative));
        }
        else
        {
            await _voiceService.StartListeningAsync();

            if (_voiceService.IsListening)
            {
                ListeningIndicator.Visibility = Visibility.Visible;
                RobotImage.Source = new System.Windows.Media.Imaging.BitmapImage(
                    new Uri("Assets/robot_listening.png", UriKind.Relative));
            }
            // Se não conseguiu ligar (ex: sem microfone), o robô permanece no estado
            // idle e o HandleVoiceError já mostra o balão sugerindo o teclado.
        }
    }

    private async void GitSyncButton_Click(object sender, RoutedEventArgs e)
    {
        GitSyncButton.IsEnabled = false;

        try
        {
            var projectPath = await GitSyncHandler.ResolveProjectPathAsync();
            if (projectPath is null)
            {
                ShowFeedback("Não consegui identificar o projeto. Deixe o VS Code ou o Visual Studio aberto.");
                return;
            }

            var window = new GitFlowWindow(projectPath);
            window.Show();
        }
        finally
        {
            GitSyncButton.IsEnabled = true;
        }
    }

    private void KeyboardToggleButton_Click(object sender, RoutedEventArgs e)
    {
        TextInputPanel.Visibility = TextInputPanel.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;

        if (TextInputPanel.Visibility == Visibility.Visible)
        {
            CommandTextBox.Focus();
        }
    }

    private async void SendTextButton_Click(object sender, RoutedEventArgs e) => await SubmitTypedCommandAsync();

    private async void CommandTextBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            await SubmitTypedCommandAsync();
        }
    }

    private async Task SubmitTypedCommandAsync()
    {
        var text = CommandTextBox.Text?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        CommandTextBox.Clear();
        await _router.ProcessAsync(text);
    }

    private async void HandleVoiceRecognized(string recognizedText)
    {
        // Volta pro estado "idle" visualmente enquanto processa,
        // e processa o comando reconhecido.
        await Dispatcher.InvokeAsync(async () => await _router.ProcessAsync(recognizedText));
    }

    private void HandleVoiceError(string message)
    {
        Dispatcher.Invoke(() => ShowFeedback($"Não consegui ouvir direito. Tente de novo ou digite: {message}"));
    }

    private void ShowPartialFeedback(string partialText)
    {
        Dispatcher.Invoke(() => ShowFeedback(partialText, autoHide: false));
    }

    private System.Windows.Threading.DispatcherTimer? _feedbackHideTimer;

    private void ShowFeedback(string message, bool autoHide = true)
    {
        // Cancela qualquer timer anterior antes de mostrar a nova mensagem - sem isso,
        // um timer de uma mensagem antiga podia esconder o balão no meio de uma fala
        // mais recente (que tem autoHide: false enquanto a pessoa ainda está falando).
        _feedbackHideTimer?.Stop();
        _feedbackHideTimer = null;

        FeedbackText.Text = message;
        FeedbackBubble.Visibility = Visibility.Visible;

        if (autoHide)
        {
            _feedbackHideTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(4)
            };
            _feedbackHideTimer.Tick += (_, _) =>
            {
                FeedbackBubble.Visibility = Visibility.Collapsed;
                _feedbackHideTimer?.Stop();
            };
            _feedbackHideTimer.Start();
        }
    }
}
