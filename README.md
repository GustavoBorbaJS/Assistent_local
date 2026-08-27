# Assist_IA_Borb — esqueleto do projeto

Assistente flutuante para Windows (WPF), voltado a idosos e pessoas com
dificuldade de usar o sistema, com comando de voz como entrada principal
e digitação como fallback.

## Estrutura

```
src/
├── Assist_IA_Borb.Core/       -> CommandRouter, interfaces (ICommandHandler, IIntentClassifier)
├── Assist_IA_Borb.Handlers/   -> YouTube, Google Agenda, Pesquisa, Configurações do Windows
├── Assist_IA_Borb.Speech/     -> Integração com Azure Speech SDK (pt-BR)
├── Assist_IA_Borb.UI/         -> App WPF (janela flutuante, robozinho, teclado)
└── Assist_IA_Borb.Proxy/      -> Backend fino que guarda as chaves reais (LLM)
```

## Por que existe um "Proxy" separado?

O app desktop (Assist_IA_Borb.UI) **nunca** fala diretamente com a API do LLM
usada para classificar intenção. Ele chama o `Assist_IA_Borb.Proxy`, que roda
no seu servidor e é o único lugar com a chave real. Isso porque qualquer
chave embutida no cliente C# pode ser extraída via decompilação (dnSpy/ILSpy),
mesmo com obfuscation.

## Por que DeepSeek para a classificação de intenção?

A tarefa aqui é simples e bem delimitada: decidir entre 4 categorias fixas
(`youtube`, `agenda`, `sistema`, `pesquisa`) e extrair o termo de busca. Um
modelo caro tipo Claude/GPT-4 é overkill pra isso e custa muito mais por
chamada — o `deepseek-chat` resolve com uma fração do custo, o que importa
bastante num projeto grátis/portfólio com potencial de muitas chamadas por
dia. O `Program.cs` do Proxy já usa `response_format: json_object` da API
do DeepSeek pra forçar saída em JSON válido, e tem fallback pra "pesquisa"
se a resposta vier fora do formato ou a chamada falhar — o app nunca trava
por causa disso.

A chave da **Azure Speech** por enquanto ainda vai no cliente (é assim que o
SDK funciona nativamente) — mitigado com:
1. `dotnet user-secrets` em desenvolvimento (nunca vai pro Git).
2. Variável de ambiente em produção.
3. Se/quando o projeto crescer, dá pra migrar pra emissão de token temporário
   via STS da Azure, feita pelo próprio Proxy — aí a chave mestra da Azure
   também some do cliente.

## Setup local

```bash
# 1. Configurar segredos locais (dev) - dentro de src/Assist_IA_Borb.UI
dotnet user-secrets init
dotnet user-secrets set "Azure:SpeechSubscriptionKey" "SUA_KEY_TRIAL_AQUI"
dotnet user-secrets set "Proxy:InstallationToken" "um-token-qualquer-por-instalacao"

# 2. Rodar o backend proxy (outro terminal)
cd src/Assist_IA_Borb.Proxy
setx DEEPSEEK_API_KEY "sua-chave-do-deepseek"   # pega em https://platform.deepseek.com
# reabra o terminal depois do setx pra variável valer, ou use:
# $env:DEEPSEEK_API_KEY="sua-chave-do-deepseek"   (PowerShell, só na sessão atual)
dotnet run

# 3. Rodar o app
cd src/Assist_IA_Borb.UI
dotnet run
```

## Checklist de segurança antes de distribuir publicamente

- [ ] Nenhuma chave em `appsettings.json` versionado no Git (só placeholders).
- [ ] `DEEPSEEK_API_KEY` só existe no ambiente do servidor Proxy.
- [ ] Rate limiting no Proxy ativo (evita abuso da conta trial).
- [ ] Build de distribuição com `PublishAot=true` (dificulta decompilação).
- [ ] Passar o binário final por um obfuscador gratuito (ex: ConfuserEx) como
      camada extra, mesmo com AOT.
- [ ] Testar o app sem internet (deve degradar pra "não entendi, tente
      digitar" em vez de travar — importante pro público-alvo).

## Próximos passos sugeridos

1. Implementar `InstalledAppsIndexer` (Core/Handlers) para buscar entre
   atalhos do Menu Iniciar quando o comando não bate com nada específico.
2. Testes manuais de posicionamento em telas com escala diferente
   (100%/125%/150% no Windows) — `SystemParameters.WorkArea` já lida com
   DPI, mas vale confirmar visualmente.
3. Ajustar o `SystemPrompt` do `Assist_IA_Borb.Proxy/Program.cs` conforme os
   erros de classificação aparecerem no uso real (o log já registra quando
   o DeepSeek foge do formato JSON esperado).
