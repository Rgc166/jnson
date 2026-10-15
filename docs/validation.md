# Registro de validação

Data: 15/10/2026. Ambiente de execução: Linux x64. SDK .NET 10.0.402; PowerShell 7.6.7 obtido da distribuição oficial e conferido com SHA-256 publicado pelo projeto.

## Verificado neste ambiente

| Verificação | Resultado |
|---|---|
| Compilação da solução, incluindo WPF e serviços Windows | Sucesso; zero erros e zero avisos, com `-warnaserror` |
| Testes automatizados Core | 27 passaram; zero falhas e zero ignorados |
| Auditoria de dependências diretas e transitivas | Nenhuma vulnerabilidade conhecida reportada pela fonte NuGet consultada nesta execução |
| Sintaxe dos scripts PowerShell | Analisada pelo parser do PowerShell |
| Helpers de instalação | Gravação/substituição atômica JSON e propagação de falha de comando externo verificadas |
| Publicação cruzada Windows x64 | Serviços e aplicativos publicados com runtime incluído; pacotes Professor/Aluno gerados pelo script de build |

Os testes cobrem uso único, substituição, expiração e revogação de códigos/credenciais; isolamento por laboratório; sequência e persistência; transação interrompida; migração das tabelas da etapa 1; comunicação recente e recuperação; dados alterados durante integração; validação de campos e URLs; certificados expirados, nome divergente e impressão digital incorreta; integração com UUID estável, backups, colisões e restauração.

Os testes do adaptador Veyon usam um executor controlado para testar os comandos, alterações e verificações. **Eles não executam o Veyon real.** O formato JSON, comandos e a necessidade de preservar UUID foram conferidos também no código oficial do Veyon. Isso não prova compatibilidade com toda versão instalada.

## Ainda não verificado

- Execução e renderização das janelas WPF, temas, teclado, DPI, acessibilidade e redimensionamento no Windows.
- Instalação, reparo e remoção reais, serviços SYSTEM, ACLs NTFS, canal administrativo e recusa de usuários comuns no Windows.
- Coleta real de usuário, IP e MAC, incluindo PCs sem sessão e troca de rede.
- TLS professor–aluno em computadores separados, DNS, firewall e o caso histórico de falha no PC03.
- Atualização e restauração na instalação real do Veyon e atualização visual do Veyon Master.
- Execução do workflow no GitHub: os arquivos estão preparados, mas não houve push nem execução remota.

## Roteiro de aceitação no Windows

Use uma VM descartável sem instalação anterior. No PowerShell 7 elevado, gere os pacotes com `./scripts/build.ps1` e execute `./scripts/windows-smoke.ps1`. O script recusa dados/serviços preexistentes, instala professor e aluno, pareia por HTTPS, envia um relatório, confere recebimento, repara e reinicia os serviços, confere identidade e duplicação e remove os dados de teste.

Em seguida, teste com professor e aluno separados: troca de usuário, alteração de IP, professor desligado, reinício com pendência, modo manual e certificado incorreto/expirado. Confira que um usuário comum não consegue ler os arquivos de dados nem executar ações administrativas sem elevação.

Para UI: abra as duas aplicações, navegue por teclado, teste tema escuro/claro/Windows, 100%/150%/200% de DPI, filtro enquanto chegam dados, erro do serviço e pareamento com falha sem perda dos campos. Feche as janelas e confirme que o envio do serviço continua.

Para Veyon: registre sua versão, use um diretório de teste com cadastros não administrados, crie backup, aplique uma máquina, altere seu IP, reaplique e confira que o UUID e a quantidade de cadastros se mantêm. Confira também que objetos externos permanecem intactos e teste a restauração com confirmação.

Registre os resultados reais antes de promover esta versão a uma release de produção. Os instaladores são scripts em ZIP, sem assinatura de código. A licença de distribuição continua pendente de escolha pelo proprietário.
