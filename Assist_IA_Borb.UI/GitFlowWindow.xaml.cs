using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;

// Aliases explícitos: com UseWindowsForms habilitado (necessário pro NotifyIcon da
// bandeja), os nomes Application e MessageBox existem em WPF e em WinForms ao mesmo
// tempo. Estes aliases deixam claro que aqui usamos sempre a versão do WPF.
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;
using Color = System.Windows.Media.Color;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;

namespace Assist_IA_Borb.UI;

public partial class GitFlowWindow : Window
{
    private readonly string _projectPath;
    private string _currentBranch = "main";

    public GitFlowWindow(string projectPath)
    {
        InitializeComponent();
        _projectPath = projectPath;

        ProjectPathLabel.Text = projectPath;
        Loaded += async (_, _) => await InitializeAsync();
    }

    // ─────────────────────────────────────────────
    //  Inicialização: status + branches
    // ─────────────────────────────────────────────

    private async Task InitializeAsync()
    {
        // Garante que existe um .git na raiz do projeto (e não num subdiretório).
        // Se o git init foi feito dentro de src/ por engano, o .git vai estar lá e
        // o add/status vão falhar com "does not have a commit checked out".
        var hasGitHere = Directory.Exists(Path.Combine(_projectPath, ".git"));
        if (!hasGitHere)
        {
            StatusOutput.Text = "Repositório Git não inicializado nessa pasta. Rodando git init...";
            var initResult = await RunGitAsync("init");
            if (initResult.ExitCode != 0)
            {
                StatusOutput.Text = $"Falha ao inicializar repositório:\n{initResult.Stderr}";
                AddButton.IsEnabled = false;
                return;
            }

            GitIgnoreHelper.EnsureDefaultGitIgnore(_projectPath);
        }

        // Elimina os warnings de LF/CRLF que poluem a saída do add (não é erro real,
        // só uma conversão automática de line endings que o Git faz no Windows por padrão).
        await RunGitAsync("config core.autocrlf false");

        // Verifica se algum subdiretório imediato é também um repo Git — causa o erro
        // "src/ does not have a commit checked out" no git add. Avisa o usuário.
        var suspectSubrepos = FindSubrepositories(_projectPath);
        if (suspectSubrepos.Count > 0)
        {
            var names = string.Join(", ", suspectSubrepos.Select(Path.GetFileName));
            StatusOutput.Text =
                $"⚠ Atenção: as pastas [{names}] contêm um .git próprio.\n" +
                "Isso impede o 'git add' de funcionar corretamente.\n" +
                "Apague o .git interno dessas pastas e reabra esta janela.";
            AddButton.IsEnabled = false;
            return;
        }

        // Remote
        var remote = await RunGitAsync("remote get-url origin");
        RemoteLabel.Text = remote.ExitCode == 0
            ? $"↑ {remote.Stdout.Trim()}"
            : "Sem repositório remoto configurado";

        // Branches disponíveis (locais)
        var branchResult = await RunGitAsync("branch");
        var branches = branchResult.Stdout
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(b => b.Trim().TrimStart('*').Trim())
            .Where(b => !string.IsNullOrWhiteSpace(b))
            .ToList();

        if (branches.Count == 0)
        {
            branches.Add("main");
        }

        BranchCombo.ItemsSource = branches;

        // Branch atual
        var currentResult = await RunGitAsync("branch --show-current");
        _currentBranch = currentResult.Stdout.Trim();
        if (string.IsNullOrWhiteSpace(_currentBranch))
        {
            _currentBranch = "main";
        }

        BranchCombo.SelectedItem = branches.Contains(_currentBranch)
            ? _currentBranch
            : branches.FirstOrDefault() ?? "main";

        // Status inicial
        await RefreshStatusAsync();
    }

    private async Task RefreshStatusAsync()
    {
        var status = await RunGitAsync("status --short");
        if (string.IsNullOrWhiteSpace(status.Stdout))
        {
            StatusOutput.Text = "(Nenhuma alteração detectada — working tree limpa)";
            StatusOutput.Foreground = new SolidColorBrush(Color.FromRgb(80, 200, 120));
        }
        else
        {
            StatusOutput.Text = status.Stdout.Trim();
            StatusOutput.Foreground = new SolidColorBrush(Color.FromRgb(200, 211, 245));
        }

        // Habilita o Add só se houver arquivos modificados
        AddButton.IsEnabled = !string.IsNullOrWhiteSpace(status.Stdout);
    }

    // ─────────────────────────────────────────────
    //  Passo 2 — git add
    // ─────────────────────────────────────────────

    private async void AddButton_Click(object sender, RoutedEventArgs e)
    {
        AddButton.IsEnabled = false;
        AddOutput.Text = "Verificando subrepositórios Git...";

        // Detecta subpastas que têm seu próprio .git (repositórios aninhados não
        // registrados como submódulos). O git add falha silenciosamente nesses casos,
        // portanto é melhor detectar e resolver antes de tentar adicionar.
        var nestedRepos = FindNestedGitRepos(_projectPath);
        if (nestedRepos.Count > 0)
        {
            var list = string.Join("\n", nestedRepos.Select(r => $"  {r}"));
            var answer = MessageBox.Show(
                $"Encontrei pasta(s) com repositório Git aninhado (não registrado como submódulo):\n\n{list}\n\n" +
                "Isso impede o 'git add'. Deseja remover o(s) '.git' dessas subpastas para continuar?\n\n" +
                "(Os arquivos são preservados — só a pasta .git oculta é removida)",
                "Repositório Git aninhado detectado",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (answer == MessageBoxResult.Yes)
            {
                foreach (var repo in nestedRepos)
                {
                    var gitDir = Path.Combine(repo, ".git");
                    try
                    {
                        // .git pode ter arquivos read-only (git os cria assim);
                        // precisa forçar a remoção mudando o atributo antes de deletar.
                        ForceDeleteDirectory(gitDir);
                    }
                    catch (Exception ex)
                    {
                        AddOutput.Text = $"Não consegui remover {gitDir}:\n{ex.Message}";
                        AddOutput.Foreground = new SolidColorBrush(Color.FromRgb(255, 100, 100));
                        AddButton.IsEnabled = true;
                        return;
                    }
                }
            }
            else
            {
                AddOutput.Text = "Add cancelado. Remova os repositórios aninhados manualmente antes de continuar.";
                AddButton.IsEnabled = true;
                return;
            }
        }

        AddOutput.Text = "Executando git add -A ...";

        var result = await RunGitAsync("add -A");

        if (result.ExitCode == 0)
        {
            var staged = await RunGitAsync("diff --cached --name-only");
            var stagedList = staged.Stdout.Trim();

            AddOutput.Text = string.IsNullOrWhiteSpace(stagedList)
                ? "Nenhum arquivo novo para adicionar."
                : stagedList;

            AddOutput.Foreground = new SolidColorBrush(Color.FromRgb(80, 200, 120));
            CommitButton.IsEnabled = !string.IsNullOrWhiteSpace(stagedList);
        }
        else
        {
            AddOutput.Text = $"Erro: {result.Stderr}";
            AddOutput.Foreground = new SolidColorBrush(Color.FromRgb(255, 100, 100));
            AddButton.IsEnabled = true;
        }
    }

    private static List<string> FindNestedGitRepos(string rootPath)
    {
        var found = new List<string>();
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(rootPath, "*", SearchOption.AllDirectories))
            {
                // Ignora o .git da própria raiz e entradas dentro de pastas .git
                if (dir.Contains(Path.DirectorySeparatorChar + ".git" + Path.DirectorySeparatorChar) ||
                    dir.EndsWith(Path.DirectorySeparatorChar + ".git"))
                {
                    continue;
                }

                var nestedGit = Path.Combine(dir, ".git");
                if (Directory.Exists(nestedGit) || File.Exists(nestedGit))
                {
                    found.Add(dir);
                }
            }
        }
        catch (UnauthorizedAccessException) { }
        catch (IOException) { }

        return found;
    }

    private static void ForceDeleteDirectory(string path)
    {
        foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }
        Directory.Delete(path, recursive: true);
    }

    // ─────────────────────────────────────────────
    //  Passo 3 — git commit
    // ─────────────────────────────────────────────

    private async void CommitButton_Click(object sender, RoutedEventArgs e)
    {
        var message = CommitMessageBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(message))
        {
            MessageBox.Show("Escreva uma mensagem de commit antes de continuar.",
                "Git", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        CommitButton.IsEnabled = false;
        CommitOutput.Visibility = Visibility.Visible;
        CommitOutput.Text = "Executando git commit ...";

        // Garante que a branch existe antes do commit.
        // Em repos recém-criados (sem nenhum commit), "branch -M main" falha;
        // usamos "checkout -b" que funciona em qualquer estado.
        var currentBranchResult = await RunGitAsync("branch --show-current");
        var br = currentBranchResult.Stdout.Trim();
        if (string.IsNullOrWhiteSpace(br))
        {
            await RunGitAsync($"checkout -b {_currentBranch}");
        }
        else if (!string.Equals(br, _currentBranch, StringComparison.OrdinalIgnoreCase))
        {
            await RunGitAsync($"branch -M {_currentBranch}");
        }

        var result = await RunGitAsync($"commit -m \"{EscapeForGit(message)}\"");

        CommitOutput.Text = result.ExitCode == 0
            ? result.Stdout.Trim()
            : result.Stderr.Trim();

        CommitOutput.Foreground = result.ExitCode == 0
            ? new SolidColorBrush(Color.FromRgb(80, 200, 120))
            : new SolidColorBrush(Color.FromRgb(255, 100, 100));

        if (result.ExitCode == 0)
        {
            PushButton.IsEnabled = true;
            PushOutput.Text = $"Pronto para enviar para 'origin/{_currentBranch}'. Clique em Enviar.";
        }
    }

    // ─────────────────────────────────────────────
    //  Passo 4 — git pull
    // ─────────────────────────────────────────────

    private async void PullButton_Click(object sender, RoutedEventArgs e)
    {
        PullButton.IsEnabled = false;

        var remoteCheck = await RunGitAsync("remote");
        if (string.IsNullOrWhiteSpace(remoteCheck.Stdout))
        {
            PullOutput.Text = "Nenhum repositório remoto configurado ainda - configure no envio (Passo 5) primeiro.";
            PullButton.IsEnabled = true;
            return;
        }

        PullOutput.Text = $"Abrindo terminal para buscar atualizações de origin/{_currentBranch}...\n" +
                          "Autentique-se na janela que abrir se solicitado.";
        PullOutput.Foreground = new SolidColorBrush(Color.FromRgb(200, 211, 245));

        // Mesmo motivo do push: usa terminal visível pra permitir autenticação do
        // Git Credential Manager em repositórios privados.
        var success = await RunGitInVisibleTerminalAsync($"pull --rebase origin {_currentBranch}");

        if (success)
        {
            PullOutput.Text = "✓ Comando de busca executado. Verifique o terminal para conferir se houve conflito.";
            PullOutput.Foreground = new SolidColorBrush(Color.FromRgb(80, 200, 120));
            await RefreshStatusAsync();
        }
        else
        {
            PullOutput.Text = "Não foi possível abrir o terminal para buscar atualizações.";
            PullOutput.Foreground = new SolidColorBrush(Color.FromRgb(255, 100, 100));
        }

        PullButton.IsEnabled = true;
    }

    // ─────────────────────────────────────────────
    //  Passo 5 — git push
    // ─────────────────────────────────────────────

    private async void PushButton_Click(object sender, RoutedEventArgs e)
    {
        PushButton.IsEnabled = false;

        // Verifica se o remote existe antes de empurrar
        var remoteCheck = await RunGitAsync("remote");
        if (string.IsNullOrWhiteSpace(remoteCheck.Stdout))
        {
            var url = await AskForRemoteUrlAsync();
            if (string.IsNullOrWhiteSpace(url))
            {
                PushOutput.Text = "Push cancelado — nenhum repositório remoto configurado.";
                PushButton.IsEnabled = true;
                return;
            }

            var addResult = await RunGitAsync($"remote add origin \"{url}\"");
            if (addResult.ExitCode != 0)
            {
                PushOutput.Text = $"Não consegui conectar ao repositório:\n{addResult.Stderr}";
                PushButton.IsEnabled = true;
                return;
            }

            RemoteLabel.Text = $"↑ {url}";
        }

        PushOutput.Text = $"Abrindo terminal para push em origin/{_currentBranch}...\n" +
                          "Autentique-se na janela que abrir se solicitado.";
        PushOutput.Foreground = new SolidColorBrush(Color.FromRgb(200, 211, 245));

        // O push precisa de um terminal REAL e visível porque o Git Credential Manager
        // (GCM) abre uma janela de login do GitHub pra autenticar - isso não funciona
        // quando o processo é criado com CreateNoWindow=true (sem acesso ao desktop).
        // Abrimos um cmd.exe visível que fica esperando o usuário pressionar qualquer
        // tecla depois do push, pra ele poder ler o resultado antes de fechar.
        var success = await RunGitInVisibleTerminalAsync($"push -u origin {_currentBranch}");

        if (success)
        {
            PushOutput.Text = "✓ Comando de push executado. Verifique o terminal que abriu para confirmar o resultado.";
            PushOutput.Foreground = new SolidColorBrush(Color.FromRgb(80, 200, 120));
            await RefreshStatusAsync();
        }
        else
        {
            PushOutput.Text = "Não foi possível abrir o terminal para o push.";
            PushOutput.Foreground = new SolidColorBrush(Color.FromRgb(255, 100, 100));
            PushButton.IsEnabled = true;
        }
    }

    private async Task<string?> AskForRemoteUrlAsync()
    {
        var folderName = Path.GetFileName(_projectPath.TrimEnd(Path.DirectorySeparatorChar));
        return await Dispatcher.InvokeAsync(() =>
        {
            var dialog = new RepoUrlDialog($"https://github.com/SEU-USUARIO/{folderName}.git");
            return dialog.ShowDialog() == true ? dialog.EnteredUrl : null;
        });
    }

    // ─────────────────────────────────────────────
    //  Branch
    // ─────────────────────────────────────────────

    private void BranchCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (BranchCombo.SelectedItem is string branch && !string.IsNullOrWhiteSpace(branch))
        {
            _currentBranch = branch;
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    // ─────────────────────────────────────────────
    //  Helpers
    // ─────────────────────────────────────────────

    /// <summary>
    /// Procura subdiretórios imediatos que contenham um .git próprio — eles causam
    /// o erro "does not have a commit checked out" no git add quando o repositório
    /// pai tenta indexar esses diretórios como arquivos normais.
    /// </summary>
    /// <summary>
    /// Executa um comando git em um terminal cmd VISÍVEL, permitindo que o Git
    /// Credential Manager abra a janela de autenticação do GitHub normalmente.
    /// Usado exclusivamente para 'push' e 'pull', que precisam autenticar.
    /// O terminal fecha automaticamente depois (o usuário vê o resultado por
    /// alguns segundos antes do /K manter a janela esperando Enter).
    /// </summary>
    private async Task<bool> RunGitInVisibleTerminalAsync(string gitArguments)
    {
        try
        {
            // /K mantém a janela do cmd aberta depois que o comando terminar,
            // pra o usuário poder ler "Everything up-to-date" ou o erro real
            // antes de fechar manualmente.
            var cmd = $"/K cd /d \"{_projectPath}\" && git {gitArguments} & echo. & echo Pressione qualquer tecla para fechar... & pause > nul";

            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = cmd,
                UseShellExecute = true,   // ESSENCIAL: dá acesso ao desktop pro GCM
                CreateNoWindow = false,
                WorkingDirectory = _projectPath
            });

            if (process is null)
            {
                return false;
            }

            await process.WaitForExitAsync();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static List<string> FindSubrepositories(string rootPath)
    {
        try
        {
            return Directory
                .GetDirectories(rootPath, "*", SearchOption.TopDirectoryOnly)
                .Where(d => Directory.Exists(Path.Combine(d, ".git")))
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    private static string EscapeForGit(string message) =>
        message.Replace("\"", "\\\"").Replace("\n", " ").Replace("\r", "");

    private async Task<(int ExitCode, string Stdout, string Stderr)> RunGitAsync(string arguments)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = arguments,
                    WorkingDirectory = _projectPath,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                }
            };

            process.Start();

            var stdOut = await process.StandardOutput.ReadToEndAsync();
            var stdErr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            return (process.ExitCode, stdOut, stdErr);
        }
        catch (Exception ex)
        {
            return (-1, string.Empty, ex.Message);
        }
    }
}
