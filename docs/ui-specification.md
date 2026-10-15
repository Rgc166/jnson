> Especificação original fornecida pelo proprietário. Para o estado implementado e as limitações, consulte [design.md](design.md) e [validation.md](validation.md).

# VEYONSYNC | PROPOSTA COMPLETA DE INTERFACE GRÁFICA (UI/UX)

## 1. Objetivo geral

Desenvolver duas interfaces gráficas para o VeyonSync, mantendo a arquitetura já existente de comunicação entre computadores, certificados TLS, pareamento e armazenamento de histórico.

As duas aplicações deverão utilizar a mesma identidade visual, mas possuir funcionalidades adequadas a seus respectivos usuários.

- **VeyonSync Professor:** painel administrativo para monitoramento, cadastro, organização e histórico dos computadores.
- **VeyonSync Aluno:** aplicação compacta para visualizar conexão, realizar pareamento e acompanhar sincronizações.

O design deve seguir os princípios de minimalismo, modernidade, legibilidade, desempenho e facilidade de utilização.

O visual deve transmitir a sensação de um software profissional desenvolvido para gerenciamento de laboratórios de informática.

## 2. Identidade visual

### Estilo

Adotar uma estética **Minimal Tech**, com elementos inspirados em aplicações modernas de produtividade e gerenciamento de infraestrutura.

Características:

- Interface limpa, sem excesso de informações.
- Cantos arredondados entre 8 e 12 pixels.
- Espaçamento consistente entre componentes.
- Tipografia moderna, preferencialmente Segoe UI Variable.
- Ícones vetoriais finos e minimalistas.
- Bordas discretas e sombras suaves.
- Utilização moderada de efeitos de transparência.
- Animações rápidas e fluidas.
- Layout adaptável a diferentes resoluções.

Evitar elementos visuais desnecessários, efeitos exagerados, cores excessivamente saturadas e animações que prejudiquem o desempenho.

### Paleta de cores

**Tema escuro, padrão:**

| Elemento | Cor |
|---|---|
| Fundo principal | `#0B1120` |
| Menu lateral | `#111827` |
| Cartões | `#1E293B` |
| Cor primária | `#3B82F6` |
| Azul de destaque | `#60A5FA` |
| Texto principal | `#F8FAFC` |
| Texto secundário | `#94A3B8` |
| Sucesso | `#22C55E` |
| Alerta | `#F59E0B` |
| Erro | `#EF4444` |

**Tema claro:**

| Elemento | Cor |
|---|---|
| Fundo principal | `#F8FAFC` |
| Menu lateral | `#FFFFFF` |
| Cartões | `#FFFFFF` |
| Cor primária | `#2563EB` |
| Texto principal | `#0F172A` |
| Texto secundário | `#64748B` |

Permitir alternância manual entre os temas e oferecer a opção de seguir automaticamente o tema do Windows.

A mudança deve ocorrer com uma transição suave de aproximadamente 200 a 300 ms.

## 3. Interface do Professor

O VeyonSync Professor será a principal aplicação administrativa.

### Estrutura da janela

Dividir a interface em três regiões:

**Menu lateral esquerdo**

Exibir logotipo e nome VeyonSync na parte superior.

Itens:

1. Dashboard
2. Computadores
3. Laboratórios
4. Histórico
5. Pareamento
6. Diagnóstico
7. Configurações

O menu deve ser recolhível.

Quando recolhido, exibir apenas ícones com tooltips.

**Barra superior**

Exibir:

- Nome do laboratório atual.
- Nome do computador professor.
- Relógio digital discreto.
- Indicador de funcionamento do serviço.
- Botão para alternar entre tema claro e escuro.
- Acesso às configurações.

**Área central**

Exibir o conteúdo correspondente à seção selecionada, com cartões, tabelas e controles organizados.

### Dashboard

A tela inicial deverá apresentar um resumo do estado do laboratório.

Cartões principais:

- Total de computadores cadastrados.
- Computadores conectados.
- Computadores desconectados.
- Computadores com problemas de sincronização.
- Última sincronização realizada.

Abaixo dos cartões, apresentar a lista de computadores.

Cada computador deverá ter um cartão contendo:

- Nome da máquina.
- Posição física, como PC01 ou PC03.
- Endereço IP, quando disponível.
- Endereço MAC, quando disponível.
- Estado da conexão.
- Horário da última comunicação.
- Botão para visualizar detalhes.

Estados visuais:

- Verde: conectado e sincronizado.
- Amarelo: aguardando conexão ou atualização.
- Vermelho: erro confirmado.
- Cinza: desconectado ou sem dados recentes.

Não utilizar o status verde apenas porque o serviço Windows está iniciado. A aplicação deve confirmar a comunicação com o professor.

### Computadores

Permitir dois modos de visualização:

**Grade:** cartões minimalistas representando cada computador.

**Lista:** tabela com informações detalhadas, filtros e ordenação.

Disponibilizar:

- Pesquisa instantânea por nome.
- Filtro por laboratório.
- Filtro por estado.
- Ordenação por posição.
- Visualização de detalhes.
- Histórico individual.
- Exportação das informações.

A atualização da lista deve ocorrer automaticamente, sem exigir que o usuário atualize a página.

### Laboratórios

Cada laboratório deverá possuir uma configuração independente.

Exemplos:

- Laboratório A
- Laboratório B
- Laboratório C

Cada computador professor deve gerenciar seu próprio conjunto de máquinas.

O sistema não deve presumir que o nome do Windows corresponde ao laboratório físico.

Permitir definir manualmente o laboratório durante a configuração.

A troca de laboratório não pode transferir alunos ou certificados automaticamente para outro professor.

### Histórico

Criar uma seção para consultar os registros de sincronização e eventos.

Informações recomendadas:

- Computador.
- Laboratório.
- Data e horário.
- Tipo de evento.
- Resultado.
- Detalhes técnicos.

Exibir filtros por período, máquina e tipo de evento.

Permitir exportação para CSV.

O histórico deve ser preservado entre reinicializações e atualizações.

### Pareamento

Criar uma interface específica para adicionar computadores.

Disponibilizar um botão:

**Gerar código de pareamento**

Após clicar, mostrar:

- Código temporário.
- Tempo restante de validade.
- Nome do professor.
- Laboratório correspondente.
- Status do pareamento.

O código deve expirar automaticamente conforme a regra existente de 15 minutos.

Mostrar confirmação visual quando um aluno for pareado corretamente.

Evitar expor códigos de pareamento em logs comuns.

### Diagnóstico

Criar uma área de diagnóstico técnico com verificações automáticas:

- Serviço do professor.
- Porta de comunicação.
- Certificado TLS.
- Validade do certificado.
- Nome do computador.
- Banco de dados.
- Comunicação com alunos.
- Últimos erros registrados.

Apresentar explicações acessíveis ao lado dos detalhes técnicos.

Exemplo:

**Falha na conexão segura**

O certificado apresentado pelo professor não corresponde ao nome utilizado pelo aluno.

Ações sugeridas:

- Conferir o nome do professor.
- Verificar o certificado.
- Exportar diagnóstico.

Nunca corrigir problemas de TLS desativando a validação de certificados.

## 4. Interface do Aluno

A aplicação do aluno deverá ser extremamente simples.

Não precisa reproduzir todo o painel administrativo do professor.

### Tela principal

Exibir:

**VEYONSYNC**

Status: Conectado ao professor

Informações:

- Laboratório: B
- Computador: PC03
- Professor: PROFESSOR-LAB-B
- Última sincronização: 14:32
- Estado: Confirmado pelo professor

Abaixo, disponibilizar:

- Sincronizar agora.
- Ver detalhes da conexão.
- Diagnóstico.
- Configurações permitidas.

Não permitir que usuários comuns alterem informações administrativas sensíveis sem autorização.

### Pareamento inicial

Na primeira execução, abrir um assistente com etapas simples.

**Etapa 1:** identificar o laboratório.

**Etapa 2:** informar o nome ou endereço IP do professor.

**Etapa 3:** selecionar o certificado público do professor.

**Etapa 4:** informar a posição da máquina.

**Etapa 5:** inserir o código de pareamento.

**Etapa 6:** testar e confirmar a conexão.

Exibir uma barra de progresso indicando a etapa atual.

Se ocorrer um erro, manter os dados preenchidos para que o usuário não precise começar novamente.

### Funcionamento em segundo plano

O serviço do aluno deverá continuar funcionando independentemente da interface gráfica.

Fechar a janela não deve encerrar a comunicação.

O aplicativo poderá utilizar a bandeja do sistema Windows, quando permitido.

A interface deve indicar claramente quando o serviço está parado.

## 5. Interações e microanimações

As animações devem dar feedback às ações, não servir apenas como decoração.

Aplicar:

- Hover suave em botões e cartões: 120 a 180 ms.
- Transição entre telas: 180 a 250 ms.
- Expansão e recolhimento do menu lateral: aproximadamente 220 ms.
- Alternância entre temas: 200 a 300 ms.
- Indicador discreto durante sincronizações.
- Animação de confirmação ao concluir um pareamento.
- Mensagens temporárias de sucesso, aviso e erro.
- Indicadores de carregamento em operações demoradas.
- Animação de entrada de novos computadores na lista.

Evitar animações contínuas que consumam CPU desnecessariamente.

Respeitar a configuração de redução de movimento do Windows.

Não utilizar indicadores infinitos sem informar o estado real da operação.

## 6. Notificações

Criar notificações discretas para eventos relevantes:

- Computador conectado.
- Computador desconectado.
- Novo pareamento concluído.
- Falha de sincronização.
- Certificado próximo do vencimento.
- Serviço interrompido.

Evitar notificações repetitivas.

Agrupar eventos semelhantes quando muitos computadores forem atualizados simultaneamente.

## 7. Tecnologias recomendadas

Como o VeyonSync já utiliza C# e .NET 10, recomenda-se avaliar **WPF** para a interface gráfica do Windows.

Motivos:

- Integração com Windows.
- Compatibilidade com C#.
- Suporte a MVVM.
- Data binding.
- Interface rica com XAML.
- Possibilidade de desenvolver componentes reutilizáveis.
- Bom controle sobre consumo de recursos.

Manter a interface separada dos serviços responsáveis pela comunicação e persistência.

Não reescrever desnecessariamente a lógica existente.

## 8. Arquitetura e segurança

A implementação da UI não deve comprometer o funcionamento atual.

Requisitos:

- Preservar os serviços existentes.
- Preservar o banco de dados e histórico.
- Manter a validação TLS.
- Não armazenar códigos temporários em texto puro nos logs.
- Restringir operações administrativas.
- Não permitir que a interface do aluno controle o professor.
- Não permitir que uma máquina seja atribuída silenciosamente a outro laboratório.
- Preservar os dados durante atualizações.
- Exibir mensagens de erro úteis.

A integração futura com o Veyon CLI deverá ser feita por uma camada de serviço independente, e não diretamente pelos componentes visuais.

## 9. Instalação e distribuição

Criar instaladores separados:

**VeyonSync Professor**

Instala serviço, interface administrativa e configurações necessárias.

**VeyonSync Aluno**

Instala serviço e interface simplificada.

O instalador do aluno deverá ser universal e funcionar em qualquer laboratório mediante configuração.

Os instaladores deverão oferecer:

- Verificação de requisitos.
- Identificação de instalação anterior.
- Instalação e reparo.
- Logs de diagnóstico em TXT.
- Preservação das configurações.
- Mensagens claras de erro.
- Opção de desinstalação.

## 10. Resultado esperado

Entregar duas aplicações Windows com identidade visual consistente, navegação intuitiva, animações discretas e funcionamento eficiente.

A interface do professor deve facilitar a administração de vários computadores.

A interface do aluno deve oferecer apenas as informações e ações necessárias.

O resultado deve parecer um software moderno e confiável para uso real em laboratórios de informática.

**Prioridade de desenvolvimento:**

1. Corrigir problemas existentes de TLS e dependências vulneráveis.
2. Garantir comunicação e pareamento confiáveis.
3. Implementar a estrutura visual das duas interfaces.
4. Integrar os dados reais dos serviços.
5. Implementar histórico, diagnósticos e notificações.
6. Refinar animações e experiência visual.
7. Preparar instaladores e testes de distribuição.

Não apresentar dados simulados como se fossem informações reais de computadores conectados.