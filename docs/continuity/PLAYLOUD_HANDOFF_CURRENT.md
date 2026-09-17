# Playloud Informática — Continuidade atual

Data: 2026-09-17

## Repo / branch
- Repo: `nexoinfo801-ship-it/nexo-informatica`
- Branch: `feat/playloud-foundation-client`
- NÃO trabalhar em `main`.
- HEAD ao criar este handoff: `261e8e33b06f8474adfdbbca00fd65c3ca9b241d`

## Último baseline totalmente GREEN
Commit: `5dec5354c1f6564a516ed5746effa9e6090098d5`
- Windows/.NET 10 PASS
- 0 warnings
- 0 errors
- 52/52 testes

## Estado exato atual
A shell comercial do Playloud Cliente já foi criada em `src/Playloud.Cliente/Shell/MainWindow.xaml`.

O commit `03c983e60343ad85f60d988b3a9760431c5333dd` falhou porque WPF não suporta `TextBlock.CharacterSpacing`. Isso foi corrigido no HEAD `261e8e33...`.

O CI do HEAD ainda falha no build por este binding em `MainWindow.xaml`, aproximadamente linha 482:

```xml
Text="{Binding LastTotal, StringFormat={}{0:C2}, TargetNullValue=R$ 0,00}"
```

Erro: `MC3042`.
Causa: a vírgula no `TargetNullValue` quebra a MarkupExtension.

Primeira tarefa ao retomar:
1. confirmar o erro no log;
2. corrigir somente a causa raiz;
3. rodar restore/build/test completos;
4. só declarar GREEN com 0 warnings, 0 errors e todos os testes PASS;
5. expectativa atual: 53/53 testes.

## Processo obrigatório
`achar erro → reproduzir → corrigir → regressão → revisar → avançar`

Aplicar em todas as partes:
`/frame /code /api /critic /promptenginner /researchch /dig /improve /gaps /risk /debug /reboot /automate /outlin /prot /cio /dono /pro /expert /ceo`

TDD real: RED → confirmar causa → GREEN mínimo → regressão → revisão.
Nunca desativar analyzer/teste para passar CI.

## Produto
Marca: **Playloud Informática — PDV & ERP**

Produtos:
- Playloud Cliente
- Playloud Master
- Playloud Assist

Foco atual: fundação + Cliente primeiro; Master e Assist depois sobre a mesma base.

## Stack
- C#
- .NET 10
- WPF/XAML
- SQLite
- Windows 10/11 x64
- C++ apenas quando necessário para SDK/driver/interop nativo

Não voltar para Electron/Node/Python como núcleo.

## Dependências
- Domain não conhece WPF/SQLite/Windows
- Application define casos de uso e ports
- Persistence implementa infraestrutura
- Cliente WPF usa ViewModel/Application
- WPF nunca acessa SQLite diretamente

## Já implementado
Domínio:
- Product/ProductCatalog
- IDs estáveis
- estoque sem negativo
- venda não excede estoque
- snapshots imutáveis de produto/preço/custo
- desconto 0% válido
- data comercial explícita
- pagamentos: dinheiro, PIX, débito, crédito, misto
- venda duplicada bloqueada
- fechamento de caixa
- suprimento/sangria com motivo
- caixa fechado bloqueia movimento

SQLite:
- WAL
- foreign keys por conexão
- transações explícitas
- venda + itens + pagamentos + estoque + caixa em uma transação
- rollback total
- ledger de ajustes
- fechamento persistente

Application:
- `FinalizarVenda`
- `IProductSnapshotReader`
- `ISaleCommitter`
- `IFinalizarVenda`
- `SqliteCommerceAdapter`

Cliente:
- `SaleCheckoutViewModel`
- `INotifyPropertyChanged`
- `ClienteRuntime`
- `ClienteApplicationHost`
- startup STA
- App sem `StartupUri`
- DB em `%LocalAppData%\Playloud\Cliente\Data\playloud-cliente.db`

## Shell visual
Regiões testadas:
- `NavigationRail`
- `OperationalHeader`
- `SalesWorkspace`
- `CheckoutStatusCard`

Paleta atual:
- `#070B12`
- `#0D131D`
- `#121A26`
- `#162131`
- `#223044`
- `#2F80ED`
- `#182E4E`
- `#F7F9FC`
- `#A7B2C3`
- `#748196`
- `#4CC38A`

A shell já contém navegação para PDV, Estoque, Clientes, Financeiro, Relatórios, Configurações e Atualizações, mais status do runtime, área da venda, busca preparada, área de itens preparada e status real ligado ao ViewModel.

## Próxima sequência após GREEN
1. catálogo/busca real de produtos
2. carrinho real
3. quantidade + proteção de estoque
4. remoção segura
5. subtotal/total/desconto
6. pagamentos
7. finalizar via `IFinalizarVenda`
8. integrar à shell sem lógica de negócio no code-behind
9. atalhos e DPI 100/125/150
10. Safe Release/Atualizações

Usuário não deve manipular IDs técnicos.

## Requisitos permanentes futuros
AutoDebug: Playloud.Diagnostics com recorder, exception/crash monitor, probes, classifier, sanitizer, repair engine e evidence packager. P0–P4. Ações críticas exigem confirmação.

Safe Release: manifesto assinado → SHA-256 → compatibilidade → backup → stage → instalação → health check → PASS/rollback.

Playloud Assist: suporte automático + vendas + pós-venda + onboarding + renovação + CIO Intelligence. CIO é capacidade de análise, não privilégio irrestrito.

Master: clientes, licenças, planos, assinaturas, vendas, suporte, atualizações. MASTER assina; Cliente recebe chave pública, nunca chave privada.

Instalador final: normal, pasta/unidade selecionável, atalhos, uninstall/repair, sem console, Authenticode comercial. Não entregar produto final como CMD/ZIP portátil.

## UX
Azul + preto + branco; profissional, comercial, leve, alinhado; ícones com legenda; sem visual infantil; DPI 100/125/150.

## Governança
Everton = PO/decisor.
ChatGPT = engenharia.
Guardião = segurança/banco/gates.
VISION = produto/UX.
PRIME = governança.

Quando Everton disser `continue` ou `autorizado`, prosseguir sem perguntar repetidamente. Ações críticas/destrutivas continuam approval-gated.
