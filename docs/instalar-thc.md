# Fazer a interface THC aparecer no professor

Este pacote complementa o **VeyonSync-etapa2.zip** já instalado. Não é necessário colar a etapa 2 sobre a etapa 1 nem reinstalar o serviço. O serviço do professor deve existir em `C:\Program Files\VeyonSync\Teacher\VeyonSync.Teacher.exe` e usar o banco já existente.

1. No computador do professor, baixe o artefato **professor-thc-etapa2** do GitHub Actions.
2. Extraia o arquivo baixado. Se encontrar outro ZIP dentro, extraia também `VeyonSync-Professor-THC-win-x64.zip`.
3. Execute **INSTALAR-INTERFACE-THC.bat** e aceite a elevação administrativa do Windows.
4. A janela abre no final da instalação. Nas próximas vezes, use **VeyonSync THC**, na Área de Trabalho ou no menu Iniciar.

O runtime .NET está incluído. O professor não precisa compilar a interface nem instalar o SDK para abrir o pacote pronto.

## O que aparece

- Visão geral e computadores do banco real da etapa 2, em tabela ou grade.
- Pesquisa, detalhes, histórico e exportação CSV.
- Pareamento: código de uso único gerado pelo executável já instalado.
- Botão para salvar o arquivo público `.vsync` e levá-lo aos alunos.
- Solicitação de coleta para todos os alunos, conforme os comandos disponíveis na etapa 2.
- Diagnóstico, temas e indicação do estado real do serviço.

O inventário é lido em modo somente leitura; a interface não cria nem migra o banco da etapa 2. O instalador copia somente a interface para `C:\Program Files\VeyonSync\Teacher\THC` e cria atalhos. Não para nem substitui o serviço, não troca certificados, não altera o laboratório e não apaga pareamentos.

## Integração Veyon

As ações de integração usam a API HTTPS autenticada **do serviço da etapa 2**. Em Configurações, é possível vincular uma chave administrativa existente ou gerar uma nova chave local após confirmação. A nova chave invalida a chave administrativa anterior e pode exigir reconfigurar painéis centrais; não altera os códigos ou tokens dos alunos. A chave local fica protegida por DPAPI e ACL administrativa e não aparece nos diagnósticos.

Primeiro selecione um computador, gere a prévia e confira o que será aplicado. O complemento respeita as regras e limitações do integrador da etapa 2 instalado; não substitui essa implementação. Se o serviço não validar o `veyon-cli`, a interface informa a falha e não força a operação. Restauração e revogação individual de agentes não são acrescentadas ao serviço por este pacote. A versão atual não oferece painel central de vários professores nem mapa físico do laboratório.

## Se a janela não abrir

- Confira se o ZIP foi extraído por completo.
- Confira se está no computador do professor e se o serviço `VeyonSyncTeacher` existe.
- Use o atalho novo **VeyonSync THC**, não o executável de serviço `VeyonSync.Teacher.exe`.
- A interface é administrativa e pede elevação pelo Windows.
- Se o PowerShell bloquear o instalador, confira a origem e o checksum, desbloqueie o ZIP pelas Propriedades antes de extraí-lo e respeite a política da sua organização.
- Se ainda falhar, copie a mensagem do erro. Não apague `C:\ProgramData\VeyonSync`.

Fechar a janela mantém o serviço funcionando. A disponibilidade no Veyon Master permanece separada da confirmação de recebimento e da aplicação do cadastro.
