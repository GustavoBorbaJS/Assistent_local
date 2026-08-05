using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using Assist_IA_Borb.Core.Handlers;

// Aliases explícitos: com UseWindowsForms habilitado (necessário pro NotifyIcon da
// bandeja), os nomes Application e MessageBox existem em WPF e em WinForms ao mesmo
// tempo. Estes aliases deixam claro que aqui usamos sempre a versão do WPF.
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;

namespace Assist_IA_Borb.UI;

/// <summary>
/// Implementa a lógica "estilo Git GUI": detecta a pasta do projeto atualmente aberto
/// no VS Code ou Visual Studio (via título da janela), garante que a identidade global
/// do Git esteja configurada, e sincroniza com o GitHub - criando um repositório novo
/// automaticamente (via GitHub CLI) se o projeto ainda não tiver um.
///
/// Fica no projeto de UI porque precisa mostrar diálogos/mensagens (GitConfigDialog,
/// MessageBox) - os outros handlers "puros" (YouTube, Agenda) não têm essa dependência.
///
/// LIMITAÇÃO HONESTA: a detecção de "qual projeto está aberto" é heurística, baseada no
/// título das janelas abertas do Windows - não é uma integração oficial com o VS Code/
/// Visual Studio. Com várias janelas abertas ao mesmo tempo, pega a que aparecer primeiro
/// na ordem de sobreposição (Z-order), que tende a ser a mais recentemente usada, mas não
/// é garantido.
/// </summary>
public sealed class GitSyncHandler : ICommandHandler
{
    public string IntentKey => "git";

    private static readonly string[] SearchRoots =
    [
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        Environment.ExpandEnvironmentVariables(@"%USERPROFILE%\source\repos"),
        Environment.ExpandEnvironmentVariables(@"%USERPROFILE%\Projects"),
        Environment.ExpandEnvironmentVariables(@"%USERPROFILE%\Documents\GitHub"),
    ];

    public Task ExecuteAsync(string query, CancellationToken cancellationToken = default) =>
        SyncCurrentProjectAsync(cancellationToken);

    /// <summary>
    /// Ponto de entrada público - usado tanto pelo comando de voz/texto quanto pelo
    /// botão dedicado do ícone de Git na tela.
    /// </summary>
    public static async Task<string?> ResolveProjectPathAsync(CancellationToken cancellationToken = default)
    {
        var windowTitle = FindActiveDevWindowTitle();
        if (windowTitle is null)
        {
            return null;
        }

        var projectName = ExtractProjectNameFromTitle(windowTitle);
        if (string.IsNullOrWhiteSpace(projectName))
        {
            return null;
        }

        var projectPath = ProjectFolderMemory.TryGet(projectName)
                          ?? FindProjectFolder(projectName);

        if (projectPath is null)
        {
            projectPath = await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var dialog = new Microsoft.Win32.OpenFolderDialog
                {
                    Title = $"Selecione a pasta do projeto \"{projectName}\"",
                    InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
                };
                return dialog.ShowDialog() == true ? dialog.FolderName : null;
            });
        }

        if (projectPath is not null)
        {
            ProjectFolderMemory.Save(projectName, projectPath);
        }

        return projectPath;
    }

    public static async Task SyncCurrentProjectAsync(CancellationToken cancellationToken = default)
    {
        var projectPath = await ResolveProjectPathAsync(cancellationToken);

        if (projectPath is null)
        {
            ShowMessage(
                "Não consegui identificar a pasta do projeto. Deixe o VS Code ou o " +
                "Visual Studio aberto com o projeto certo e tente de novo.");
            return;
        }

        if (!await IsCommandAvailableAsync("git", "--version"))
        {
            ShowMessage("O Git não parece estar instalado ou não está no PATH.");
            return;
        }

        try
        {
            await EnsureGitIdentityAsync(cancellationToken);
        }
        catch (InvalidOperationException)
        {
            ShowMessage("Configuração de identidade do Git cancelada - nada foi sincronizado.");
            return;
        }

        var isRepo = Directory.Exists(Path.Combine(projectPath, ".git"));

        if (!isRepo)
        {
            await RunGitAsync(projectPath, "init", cancellationToken);
        }

        // -----------------------------------------------------------------------
        // REMOTE: resolve ANTES do commit. Se não tem remote, pergunta/conecta agora.
        // Motivo: o push precisa do remote, e o commit só faz sentido se o push vai
        // funcionar depois. Resolver o remote no meio (depois do commit mas antes do
        // push) causava a situação onde o commit existia mas o branch não tinha remote
        // configurado, resultando em "src refspec main does not match any".
        // -----------------------------------------------------------------------
        var remoteResult = await RunGitAsync(projectPath, "remote", cancellationToken);
        var hasRemote = !string.IsNullOrWhiteSpace(remoteResult.StandardOutput);

        if (!hasRemote)
        {
            var folderName = Path.GetFileName(projectPath.TrimEnd(Path.DirectorySeparatorChar));
            var suggestedUrl = $"https://github.com/SEU-USUARIO/{folderName}.git";

            var existingRepoUrl = await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var dialog = new RepoUrlDialog(suggestedUrl);
                return dialog.ShowDialog() == true ? dialog.EnteredUrl : null;
            });

            if (!string.IsNullOrWhiteSpace(existingRepoUrl))
            {
                var url = existingRepoUrl.EndsWith(".git", StringComparison.OrdinalIgnoreCase)
                    ? existingRepoUrl
                    : existingRepoUrl + ".git";

                var addRemoteResult = await RunGitAsync(projectPath, $"remote add origin \"{url}\"", cancellationToken);
                if (addRemoteResult.ExitCode != 0)
                {
                    ShowMessage($"Não consegui conectar ao repositório: {addRemoteResult.StandardError}");
                    return;
                }

                hasRemote = true;
            }
            else if (await IsCommandAvailableAsync("gh", "--version"))
            {
                // Usuário cancelou o diálogo: tenta criar um repositório novo via GitHub CLI.
                await RunGitAsync(projectPath, "add -A", cancellationToken);
                await RunGitAsync(projectPath, "commit -m \"Commit inicial via Assist_IA_Borb\"", cancellationToken);

                var createResult = await RunProcessAsync(
                    "gh",
                    $"repo create {folderName} --private --source=. --remote=origin --push",
                    projectPath,
                    cancellationToken);

                ShowMessage(createResult.ExitCode == 0
                    ? $"Repositório '{folderName}' criado no GitHub e projeto enviado!"
                    : $"Não consegui criar o repositório no GitHub: {createResult.StandardError}");
                return;
            }
            else
            {
                ShowMessage(
                    "Esse projeto ainda não tem repositório no GitHub, e o GitHub CLI " +
                    "(gh) não foi encontrado pra criar um automaticamente.\n\n" +
                    "Cole a URL de um repositório existente e tente de novo, ou instale " +
                    "o GitHub CLI em cli.github.com e faça 'gh auth login' uma vez.");
                return;
            }
        }

        // -----------------------------------------------------------------------
        // ADD + COMMIT + PUSH: agora que o remote está garantido.
        // -----------------------------------------------------------------------
        await RunGitAsync(projectPath, "add -A", cancellationToken);

        // Commit pode retornar código de erro se não houver nada novo - não é falha,
        // só significa que não houve alteração desde o último commit.
        var commitResult = await RunGitAsync(
            projectPath,
            "commit -m \"Atualização via Assist_IA_Borb\"",
            cancellationToken);

        // Verifica se tem pelo menos um commit (necessário pro push funcionar).
        // "git log" falha se não há commits de forma alguma.
        var logResult = await RunGitAsync(projectPath, "log --oneline -1", cancellationToken);
        if (logResult.ExitCode != 0)
        {
            ShowMessage(
                "Não há nenhum commit nesse repositório ainda. Verifique se os arquivos " +
                "foram adicionados corretamente e tente de novo.");
            return;
        }

        // Garante que a branch se chama "main" (padrão GitHub).
        // "checkout -b main" funciona mesmo sem commits anteriores;
        // "branch -M main" exige pelo menos um commit.
        var currentBranchResult = await RunGitAsync(projectPath, "branch --show-current", cancellationToken);
        var currentBranch = currentBranchResult.StandardOutput.Trim();

        if (!string.Equals(currentBranch, "main", StringComparison.OrdinalIgnoreCase))
        {
            // Tenta renomear; se falhar (branch vazia), cria do zero.
            var renameResult = await RunGitAsync(projectPath, "branch -M main", cancellationToken);
            if (renameResult.ExitCode != 0)
            {
                await RunGitAsync(projectPath, "checkout -b main", cancellationToken);
            }
        }

        var pushResult2 = await RunGitAsync(projectPath, "push -u origin main", cancellationToken);
        ShowMessage(pushResult2.ExitCode == 0
            ? $"Projeto '{Path.GetFileName(projectPath)}' sincronizado com o GitHub!"
            : $"Não consegui enviar as mudanças:\n{pushResult2.StandardError}\n\n" +
              "Se o repositório remoto já tem arquivos (README, etc), rode no terminal:\n" +
              "git pull origin main --allow-unrelated-histories");
    }

    private static async Task EnsureGitIdentityAsync(CancellationToken cancellationToken)
    {
        var nameResult = await RunProcessAsync("git", "config --global user.name", Environment.CurrentDirectory, cancellationToken);
        var emailResult = await RunProcessAsync("git", "config --global user.email", Environment.CurrentDirectory, cancellationToken);

        var currentName = nameResult.StandardOutput.Trim();
        var currentEmail = emailResult.StandardOutput.Trim();

        if (!string.IsNullOrWhiteSpace(currentName) && !string.IsNullOrWhiteSpace(currentEmail))
        {
            return;
        }

        var (name, email) = await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            var dialog = new GitConfigDialog(currentName, currentEmail);
            var confirmed = dialog.ShowDialog() == true;
            return confirmed ? (dialog.EnteredName, dialog.EnteredEmail) : (string.Empty, string.Empty);
        });

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(email))
        {
            throw new InvalidOperationException("Identidade do Git não configurada.");
        }

        await RunProcessAsync("git", $"config --global user.name \"{name}\"", Environment.CurrentDirectory, cancellationToken);
        await RunProcessAsync("git", $"config --global user.email \"{email}\"", Environment.CurrentDirectory, cancellationToken);
    }


    private static string? ExtractProjectNameFromTitle(string title)
    {
        var name = ExtractRawProjectName(title);
        return name is null ? null : CleanupWindowTitleNoise(name);
    }

    private static string? ExtractRawProjectName(string title)
    {
        if (title.Contains("Visual Studio Code", StringComparison.OrdinalIgnoreCase))
        {
            var parts = title.Split(" - ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            // Formato típico: "arquivo.cs - NomeDaPasta - Visual Studio Code"
            // ou, sem arquivo aberto: "NomeDaPasta - Visual Studio Code"
            return parts.Length >= 2 ? parts[^2] : null;
        }

        if (title.Contains("Microsoft Visual Studio", StringComparison.OrdinalIgnoreCase))
        {
            var index = title.IndexOf(" - Microsoft Visual Studio", StringComparison.OrdinalIgnoreCase);
            return index > 0 ? title[..index].Trim() : null;
        }

        return null;
    }

    /// <summary>
    /// Remove "ruído de estado" que as IDEs adicionam ao título e que não faz parte
    /// do nome real do projeto/solution:
    /// - Visual Studio em depuração: "Assist_IA_Borb (Executando)" / "(Running)" /
    ///   "(Depurando)" / "(Debugging)" - acontece SEMPRE que o app é testado via F5,
    ///   que é exatamente o cenário mais comum de teste.
    /// - Modo administrador: sufixo "[Administrador]" / "[Administrator]".
    /// - VS Code com arquivo não salvo: prefixo "●".
    /// </summary>
    private static string CleanupWindowTitleNoise(string name)
    {
        var cleaned = name.Trim().TrimStart('●', '*', ' ');

        // Remove qualquer sufixo entre parênteses ou colchetes no FINAL do nome -
        // "(Executando)", "(Running)", "[Administrador]", etc. Aplica em loop porque
        // pode haver mais de um ("Nome (Executando) [Administrador]").
        while (true)
        {
            var trimmed = System.Text.RegularExpressions.Regex.Replace(
                cleaned, @"\s*[\(\[][^\)\]]*[\)\]]\s*$", "").Trim();

            if (trimmed == cleaned || string.IsNullOrWhiteSpace(trimmed))
            {
                break;
            }

            cleaned = trimmed;
        }

        return cleaned;
    }

    private static string? FindProjectFolder(string name)
    {
        // 1) Tentativa rápida: pasta com esse nome exato, direto dentro de uma das raízes.
        foreach (var root in SearchRoots)
        {
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
            {
                continue;
            }

            string[] subDirs;
            try
            {
                subDirs = Directory.GetDirectories(root);
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            var exactMatch = subDirs.FirstOrDefault(d =>
                string.Equals(Path.GetFileName(d), name, StringComparison.OrdinalIgnoreCase));

            if (exactMatch is not null)
            {
                return exactMatch;
            }
        }

        // 2) Fallback mais profundo: o nome que aparece no título da janela costuma ser
        // o nome da SOLUTION (.sln), que nem sempre bate com o nome da pasta raiz no
        // disco (comum quando o projeto foi renomeado ao longo do tempo, ou quando a
        // .sln fica numa subpasta "src"). Procura recursivamente (até 4 níveis) por um
        // arquivo .sln com esse nome.
        foreach (var root in SearchRoots)
        {
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
            {
                continue;
            }

            var slnPath = FindSlnFileRecursive(root, name, maxDepth: 4);
            if (slnPath is null)
            {
                continue;
            }

            var slnDir = Path.GetDirectoryName(slnPath)!;

            // Convenção comum (inclusive a que usamos nesse projeto): a .sln fica numa
            // subpasta "src", e a raiz de verdade (onde deveria ficar o .git) é uma
            // pasta acima.
            return string.Equals(Path.GetFileName(slnDir), "src", StringComparison.OrdinalIgnoreCase)
                ? Path.GetDirectoryName(slnDir) ?? slnDir
                : slnDir;
        }

        return null;
    }

    private static string? FindSlnFileRecursive(string currentDir, string name, int maxDepth)
    {
        if (maxDepth < 0)
        {
            return null;
        }

        string[] files;
        try
        {
            files = Directory.GetFiles(currentDir, "*.sln");
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }

        var match = files.FirstOrDefault(f =>
            string.Equals(Path.GetFileNameWithoutExtension(f), name, StringComparison.OrdinalIgnoreCase));

        if (match is not null)
        {
            return match;
        }

        string[] subDirs;
        try
        {
            subDirs = Directory.GetDirectories(currentDir);
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }

        foreach (var subDir in subDirs)
        {
            // Evita entrar em pastas de build/lixo que só desperdiçam tempo de busca.
            var dirName = Path.GetFileName(subDir);
            if (dirName is "bin" or "obj" or "node_modules" or ".git")
            {
                continue;
            }

            var found = FindSlnFileRecursive(subDir, name, maxDepth - 1);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    // --- Enumeração de janelas (Win32) ---

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    private static string? FindActiveDevWindowTitle()
    {
        string? found = null;

        EnumWindows((hWnd, _) =>
        {
            if (!IsWindowVisible(hWnd))
            {
                return true; // continua procurando
            }

            var buffer = new StringBuilder(512);
            GetWindowText(hWnd, buffer, buffer.Capacity);
            var title = buffer.ToString();

            if (string.IsNullOrWhiteSpace(title))
            {
                return true;
            }

            if (title.Contains("Visual Studio Code", StringComparison.OrdinalIgnoreCase) ||
                title.Contains("Microsoft Visual Studio", StringComparison.OrdinalIgnoreCase))
            {
                found = title;
                return false; // achou - para a enumeração (janelas vêm em ordem de Z-order)
            }

            return true;
        }, IntPtr.Zero);

        return found;
    }

    // --- Execução de processos ---

    private static Task<(int ExitCode, string StandardOutput, string StandardError)> RunGitAsync(
        string workingDirectory, string arguments, CancellationToken cancellationToken) =>
        RunProcessAsync("git", arguments, workingDirectory, cancellationToken);

    private static async Task<bool> IsCommandAvailableAsync(string fileName, string arguments)
    {
        try
        {
            var result = await RunProcessAsync(fileName, arguments, Environment.CurrentDirectory, CancellationToken.None);
            return result.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<(int ExitCode, string StandardOutput, string StandardError)> RunProcessAsync(
        string fileName, string arguments, string workingDirectory, CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            }
        };

        process.Start();

        var stdOutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stdErrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken);

        return (process.ExitCode, await stdOutTask, await stdErrTask);
    }

    private static void ShowMessage(string message)
    {
        Application.Current.Dispatcher.Invoke(() =>
            MessageBox.Show(message, "Assist_IA_Borb - Git", MessageBoxButton.OK, MessageBoxImage.Information));
    }
}
