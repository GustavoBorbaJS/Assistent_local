using System.Text.RegularExpressions;

namespace Assist_IA_Borb.Core.Intent;

/// <summary>
/// Classificador de intenção 100% local, baseado em palavras-chave - não depende de
/// nenhuma API paga nem de conexão com internet. Funciona como o "filtro mínimo":
/// "abre X" -> abrir um app instalado; "youtube/vídeo/toca X" -> busca no YouTube;
/// "agenda/anota X" -> agenda ou anotação; qualquer outra coisa -> pesquisa geral.
///
/// O ProxyIntentClassifier e o Assist_IA_Borb.Proxy (backend com LLM) continuam
/// existindo no projeto, só não estão em uso agora - é só trocar o registro de DI
/// no App.xaml.cs pra reativar a classificação via IA quando fizer sentido de novo.
/// </summary>
public sealed class LocalKeywordIntentClassifier : IIntentClassifier
{
    // Ordem importa: categorias mais específicas primeiro, "pesquisa" por último
    // como fallback genérico.
    private static readonly string[] AnotacaoTriggers =
        ["anota que", "anota ", "anotação", "cria uma nota", "cria nota", "lembrete", "escreve que", "escreve aí que"];

    private static readonly string[] AlarmeTriggers =
        ["alarme", "despertador", "me acorda", "me acorde", "me desperta"];

    private static readonly string[] AgendaTriggers =
        ["agenda ", "agende", "agendar", "marca uma", "marque uma", "marca um", "marque um",
         "reunião", "reuniao", "consulta", "compromisso"];

    private static readonly string[] YoutubeTriggers =
        ["youtube", "vídeo de", "video de", "toca ", "assistir", "assiste "];

    private static readonly string[] AbrirTriggers =
        ["abre ", "abrir ", "abra "];

    private static readonly string[] GitTriggers =
        ["sobe pro git", "sobe pro github", "sobe o projeto pro github", "publica no github",
         "cria um repositório", "cria repositório", "sincroniza com o github",
         "sincroniza o projeto", "faz o git", "roda o git", "salva no github"];

    private static readonly string[] PesquisaTriggers =
        ["pesquisa ", "pesquisar ", "pesquise ", "procura ", "busca "];

    public Task<CommandIntent> ClassifyAsync(string userInput, CancellationToken cancellationToken = default)
    {
        var raw = userInput?.Trim() ?? string.Empty;

        if (string.IsNullOrEmpty(raw))
        {
            return Result("pesquisa", raw, raw);
        }

        var normalized = raw.ToLowerInvariant();

        if (TryMatch(normalized, raw, AnotacaoTriggers, out var anotacaoQuery))
            return Result("anotacao", anotacaoQuery, raw);

        // Alarme ANTES de agenda: "me acorda às 7" é alarme, não compromisso.
        // Nos dois casos o texto vai com os gatilhos removidos mas com a expressão de
        // data/hora intacta - quem faz a interpretação temporal é o PtBrDateTimeParser
        // dentro do handler, que também devolve o texto restante pra usar como título.
        if (TryMatch(normalized, raw, AlarmeTriggers, out var alarmeQuery))
            return Result("alarme", alarmeQuery, raw);

        if (TryMatch(normalized, raw, AgendaTriggers, out var agendaQuery))
            return Result("agenda", agendaQuery, raw);

        if (TryMatch(normalized, raw, YoutubeTriggers, out var youtubeQuery))
            return Result("youtube", youtubeQuery, raw);

        if (TryMatch(normalized, raw, AbrirTriggers, out var abrirQuery))
            return Result("sistema", abrirQuery, raw);

        if (TryMatch(normalized, raw, GitTriggers, out var gitQuery))
            return Result("git", gitQuery, raw);

        if (TryMatch(normalized, raw, PesquisaTriggers, out var pesquisaQuery))
            return Result("pesquisa", pesquisaQuery, raw);

        // Nada bateu com nenhum gatilho conhecido: fallback padrão, pesquisa geral
        // com o texto inteiro.
        return Result("pesquisa", raw, raw);
    }

    // Artigos/pronomes que costumam sobrar colados no início depois de remover a
    // palavra-gatilho (ex: "abre o git" -> sobra "o git" -> precisa virar só "git").
    private static readonly string[] LeadingStopWords = ["o ", "a ", "os ", "as ", "um ", "uma "];

    /// <summary>
    /// Verifica se algum gatilho da categoria aparece no texto e, se sim, remove
    /// TODAS as ocorrências de gatilhos dessa categoria do texto original - sobra só
    /// o "conteúdo" do comando. Funciona bem tanto quando o gatilho vem no início
    /// ("abre o X") quanto no meio/fim ("toca uma música no youtube").
    /// </summary>
    private static bool TryMatch(string normalized, string raw, string[] triggers, out string query)
    {
        var matched = triggers.Any(t => normalized.Contains(t, StringComparison.Ordinal));
        if (!matched)
        {
            query = string.Empty;
            return false;
        }

        var cleaned = raw;
        foreach (var trigger in triggers)
        {
            cleaned = Regex.Replace(cleaned, Regex.Escape(trigger), "", RegexOptions.IgnoreCase);
        }

        cleaned = Regex.Replace(cleaned, @"\s{2,}", " ").Trim(' ', ',', '.', '-');

        // Remove UM artigo/pronome solto do início, se sobrou (só um, pra não comer
        // palavras que façam parte de verdade do nome do app/termo pesquisado).
        foreach (var stopWord in LeadingStopWords)
        {
            if (cleaned.StartsWith(stopWord, StringComparison.OrdinalIgnoreCase))
            {
                cleaned = cleaned[stopWord.Length..].Trim();
                break;
            }
        }

        query = string.IsNullOrWhiteSpace(cleaned) ? raw : cleaned;
        return true;
    }

    private static Task<CommandIntent> Result(string intent, string query, string raw) =>
        Task.FromResult(new CommandIntent
        {
            IntentKey = intent,
            Query = query,
            Confidence = 1.0,
            RawInput = raw
        });
}
