# Workflow
- Typecheck (`dotnet build`) when done making code changes
- Run tests: `dotnet test "FinancialEngineCore.Tests\FinancialEngineCore.Tests.csproj"`
- Build: `dotnet build "kubera_webapp\kubera_webapp.sln"`
- Frontend build: `cd C:\Projetos\outros\kubera\kubera && npx ng build`
- Detailed domain rules: see `domain-rules.md`

# Kubera - Sistema de Billing

Sistema de cobranca financeira sobre operacoes de bolsa. Ordens vem de sistema externo, Kubera calcula fees por (BillingKey, Broker, Dia, AssetClass).

## Entidades Principais
- **Order** — operacao de bolsa (qty, price, symbol, trader, entity; asset (raiz) vem dos trades via view). AssetClass deduzida no codigo por `AssetClassifier`; sinteticos (BTGDOLZ26) quebrados por perna na view
- **EquityFeeBracket** — faixas EQUITY progressivas (Broker, Trader?, NotionalFrom/To, Bps, Cap, ValidFrom/To)
- **BmfFeeConfig** — config BMF por contrato (Broker, Trader?, Mini, Cheio, DI1 por vencimento, CapMensal)
- **PriceModificationFactor** — ajuste de preco por ativo (Symbol, Factor) para EQUITY
- **EntityBillingConfig** — agrupa billing por Entity ao inves de Trader
- **FixedMonthlyFee** — cobranca fixa mensal por broker (Description, Amount, ValidFrom/To)
- **DailyBilling** — resultado agregado do dia + **OrderBilling** (fee proporcional por ordem)
- **BracketFeeTracking** — fee cobrado por faixa EQUITY (para CAPs acumulados)

## Regras Core
- **EQUITY**: cobranca progressiva por faixas de notional, com BPS + CAP por faixa, acumulado mensal
- **BMF**: cobranca por qtd de contratos (Mini/Cheio/DI1 por vencimento/Demais = ValorOutros), CAP mensal unico
- **BillingKey**: Entity (se EntityBillingConfig) ou EnteringTrader (padrao)
- **Trader nullable**: NULL = regra padrao, preenchido = 100% custom (sem fallback)
- **Validade**: todas as configs tem ValidFrom/ValidTo

## Arquitetura
- .NET 10, Dapper, PostgreSQL
- **BillingWorker** (BackgroundService) -> poll jobs via FOR UPDATE SKIP LOCKED
- **BillingProcessingService** -> preload context (5 queries paralelas) -> agrupa por (BillingKey, AssetClass) -> transacao
- **Strategy Pattern**: ICalculationStrategy (EQUITY, BMF) -> CalculationStrategyRegistry
- **Strategies**: EquityCalculationStrategy, BmfCalculationStrategy (calculation logic as public static methods, recebem billingKey como param)
- **Performance**: parallel brokers (max 4), cascade delete, bracket resolution em memoria

## Estrutura de Arquivos
```
FinancialEngineCore/
  Model/          Order, EquityFeeBracket, BmfFeeConfig, DailyBilling, OrderBilling,
                  BracketFeeTracking, PriceModificationFactor, EntityBillingConfig,
                  FixedMonthlyFee, ProcessBilling, BrokerModel, OrderBillingDetail
  Domain/
    Services/     BillingProcessingService (orchestrador principal)
    Strategies/   ICalculationStrategy, EquityCalculationStrategy, BmfCalculationStrategy, Registry
    BillingCalculationResult.cs
    BrokerBillingContext.cs   (pre-loaded data, resolve special vs default)
  Infra/          DbConnectionFactory + Repositories (Dapper, sem interfaces)
  BillingWorker.cs

kubera_webapp/
  Program.cs      DI + endpoints
  Controllers/    ReportController, FinancialController, BrokerController,
                  PriceModificationFactorController, EntityBillingConfigController,
                  EquityBracketController, BmfConfigController,
                  FixedMonthlyFeeController, SeedDataController
  Services/       MonthlyPdfReportService

FinancialEngineCore.Tests/
  EquityCalculationStrategyTests.cs (19 testes)
  BmfCalculationStrategyTests.cs    (12 testes)
  BillingKeyResolutionTests.cs      (6 testes)
  Integration/
    DatabaseFixture.cs              (Testcontainers PostgreSQL)
    BillingIntegrationTests.cs      (7 testes e2e)
    init.sql -> link p/ kubera_webapp/Scripts/schema.sql (script unico)
```

## Frontend (repo separado: C:\Projetos\outros\kubera\kubera\)
- Angular 19 + PrimeNG 19 + Tailwind (Sakai template)
- Dev: `npm start` (proxy → localhost:5281)
- Environment: `src/enviroments/enviroment.ts` (typo intencional)
- Pages: `src/app/pages/admin/*.ts` (standalone, inline templates)
- Services: `src/app/pages/service/*.service.ts`
- Routes: `src/app.routes.ts` (root), `src/app/pages/pages.routes.ts` (admin)
- Menu: `src/app/layout/component/app.menu.ts`
- Telas: Dashboard, Corretoras, Faixas (EQUITY+BMF), Processamento, Entidades, Fatores Preco, Cobrancas Fixas, Relatorios, Seed Data (Developer)

## Database
- **financial_engine**: equity_fee_brackets, bmf_fee_configs, daily_billing, order_billing, bracket_fee_tracking, price_modification_factors, entity_billing_configs, fixed_monthly_fees
- **management**: brokers, process_billing
- **history**: orders, trades (externo)
- **Views**: vw_daily_billing_summary, vw_monthly_billing_summary, vw_monthly_billing_by_broker, vw_billable_orders
- CASCADE DELETE: daily_billing -> order_billing, bracket_fee_tracking

## Testes (44 total)
- 37 unitarios (strategy static methods, sem DB)
- 7 integracao (Testcontainers PostgreSQL, pipeline completo e2e)
- Docker necessario para testes de integracao
- Filtro: `--filter "FullyQualifiedName~Integration"` ou `!~Integration`

## Decisoes Tecnicas
- Sem interfaces nos repositories (projeto simples)
- Dapper (leve, controle SQL)
- ProcessBilling como job queue (sem RabbitMQ/Hangfire)
- Strategy classes com metodos estaticos puros, recebem billingKey/brokerId como params opcionais
- Fee proporcional por ordem para rastreabilidade
