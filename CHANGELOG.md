# Changelog

## 0.2.0 — em preparação

- Estrutura de solução com Core, serviços e interfaces WPF separadas por função.
- Temas escuro, claro e Windows; inventário, busca, histórico, pareamento, diagnóstico e configurações.
- Comunicação administrativa por named pipes restritos a Administradores/SYSTEM.
- Confiança TLS restrita à aplicação, códigos de uso único, limite de tentativas e revogação.
- Sequência persistente, gravações atômicas e transações no inventário/histórico.
- Adaptador Veyon com prévia, backup, verificação e restauração explícita.
- Dependências atualizadas, lockfiles, testes e workflow para pacotes Windows.
- Instaladores por script separados, reparo com preservação dos dados e logs TXT.

O comportamento no Windows e a integração com a instalação real do Veyon ainda dependem da validação descrita em `docs/validation.md`.
