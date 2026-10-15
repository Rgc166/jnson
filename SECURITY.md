# Segurança

Versão em pré-lançamento, ainda sem garantia de uso em produção. Instale primeiro em máquinas de teste e confira [docs/validation.md](docs/validation.md).

Relatos de vulnerabilidade devem ser privados. Se **Security → Report a vulnerability** estiver habilitado no GitHub, use esse canal. Caso contrário, solicite ao proprietário um canal privado sem publicar detalhes da exploração, credenciais ou diagnósticos sensíveis em uma issue pública.

Nunca publique chaves privadas, tokens, códigos de pareamento, bancos ou arquivos locais. Diagnósticos podem identificar usuários, máquinas e redes; revise-os antes de compartilhar.

O VeyonSync não protege dados contra administradores locais. Os serviços executam como SYSTEM e as interfaces administrativas exigem UAC. A proteção depende de ACLs corretas, origem confiável dos instaladores, certificados conferidos e política de acesso aos computadores.
