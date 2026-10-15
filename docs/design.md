# Interface e identidade visual

Referência: proposta UI/UX enviada pelo proprietário em 15/10/2026. Tema escuro padrão, fundo `#0B1120`, navegação `#111827`, cartões `#1E293B`, primária `#3B82F6`, texto `#F8FAFC` e secundário `#94A3B8`. No claro: fundo `#F8FAFC`, cartões brancos, primária `#2563EB` e texto `#0F172A`. Textos auxiliares usam contraste reforçado.

WPF com recursos compartilhados, Segoe UI Variable/Segoe UI, cantos de 8–10 unidades e separação da lógica operacional. `ShellViewModel` administra snapshots e operações; a janela compõe controles WPF e não executa coleta nem SQL. As janelas têm manifesto PerMonitorV2 e navegação por teclado. As transições de telas (180 ms), temas (240 ms) e menu (220 ms) respeitam `SystemParameters.ClientAreaAnimation`; efeitos de transparência e animações contínuas foram evitados.

## Professor

Menu recolhível: Dashboard, Computadores, Laboratórios, Integração, Histórico, Pareamento, Diagnóstico e Configurações. A seção Integração foi adicionada para expor prévia, confirmação e backup sem misturar isso ao recebimento dos relatórios.

Dashboard com totais reais e última comunicação. Computadores em grade ou tabela com busca, filtros, ordenação e detalhes por duplo clique ou Enter. O histórico mostra os últimos 500 eventos consultados, com busca, filtro por tipo e data inicial. Exportação respeita os filtros e protege células CSV contra interpretação como fórmulas.

O professor tem um laboratório configurado, sem troca silenciosa para outra sala. Pareamento mostra código, validade e consumo. Integração apresenta o IP anterior e o novo antes de pedir confirmação. Restauração alerta que afeta toda a configuração Veyon, não apenas um PC.

## Aluno

Janela compacta com estado real, último envio confirmado, dados da última coleta, integração e erro. Assistente em seis etapas mantém os campos na mesma janela após falhas, mostra a impressão digital do certificado e exige confirmação para substituir pareamento. Configurações sensíveis ficam administrativas.

## Limites visuais desta entrega

Ainda é necessário avaliar visualmente a interface no Windows. Bandeja do sistema, animação de cartões individuais, exportação PDF/DOCX e notificações do Windows ficam para uma iteração posterior. A versão atual oferece notificações agregadas dentro da janela, CSV e diagnóstico TXT. Não há screenshots reais nem resultados visuais inventados.
