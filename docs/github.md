# Preparar o GitHub e o lançamento

Os arquivos de apresentação, templates e workflows estão no repositório. Configurações da conta/repositório e publicação não são alteradas apenas por criar esses arquivos.

## Enquanto privado

Na página do repositório, use uma descrição curta: **“Inventário e sincronização de computadores de laboratório com serviços Windows, WPF e integração Veyon.”** Topics sugeridos: `csharp`, `dotnet`, `wpf`, `windows`, `veyon`, `inventory`, `education`.

Em **Settings → General**, habilite Issues se quiser receber relatos e preserve o repositório privado. Em **Settings → Actions**, autorize os workflows conforme sua política. O workflow tem permissão somente de leitura e não publica releases nem envia pacotes para serviços externos. Artefatos de um repositório privado seguem as regras de acesso do GitHub.

Depois da primeira execução bem-sucedida, configure proteção da branch principal exigindo os checks `core` e `windows`, se disponíveis no seu plano. O job Windows instala serviços em runner descartável. Ele não testa uma instalação real do Veyon nem substitui a validação visual.

O Dependabot propõe atualizações de dependências e Actions. Leia os PRs, confira os lockfiles e execute os testes antes de integrar. Ações externas estão fixadas por versão principal; se sua política exigir referências imutáveis, fixe SHAs verificados antes de publicar.

## Antes de tornar público

1. Execute o roteiro Windows e registre versão do sistema, Veyon e resultados reais.
2. Capture screenshots usando dados de teste sem identificar usuários ou rede reais.
3. Confira `git status`, arquivos ignorados e o histórico completo por credenciais e dados locais. Se houver segredo exposto, revogue-o antes de reescrever o histórico.
4. Escolha uma licença com o proprietário. Este repositório ainda não concede uma licença open source.
5. Revise nome, descrição, README, notas de versão e limitações.
6. Gere os pacotes e checksums a partir do commit validado. Se houver assinatura de código, use um certificado legítimo em armazenamento seguro.
7. Publique a release e altere a visibilidade somente quando decidir fazê-lo. Nenhuma dessas ações foi feita automaticamente.

O banner em SVG já está pronto para a página. Use uma imagem PNG/JPEG exportada dele, dentro dos limites do GitHub, caso queira configurar **Social preview** manualmente.
