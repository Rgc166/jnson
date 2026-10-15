# Instalação e diagnóstico

## Não consigo abrir a interface

As duas interfaces exigem elevação. Confira a política UAC e se há credenciais administrativas autorizadas. Não existe senha de pasta nem senha administrativa salva pelo VeyonSync.

Se o serviço estiver ausente, instale o pacote da função correspondente. Se estiver parado, consulte `Get-Service VeyonSyncTeacher,VeyonSyncStudent` e os eventos de Aplicativo no Visualizador de Eventos. Use **Diagnóstico** para exportar o estado disponível em TXT. A interface não transforma falha de comunicação com o serviço em lista vazia de máquinas conectadas.

## Logs de instalação

Transcrições em `%ProgramData%\VeyonSync\install-logs`, com ACL administrativa e no máximo vinte arquivos. Elas não incluem códigos de pareamento nem tokens. Erros de permissão antes da criação do log aparecem no console. Revise diagnósticos antes de compartilhar: nomes de usuários, computadores e endereços são informações locais.

## Professor inacessível

No aluno, execute `Resolve-DnsName NOME-DO-PROFESSOR` e `Test-NetConnection NOME-DO-PROFESSOR -Port 5443`. Uma porta aberta não comprova TLS nem pareamento. Confirme o perfil de rede e a sub-rede: o firewall do instalador permite Domínio/Privado e `LocalSubnet`.

No aluno, **Testar conexão segura** consulta `/health`, valida o certificado e confere o laboratório. Isso não envia um relatório nem comprova integração Veyon. Mantenha o horário do Windows correto.

## Falha TLS

Confira nome do professor, SAN do certificado, validade, impressão digital e relógio. Um IP só funciona se o certificado contiver esse IP no SAN. O instalador gera certificado para o nome do Windows; usar esse nome é o fluxo padrão.

Renomear o professor não atualiza o certificado. Repare a instalação com `install-teacher.ps1 -Lab B -RotateCertificate`, distribua novamente apenas o certificado público e pareie de novo os alunos. A rotação é explícita; o instalador não substitui o certificado silenciosamente.

Nunca use `-SkipCertificateCheck`, callbacks que retornam sempre verdadeiro ou raízes de procedência desconhecida. O aluno confia no certificado escolhido dentro da aplicação. Certificados que uma instalação antiga tenha importado no Windows não são removidos automaticamente: confira uso e propriedade antes de removê-los.

## Código recusado

Cada código tem oito números e autoriza um único aluno. Expira em quinze minutos; gerar outro cancela o anterior. Confira o laboratório e gere um novo código. Muitas tentativas podem produzir HTTP 429; aguarde um minuto.

## Recebido, mas não aparece no Veyon

Recebimento e integração são etapas separadas. No professor, configure `veyon-cli.exe`, abra **Integração**, confira a prévia e aplique. Não se afirma disponibilidade no Veyon Master sem medição. Consulte [veyon.md](veyon.md).

## Atualização e reparo

Feche a interface e execute o instalador correspondente novamente com os mesmos parâmetros. Ele para o serviço, arquiva dados, atualiza os binários, reaplica ACL e verifica uma resposta administrativa. Professor também testa o endpoint HTTPS. Configurações, token, identidade, estado pendente e histórico não são apagados para forçar sucesso.

Uma troca de laboratório não é executada pelo reparo. Para mudar a atribuição, planeje uma migração administrativa, preserve os dados e faça novo pareamento. O aplicativo não transfere agentes automaticamente. Se a instalação falhar, o log informa a etapa; não há promessa de rollback automático de binários. Os dados anteriores ficam no backup de atualização.

Remoção: `./uninstall.ps1 -Role Student` ou `Teacher`. Os dados são preservados por padrão. `-RemoveData` apaga os dados daquela função após confirmação. A remoção não apaga certificados de outros produtos.
