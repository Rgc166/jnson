# Integração Veyon

Esta implementação usa comandos documentados: `config export`, `config get BuiltinDirectory/NetworkObjects`, `config set BuiltinDirectory/NetworkObjects <JSON> json` e `config import` para restauração explícita. Não utiliza `networkobjects clear`, comandos `modify` inventados nem execução pelo shell.

Fontes consultadas:

- [Documentação oficial do CLI](https://docs.veyon.io/en/latest/admin/cli.html).
- [Implementação oficial do diretório interno](https://github.com/veyon/veyon/blob/1cebf45f6c1af8bab2c96278a58db1cf2f56685d/plugins/builtindirectory/BuiltinDirectoryPlugin.cpp).

## Isolamento dos cadastros

A localização dedicada é `VeyonSync-<laboratório>`. O nome estável de cada computador é `VS-<laboratório>-<UUID completo do agente>`. A posição amigável permanece no VeyonSync; não é usada como identidade para evitar duplicação quando um PC muda de lugar. A integração não adota cadastros preexistentes com nomes coincidentes. Cadastros antigos do Veyon com outros nomes não são automaticamente reconciliados: planeje a migração para não manter duas entradas da mesma máquina.

O CLI de importação CSV calcula a identidade a partir de nome, IP e MAC e pode duplicar entradas quando esses dados mudam. Por isso, a aplicação altera a chave de configuração do diretório com JSON, preservando o UUID existente e os demais campos; não usa importação CSV para atualizar máquinas.

O arquivo administrativo `veyon-owned.json` relaciona agentes e UUIDs dos objetos criados. O adaptador exige o formato exportado `BuiltinDirectory/NetworkObjects`, tanto array direto quanto o wrapper `JsonStoreArray` do exportador oficial, valida tipos, localização e propriedade. Versões ou diretórios diferentes, como LDAP, não são considerados compatíveis automaticamente. O teste com o CLI realmente instalado é obrigatório antes da distribuição.

## Aplicação

1. Exporta configuração para inspeção e produz uma prévia real.
2. Na confirmação, exporta um backup e refaz a prévia; dados diferentes exigem nova confirmação.
3. Confere se a configuração não mudou imediatamente antes da escrita.
4. Grava a chave `BuiltinDirectory/NetworkObjects` com a lista preservada e somente o objeto planejado alterado. Mantém o UUID em atualizações e cria UUIDs apenas para cadastros novos.
5. Exporta de novo, confere endereço/MAC e verifica que objetos não relacionados foram preservados.
6. Só então marca o cadastro como aplicado.

O CLI não oferece uma transação compartilhada com o Configurator. Feche o Configurator e evite alterações simultâneas por outros administradores. A conferência detecta divergências, mas não impede toda corrida com processos externos. Uma falha depois da escrita preserva o backup e é registrada; não restaura a configuração inteira automaticamente. Se um cadastro foi criado, mas sua propriedade não pôde ser registrada, será necessário revisar esse objeto antes de tentar novamente.

## Backup e restauração

Backups em `%ProgramData%\VeyonSync\Teacher\backups`. Podem conter configurações sensíveis do Veyon e recebem a mesma ACL restrita. A lista de restauração aceita somente arquivos gerados nessa pasta. **Restaurar usa `config import` e afeta toda a configuração Veyon contida naquele backup**, inclusive alterações externas posteriores. A interface exige confirmação e cria outra cópia antes de restaurar.

Após a restauração, os estados da integração são invalidados para exigir conferência. Preserve backups conforme a política do laboratório; a versão atual não os apaga automaticamente.

Para respeitar o limite de linha de comando do Windows, diretórios cujo JSON ultrapasse o limite seguro de 30 mil caracteres com escapes são bloqueados antes da escrita. Uma versão futura pode oferecer um caminho baseado em arquivo para diretórios maiores.
