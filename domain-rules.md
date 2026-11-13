# Kubera - Regras de Dominio (Referencia Detalhada)

> Este arquivo contem as regras de calculo detalhadas. Para visao geral, ver CLAUDE.local.md.

## Calculo EQUITY (Acoes, Opcoes, FIIs)

Cobranca progressiva por faixas de volume financeiro (notional).
- Notional = `qty * price` (ou `qty * (price / factor)` se PriceModificationFactor existir)
- Cada faixa: BPS (basis points) + CAP (teto maximo)
- 1 bps = 0.0001, entao 0.5 bps = 0.00005
- BPS armazenado como valor direto (0.5), convertido para taxa dividindo por 10000

### Faixas (exemplo)
| Faixa | Range Volume | BPS | Taxa | CAP |
|-------|-------------|-----|------|-----|
| 1 | 0 - 50M | 0.50 | 0.00005 | R$ 2.100 |
| 2 | 50M - 150M | 0.45 | 0.000045 | R$ 5.200 |
| 3 | 150M - 500M | 0.35 | 0.000035 | R$ 15.000 |
| 4 | 500M+ | 0.30 | 0.00003 | R$ 31.000 |

### Exemplo: R$200M no mes
```
Faixa 1: 50M * 0.00005 = R$ 2.500 -> CAP R$ 2.100 -> Cobra R$ 2.100
Faixa 2: 100M * 0.000045 = R$ 4.500 -> CAP R$ 5.200 -> Cobra R$ 4.500
Faixa 3: 50M * 0.000035 = R$ 1.750 -> CAP R$ 15.000 -> Cobra R$ 1.750
Total: R$ 8.350
```

### Exemplo: Processamento Diario com Acumulado
Trader acumulou R$40M (fee faixa 1 = R$2.000). Hoje opera +R$30M (total 70M):
```
Faixa 1 restante: 10M * 0.00005 = 500, CAP remaining = 2100-2000 = 100 -> Cobra 100
Faixa 2: 20M * 0.000045 = 900, CAP = 5200, nada cobrado -> Cobra 900
Total do dia: R$ 1.000
```

### Algoritmo
```
Para cada (BillingKey, Broker, Mes):
1. Buscar faixas aplicaveis (Trader especifico OU Trader IS NULL como fallback)
2. Buscar volume acumulado no mes (dias anteriores)
3. Buscar fee ja cobrado em cada faixa (para CAPs)
4. Buscar PriceModificationFactors validos
5. Faixas ordenadas por NotionalFrom ASC
6. Para cada faixa atravessada pelo volume do dia:
   - se posicaoAtual < NotionalFrom: o trecho ate NotionalFrom NAO e cobrado (lacuna)
   - notionalNaFaixa = min(remainingNotional, bracketEnd - posicaoAtual)
   - fee = notionalNaFaixa * (bps / 10000)
   - capRestante = CAP NULL ? sem limite : max(0, CAP - ja_cobrado_faixa)
   - feeACobrar = min(fee, capRestante)
7. Fee por ordem = proporcional ao notional (ver Distribuicao Proporcional)
```

### Detalhes de implementacao
- Faixas re-ordenadas por NotionalFrom na strategy (defensivo)
- NotionalTo nullable: NULL = faixa aberta (sem limite superior, tratado como decimal.MaxValue)
- **CAP nullable: NULL = sem teto**; CAP = 0 = faixa nao cobra nada (explicito)
- **Volume fora de qualquer faixa nao e cobrado**: abaixo da primeira faixa (se nao comeca em 0) e em lacunas entre faixas. A posicao avanca normalmente pelo trecho nao cobrado
- **Fee ja cobrado por faixa e indexado por NotionalFrom** (nao pelo Id): se a faixa muda de versao no meio do mes (novo ValidFrom) ou o trader passa de padrao para especial, o CAP ja consumido continua valendo para a faixa que comeca no mesmo volume
- Se nao ha faixas ou notional <= 0: retorna fee zero (sem faixas: log WARNING, igual a BMF sem config)
- Se PriceModificationFactor = 0: ignorado (previne divisao por zero)
- CAP remaining nunca negativo (floor em 0)
- **Arredondamento**: fee calculado de cada faixa arredondado para centavos (AwayFromZero) **antes** do CAP; `ChargedFee` e `TotalFee` sempre em centavos (= soma das ordens). `CalculatedFee` fica sem arredondar (auditoria)

### BracketFeeTracking (auditoria)
Cada faixa usada gera registro com:
- **CalculatedFee**: fee antes do CAP (valor "bruto")
- **ChargedFee**: fee apos aplicar CAP (valor efetivamente cobrado)
- **AccumulatedFeeBefore**: fee ja cobrado na faixa no mes
- **BracketCap**: valor do CAP da faixa (NULL = sem teto)
- Permite auditoria: onde o CAP foi atingido, quanto "perdido" por CAP

---

## Classificacao de Ativos (AssetClass)

A base nao traz AssetClass. `history.orders` so tem **Symbol** (PETR4, WINZ25, DI1F27); **Asset** (raiz do ativo: PETR, WIN, DI1)
existe **so em `history.trades`**. A `vw_billable_orders` traz o Asset do primeiro trade da ordem (LATERAL); ordem sem trade sai com Asset NULL.
`AssetClassifier.Classify(asset, symbol)` (regra fixa no codigo, nomenclatura B3) e chamado no `OrdersRepository`
logo apos a leitura da view (ponto unico):

| Raiz (Asset) | Classe | Exemplos |
|--------------|--------|----------|
| 4 caracteres alfanumericos (codigo de emissor) | EQUITY | PETR4, PETR4F, BOVA11, HGLG11, AAPL34, B3SA3, PETRK300 |
| 3 caracteres alfanumericos (codigo de mercadoria) | BMF | WINZ25, DOLZ26, DI1F27, BGIV25, CCMK26 |
| Outro | UNKNOWN | nao cobrado (sem strategy -> log WARNING com Symbol/Asset) |

- Asset vazio: deduz pelo Symbol (`RAIZ(3)+mes+ano(2)` -> BMF; 4 primeiros caracteres -> EQUITY).
  O Asset e preferido porque o Symbol sozinho e ambiguo (BDR `XXXU34` casa com o padrao de futuro)

### Sinteticos BMF (ex.: BTGDOLZ26 = DOL cheio + WDO mini)
- Sintetico = Symbol da ordem que **nao comeca com o Asset dos trades** (BTGDOLZ26, trades com Asset DOL). Regra generica, sem depender do prefixo BTG
- So a ordem do sintetico chega (nao ha ordens filhas). A perna de cada trade vem no sufixo do `UniqueTradeId` (`1234-WDO`, `321-DOL`)
- `LastQty` vem no padrao do cheio: a perna e convertida pelo fator (1 DOL = 5 WDO; LastQty 0,2 na perna WDO = 1 mini).
  Fatores na tabela inline da view `(VALUES ('WDO', 5), ('DOL', 1))`; perna sem fator = 1
- A `vw_billable_orders` devolve **uma linha por perna** (mesmo `Id`, `Asset` = perna, `CumQty` convertido, VWAP). O calculo nao sabe que existe sintetico
- `order_billing` fica com uma linha por perna (mesmo OrderId, coluna `Asset`). O detalhamento do PDF faz join com `history.orders` (nao com a view) para nao duplicar e mostra `BTGDOLZ26 (WDO)`
- Trade sem sufixo no UniqueTradeId -> perna `?` -> UNKNOWN (nao cobrado, warning). Sintetico sem trade no dia nao entra

---

## Calculo BMF (Futuros)

Cobranca por numero de contratos, com valor fixo por tipo. CAP mensal unico.

### Classificacao de Contratos
Tipo determinado pela raiz do contrato (`Asset`; sem Asset, os 3 primeiros caracteres do Symbol):

| Raiz | Tipo | Fee |
|------|------|-----|
| WIN, WDO | Mini | config.ValorMini por contrato |
| IND, DOL | Cheio | config.ValorCheio por contrato |
| DI1 | DI1 | varia por vencimento (lido do Symbol, ver tabela) |
| Qualquer outra (BGI, CCM, opcoes sobre futuro...) | Demais | config.ValorOutros por contrato |
| Vazia | Desconhecido | R$ 0,00 (log WARNING com os simbolos) |

### DI1 - Calculo de Vencimento

Formato do simbolo: `DI1` + codigo_mes(1 char) + ano(2 digitos). Ex: `DI1F25` = Jan/2025

**Codigos de mes (convencao B3):**
| Codigo | Mes | Codigo | Mes |
|--------|-----|--------|-----|
| F | Jan | N | Jul |
| G | Fev | Q | Ago |
| H | Mar | U | Set |
| J | Abr | V | Out |
| K | Mai | X | Nov |
| M | Jun | Z | Dez |

**Calculo do numero do vencimento:**
```
mesesAteVencimento = (anoVenc - anoTrade) * 12 + (mesVenc - mesTrade)
vencimento = max(1, mesesAteVencimento)
```
- Vencimento = meses corridos a partir do mes do trade (confirmado pelo negocio), nao a ordem dos vencimentos listados na B3. Ex.: trade em Nov/26 -> DI1X26 = 1 (vence no proprio mes: tambem 1), DI1Z26 = 1, DI1F27 = 2, DI1N27 = 8
- Ano 2 digitos convertido: `ano < 100 ? ano + 2000 : ano`
- Simbolo curto (< 5 chars) ou invalido: vencimento = 1 (tier mais barato)
- Symbol vazio para DI1: usa fee de venc 1-4

### DI1 por Vencimento
| Vencimento | Campo config |
|------------|-------------|
| 1-4 | DI1Venc1a4 |
| 5 | DI1Venc5 |
| 6 | DI1Venc6 |
| 7 | DI1Venc7 |
| 8+ | DI1Venc8Plus |

### CAP mensal
- `CapMensal` NULL = sem teto; 0 = nao cobra nada
- Fee ja cobrado no mes = soma de TotalFee BMF da BillingKey nos dias anteriores
- Fee do dia arredondado para centavos (AwayFromZero) antes do CAP

### Exemplo com CAP
```
500 Mini * 0.03 = R$ 15, 100 Cheio * 0.15 = R$ 15, DI1 = R$ 4
Total = R$ 34. CAP = R$ 25, ja cobrado = R$ 10, disponivel = R$ 15
Cobra: R$ 15 (limitado pelo CAP)
```

### Fallbacks BMF
- Config BMF nao encontrada: log WARNING + retorna fee zero (soft failure, nao lanca excecao). AccumulatedBefore/After = fee ja cobrado no mes
- Raiz vazia: fee = 0 por contrato, simbolos retornados em `UnknownSymbols` e logados como WARNING
- Fee por ordem proporcional a quantidade (ver Distribuicao Proporcional)

---

## Ordens GTC/GTD (vw_billable_orders)

- **EQUITY GTC/GTD**: a ordem pode durar varios dias e o `CumQty` dela e cumulativo. Por isso o calculo usa os trades do dia (`history.trades`): qtd = soma de `LastQty` do dia e preco = VWAP do dia. Se a ordem nao teve trade no dia, ela nao entra
- **Demais ordens (DAY etc.)**: usam `CumQty`/`AvgPx` direto da ordem
- **BMF**: nao existem ordens GTC/GTD (confirmado pelo negocio). Como a view nao conhece a AssetClass, o ramo GTC/GTD agrega qualquer ordem GTC/GTD que nao seja sintetico

---

## BillingKey (identificacao do cliente cobrado)

A chave de cobranca e o par **(KeyType, Key)**, gravado em `daily_billing`/`order_billing` nas colunas `KeyType` + `Trader`:
- `TRADER`: Key = `EnteringTrader` da ordem
- `ENTITY`: Key = Entity (grafia da `EntityBillingConfig`)

Entity e trader com o **mesmo nome sao chaves diferentes**: volume acumulado, CAPs por faixa, CAP mensal BMF e piso nao se misturam.
Key comparada case-insensitive; KeyType exato.

Resolucao por ordem, **por AssetClass** (`BillingProcessingService.ResolveBillingKey`):
1. Trader tem regra especial **TRADER** valida no dia para a AssetClass da ordem (EQUITY: faixa especial; BMF: config especial) -> `(TRADER, EnteringTrader)`. **A regra especial do trader prevalece sobre a entity**: ele sai da entity (volume dele nao conta nas faixas nem no piso da entity). Log INFO quando isso acontece
2. Senao, Entity da ordem tem `EntityBillingConfig` valida -> `(ENTITY, Entity)`
3. Senao -> `(TRADER, EnteringTrader)`

Exemplo: trader JOAO com faixa EQUITY especial, sem config BMF especial, Entity MESA_A configurada -> EQUITY cobrado como (TRADER, JOAO); BMF cobrado dentro de (ENTITY, MESA_A).

---

## Contratos Especiais (Trader/Entity + Broker)

Regras especiais na **mesma tabela**, campos `Trader` + `KeyType` nullable:
| Broker | Trader | KeyType | Tipo |
|--------|--------|---------|------|
| 123 | NULL | NULL | Regra **padrao** |
| 123 | "JOAO" | TRADER | Regra **especial** do trader JOAO (100% custom, ignora padrao) |
| 123 | "MESA_A" | ENTITY | Regra **especial** da entity MESA_A |

- CHECK no banco: `Trader` NULL <=> `KeyType` NULL
- A regra casa com a BillingKey por (KeyType, Trader): regra ENTITY "JOAO" nao vale para o trader JOAO e vice-versa
- API: `KeyType` obrigatorio quando `Trader` e informado (`BillingKeyTypes.NormalizeRuleKey`)

Busca SEM fallback: se achou especial, usa so ela. Se nao, usa padrao.
Regra especial e completamente independente (nao herda da padrao).

**Copy-to-Trader**: copia regra padrao como base para criar regra especial (`KeyType` no body, padrao TRADER).
- EQUITY: requer que existam brackets padrao (Trader IS NULL) para o broker
- BMF: requer que exista config padrao (Trader IS NULL) para o broker

---

## PriceModificationFactor

Para ativos cujo preco cotado representa um lote (ex: AZUL54 = preco de 10.000 acoes):
- `notional = qty * (price / factor)` em vez de `qty * price`
- Factor deve ser > 0 (validacao no controller, previne divisao por zero)
- Se factor cadastrado = 0 no calculo: ignorado (fallback para notional padrao)
- Lookup por Symbol case-insensitive. Dois fatores vigentes no dia para o mesmo Symbol: vale o de ValidFrom mais recente (`BillingProcessingService.BuildPriceFactorMap`)
- Cadastro por simbolo com ValidFrom/ValidTo
- Soft delete via IsActive (DELETE desativa, nao remove)
- Aplicado automaticamente no calculo EQUITY (nao afeta BMF)

---

## EntityBillingConfig (Billing por Entity)

Agrupa ordens por Entity ao inves de Trader:
- Config: Broker + Entity + ValidFrom/ValidTo
- Resolucao: se Entity da ordem esta nas configs -> BillingKey = (ENTITY, Entity **com a grafia da config**), senao -> (TRADER, EnteringTrader). Excecao: trader com regra especial propria sai da entity (ver BillingKey)
- Case insensitive (OrdinalIgnoreCase) em toda a cadeia: resolucao, agrupamento (BillingKey e AssetClass), acumulados do mes e piso
- FloorFee (opcional): piso mensal por entity. Se o fee do mes da entity < piso, cobra a diferenca como ajuste. Calculado por `FloorFeeCalculator` (fonte unica para PDF e relatorio mensal), somando **so chaves ENTITY**. Duas configs da mesma entity no mes: vale a de ValidFrom mais recente
- Entity comecando no meio do mes: cenario nao existe no negocio (acumulados nao sao transferidos do trader para a entity)
- Se nao ha configs para o broker: todas ordens usam EnteringTrader (nem verifica Entity)
- Soft delete via IsActive

Exemplo: Config MESA_A para Broker 1
- JOAO(MESA_A) -> BillingKey = (ENTITY, "MESA_A")
- MARIA(MESA_A) -> BillingKey = (ENTITY, "MESA_A") (agrupada)
- PEDRO(MESA_B) -> BillingKey = (TRADER, "PEDRO") (padrao)
- PDF mensal mostra chaves ENTITY como "MESA_A (Entidade)"

---

## Distribuicao Proporcional de Fee por Ordem

Apos calcular o fee total do dia, distribui proporcionalmente entre as ordens:
- **EQUITY**: proporcional ao notional ajustado (`orderNotional / totalNotional * totalFee`)
- **BMF**: proporcional a quantidade (`orderQty / totalQty * totalFee`)
- Arredondamento: metodo do maior resto em centavos (`FeeDistribution.Distribute`)
- Garantia: soma dos fees por ordem = `Math.Round(TotalFee, 2)` (AwayFromZero). Os centavos de sobra vao para as ordens com maior parte fracionaria

---

## FixedMonthlyFee (Cobranca Fixa Mensal)

Valor fixo cobrado mensalmente por broker, independente de volume operado.
- Config: Broker + Description + Amount + ValidFrom/ValidTo
- Validade por mes: fee incluso se `ValidFrom <= ultimo dia do mes` E `ValidTo >= primeiro dia do mes` (ValidTo inclusivo)
- Aparece no PDF mensal como secao separada ("COBRANCAS FIXAS MENSAIS") entre resumo e detalhamento
- TOTAL GERAL do PDF = fee variavel (EQUITY+BMF) + fee fixo + ajuste de piso (FloorFee) + Market Data
- Soft delete via IsActive
- Nao afeta o BillingProcessingService nem as Strategies (independente do calculo variavel)

### Validacoes
- `Broker > 0`
- `Description` obrigatoria e nao vazia
- `Amount > 0`
- `ValidFrom < ValidTo`

---

## Market Data (cobranca por conta vinculada)

Valor fixo mensal por usuario (`EnteringTrader`) com conta vinculada ao broker.
- **Origem dos vinculos**: API externa que informa so o estado atual (`UserExchangeAccountApiClient`, contrato assumido `GET {BaseUrl}/accounts -> [{ broker, user }]`, a confirmar)
- **Snapshot diario** (`UserExchangeAccountSyncWorker`): 1x/dia a partir de `UserExchangeAccountsApi:SyncHourLocal`, grava `user_exchange_account_snapshots` (dado bruto, sem exclusoes). Se `BaseUrl` vazio, o worker fica desligado. Se a API falhar, tenta de novo a cada 30 min. Sync manual: `POST /market-data/sync`
- **Quem e cobrado**: usuario visto em qualquer snapshot do mes (mes cheio, sem pro-rata), uma vez por broker (case-insensitive)
- **Exclusoes** (`market_data_exclusions`): usuarios nao cobrados (administrativos, de API...). `Broker` NULL = todos os brokers. Comparacao case-insensitive
- **Preco** (`market_data_fee_configs`): `AmountPerUser` por broker com vigencia. Vale a config que toca o mes; se houver mais de uma, a de ValidFrom mais recente. Broker sem preco: nao cobrado (WARNING + aviso na tela)
- **Apuracao** (`market_data_billing`, um registro por usuario cobrado): recalculada **automaticamente ao final do processamento de cada broker** (mesma transacao e lock), pelo sync diario (mes corrente; no dia 1 tambem o mes anterior) ou por `POST /market-data/recalculate`
- **Exclusao nova ou preco novo** so afeta meses ja apurados apos recalcular o mes
- **Limitacao**: se o sync falhar num dia, um usuario vinculado so naquele dia nao e capturado
- Entra no PDF mensal (secao "MARKET DATA", ultima cobranca antes do detalhamento por ordem) e na receita dos relatorios/dashboard (`MarketDataFee`)

### Validacoes
- Preco: `Broker > 0`, `AmountPerUser > 0`, `ValidFrom < ValidTo`
- Exclusao: `Trader` obrigatorio, `Broker` vazio (todos) ou `> 0`, sem duplicar exclusao ativa do mesmo (Broker, Trader)

---

## Soft Delete (IsActive)

Padrao aplicado em todas as tabelas de configuracao:
- Criacao: `IsActive = true` sempre
- DELETE nos endpoints: `SET IsActive = FALSE` (nao remove registro)
- Queries de calculo filtram `WHERE IsActive = TRUE`
- Permite rastreabilidade historica

---

## Fluxo do BillingWorker

```
ExecuteAsync (loop 1s, retry 5s em erro)
-> ProcessPendingBillingsAsync: processa TODOS pendentes ate esvaziar fila
   -> TryAcquireNextAsync [FOR UPDATE SKIP LOCKED]
   -> Para cada broker (Parallel.ForEachAsync, max 4):
      1. WARNING se ha dias anteriores do mes com ordens mas sem billing (processamento fora de ordem)
      2. Abrir transacao + pg_advisory_xact_lock(7001, broker) (serializa o mesmo broker)
      3. Datas = D + dias JA processados do broker no mes posteriores a D (cascata)
      4. Para cada data, em ordem:
         a. Ordens, entity configs, faixas, configs BMF e fatores em paralelo (conexoes proprias)
         b. Acumulados do mes (notional/fee por BillingKey, fee por faixa) lidos DENTRO da transacao
            (enxergam os dias reescritos antes na cascata)
         c. Deletar billing da data SEMPRE (CASCADE -> order_billing + bracket_fee_tracking),
            mesmo sem ordens (ordens removidas na origem)
         d. Agrupar por (BillingKey, AssetClass), case-insensitive -> Strategy.Calculate()
         e. Persistir via binary COPY (Ids de daily_billing reservados da sequence)
      5. Recalcular a Market Data do broker no mes (snapshots + exclusoes + preco)
      6. Commit (ou rollback se qualquer erro - atomico por broker, incluindo a cascata)
```

**Reprocessamento em cascata**: acumulados e CAPs do dia D dependem dos dias anteriores do mes. Reprocessar D recalcula D e todos os dias posteriores do mes ja processados, na mesma transacao.

**ProcessBroker comma-separated**: `"1,2,3"` -> [1, 2, 3]. IDs invalidos silenciosamente ignorados.

---

## Validacoes nos Endpoints

### Regras comuns
- `ValidFrom` deve ser estritamente anterior a `ValidTo` (nao permite igual)
- **`ValidTo` e inclusivo em todo o sistema**: config vale no dia `ValidTo` (diario: `@TradeDate <= ValidTo`; mensal: `ValidTo >= primeiro dia do mes`). Frontend rotula "Valido Ate (inclusive)"
- `Broker` deve ser > 0
- Soft delete em todos os DELETEs

### Equity Brackets
- `NotionalFrom >= 0` (primeira faixa pode comecar em zero)
- `NotionalTo > NotionalFrom` quando presente (nullable = faixa aberta)
- `Bps >= 0` (permite zero para pass-through)
- `Cap` NULL (sem teto) ou `>= 0`

### BMF Config
- Todos os valores de fee `>= 0` (ValorMini, ValorCheio, DI1*)
- `CapMensal` NULL (sem teto) ou `>= 0`

### Price Factors
- `Symbol` obrigatorio e nao vazio
- `Factor > 0` (estritamente positivo, previne divisao por zero)

### Entity Config
- `Entity` obrigatorio e nao vazio

---

## API Endpoints

### Relatorios
| Endpoint | Descricao |
|----------|-----------|
| `GET /report/billing/daily/{yyyy-MM-dd}` | Billing do dia (so fee variavel: fixa e piso sao mensais) |
| `GET /report/billing/month/{yyyy-MM}` | Billing do mes |
| `GET /report/billing/month/{yyyy-MM}/broker/{id}/pdf` | PDF mensal por broker (inclui fixed fees e piso) |
| `GET /report/billing/anual/{yyyy}` | Billing do ano (2000-2100) |
| `GET /report/dashboard` | Dashboard (YTD, MTD, traders ativos, receita mensal, volume corretoras) |

**Receita nos relatorios mensal/anual e no dashboard** = mesma composicao do PDF mensal, calculada por `MonthlyRevenueCalculator` (via `RevenueReportService`):
- `TotalFee` = `VariableFee` (EQUITY+BMF) + `FixedFee` (cobrancas fixas validas no mes) + `FloorAdjustment` (piso por entity) + `MarketDataFee` (apuracao de Market Data)
- `TotalFee` por classe de ativo (`AssetClasses[]`) continua sendo so o variavel
- Broker entra no mes se teve billing variavel OU cobranca fixa valida OU Market Data no mes (igual ao PDF)
- Meses futuros nao entram (cobranca fixa so a partir do mes corrente)

### Processamento
| Endpoint | Descricao |
|----------|-----------|
| `GET /financial/status` | Processamentos pendentes |
| `GET /financial/history` | 50 mais recentes |
| `POST /financial/start-process` | Agenda processamento (body: date, brokers) |

### CRUD
| Recurso | Base URL | Operacoes |
|---------|----------|-----------|
| Price Factors | `/price-modification-factor` | GET, GET/{id}, POST, PUT/{id}, DELETE/{id} |
| Entity Config | `/entity-billing-config` | GET, GET/{id}, POST, PUT/{id}, DELETE/{id} |
| Equity Brackets | `/equity-bracket` | GET/broker/{id}, POST, PUT/{id}, DELETE/{id}, POST/copy |
| BMF Configs | `/bmf-config` | GET/broker/{id}, POST, PUT/{id}, DELETE/{id}, POST/copy |
| Fixed Monthly Fees | `/fixed-monthly-fee` | GET, GET/{id}, POST, PUT/{id}, DELETE/{id} |
| Market Data Precos | `/market-data-config` | GET, GET/{id}, POST, PUT/{id}, DELETE/{id} |
| Market Data Exclusoes | `/market-data-exclusion` | GET, POST, PUT/{id}, DELETE/{id} |
| Market Data Operacao | `/market-data` | POST /sync, POST /recalculate, GET /month/{yyyy-MM} |
| Brokers | `/broker` | GET, GET/{id}, POST, PUT/{id} |

### Developer / Test
| Endpoint | Descricao |
|----------|-----------|
| `POST /seed/broker-935` | Gera dados ficticios (orders+trades) para broker 935 num mes (body: { month: "yyyy-MM" }). Idempotente: limpa e recria. Cria broker + fee configs se nao existirem. |
