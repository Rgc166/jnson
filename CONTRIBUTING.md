# Contribuir

Instale o SDK fixado em `global.json`. Execute restore em modo bloqueado, build da solução e os testes em `tests/VeyonSync.Tests`. Alterações de dependências devem atualizar os lockfiles intencionalmente e passar pela auditoria NuGet. Não suprima alertas de segurança para fazer a compilação passar.

Antes de propor mudanças, descreva o comportamento que precisa mudar. Inclua testes para persistência, autorização, comunicação e integração quando afetados. Mudanças de UI devem incluir validação no Windows, teclado, DPI e screenshots reais sem dados pessoais.

Não inclua certificados privados, tokens, códigos de pareamento, arquivos de configuração locais, bancos nem diagnósticos de produção. Não faça alterações que limpem todo o diretório do Veyon, transfiram laboratórios silenciosamente ou desativem TLS.

Use os templates de issue e pull request. A licença de distribuição ainda depende da decisão do proprietário; não presuma autorização para redistribuição pública.
