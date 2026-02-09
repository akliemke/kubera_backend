-- ============================================
-- Kubera Billing - Database Schema
-- ============================================

-- Create schema if not exists
CREATE SCHEMA IF NOT EXISTS financial_engine AUTHORIZATION "admin";

-- ============================================
-- EQUITY Fee Brackets
-- ============================================
CREATE TABLE financial_engine.equity_fee_brackets (
    Id BIGSERIAL PRIMARY KEY,
    Broker INT NOT NULL,
    Trader VARCHAR(100) NULL,  -- NULL = regra padrão, preenchido = regra especial
    ValidFrom DATE NOT NULL,
    ValidTo DATE NOT NULL,
    NotionalFrom DECIMAL(18,2) NOT NULL,
    NotionalTo DECIMAL(18,2) NULL,  -- NULL = sem limite superior
    Bps DECIMAL(10,4) NOT NULL,  -- Basis points (ex: 0.5)
    Cap DECIMAL(18,2) NOT NULL,  -- CAP máximo da faixa
    IsActive BOOLEAN NOT NULL DEFAULT TRUE,
    CreatedAt TIMESTAMP NOT NULL DEFAULT NOW(),
    UpdatedAt TIMESTAMP NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_equity_fee_brackets_broker ON financial_engine.equity_fee_brackets(Broker);
CREATE INDEX idx_equity_fee_brackets_trader ON financial_engine.equity_fee_brackets(Broker, Trader);
CREATE INDEX idx_equity_fee_brackets_validity ON financial_engine.equity_fee_brackets(Broker, ValidFrom, ValidTo);

COMMENT ON TABLE financial_engine.equity_fee_brackets IS 'Faixas de cobrança EQUITY - progressiva por volume com BPS + CAP por faixa';
COMMENT ON COLUMN financial_engine.equity_fee_brackets.Trader IS 'NULL = regra padrão do broker. Preenchido = regra especial 100% custom';
COMMENT ON COLUMN financial_engine.equity_fee_brackets.Bps IS 'Basis points. Ex: 0.5 bps = 0.00005 taxa';

-- ============================================
-- BMF Fee Config
-- ============================================
CREATE TABLE financial_engine.bmf_fee_configs (
    Id BIGSERIAL PRIMARY KEY,
    Broker INT NOT NULL,
    Trader VARCHAR(100) NULL,  -- NULL = regra padrão, preenchido = regra especial
    ValidFrom DATE NOT NULL,
    ValidTo DATE NOT NULL,
    ValorMini DECIMAL(10,4) NOT NULL,  -- Valor por contrato Mini (WIN, WDO)
    ValorCheio DECIMAL(10,4) NOT NULL,  -- Valor por contrato Cheio (IND, DOL)
    DI1Venc1a4 DECIMAL(10,4) NOT NULL,  -- DI1 1º ao 4º vencimento
    DI1Venc5 DECIMAL(10,4) NOT NULL,  -- DI1 5º vencimento
    DI1Venc6 DECIMAL(10,4) NOT NULL,  -- DI1 6º vencimento
    DI1Venc7 DECIMAL(10,4) NOT NULL,  -- DI1 7º vencimento
    DI1Venc8Plus DECIMAL(10,4) NOT NULL,  -- DI1 8º vencimento em diante
    CapMensal DECIMAL(18,2) NOT NULL,  -- CAP mensal único (engloba Mini + Cheio + DI1)
    IsActive BOOLEAN NOT NULL DEFAULT TRUE,
    CreatedAt TIMESTAMP NOT NULL DEFAULT NOW(),
    UpdatedAt TIMESTAMP NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_bmf_fee_configs_broker ON financial_engine.bmf_fee_configs(Broker);
CREATE INDEX idx_bmf_fee_configs_trader ON financial_engine.bmf_fee_configs(Broker, Trader);
CREATE INDEX idx_bmf_fee_configs_validity ON financial_engine.bmf_fee_configs(Broker, ValidFrom, ValidTo);

COMMENT ON TABLE financial_engine.bmf_fee_configs IS 'Configuração de cobrança BMF - valor por contrato com CAP mensal único';
COMMENT ON COLUMN financial_engine.bmf_fee_configs.Trader IS 'NULL = regra padrão do broker. Preenchido = regra especial 100% custom';

-- ============================================
-- Daily Billing (resultado agregado)
-- ============================================
CREATE TABLE financial_engine.daily_billing (
    Id BIGSERIAL PRIMARY KEY,
    Broker INT NOT NULL,
    Trader VARCHAR(100) NOT NULL,
    TradeDate DATE NOT NULL,
    AssetClass VARCHAR(20) NOT NULL,  -- 'EQUITY' ou 'BMF'
    TotalNotional DECIMAL(18,2) NOT NULL DEFAULT 0,
    TotalQty DECIMAL(18,4) NOT NULL DEFAULT 0,
    TotalFee DECIMAL(18,4) NOT NULL,
    AccumulatedBefore DECIMAL(18,2) NOT NULL,  -- Acumulado do mês antes deste dia
    AccumulatedAfter DECIMAL(18,2) NOT NULL,  -- Acumulado do mês após este dia
    EquityConfigId BIGINT NULL REFERENCES financial_engine.equity_fee_brackets(Id),
    BmfConfigId BIGINT NULL REFERENCES financial_engine.bmf_fee_configs(Id),
    ProcessBillingId INT NOT NULL,
    CalculatedAt TIMESTAMP NOT NULL DEFAULT NOW()
);

CREATE UNIQUE INDEX idx_daily_billing_unique ON financial_engine.daily_billing(Broker, Trader, TradeDate, AssetClass);
CREATE INDEX idx_daily_billing_date ON financial_engine.daily_billing(TradeDate);
CREATE INDEX idx_daily_billing_month ON financial_engine.daily_billing(Broker, Trader, AssetClass, TradeDate);

COMMENT ON TABLE financial_engine.daily_billing IS 'Resultado agregado do cálculo de billing por (Trader, Broker, Dia, AssetClass)';

-- ============================================
-- Order Billing (fee por ordem)
-- ============================================
CREATE TABLE financial_engine.order_billing (
    Id BIGSERIAL PRIMARY KEY,
    OrderId BIGINT NOT NULL,
    DailyBillingId BIGINT NOT NULL REFERENCES financial_engine.daily_billing(Id) ON DELETE CASCADE,
    Broker INT NOT NULL,
    Trader VARCHAR(100) NOT NULL,
    TradeDate DATE NOT NULL,
    AssetClass VARCHAR(20) NOT NULL,
    Notional DECIMAL(18,2) NOT NULL DEFAULT 0,
    Qty DECIMAL(18,4) NOT NULL DEFAULT 0,
    FeeAmount DECIMAL(18,4) NOT NULL
);

CREATE INDEX idx_order_billing_daily ON financial_engine.order_billing(DailyBillingId);
CREATE INDEX idx_order_billing_order ON financial_engine.order_billing(OrderId);
CREATE INDEX idx_order_billing_date ON financial_engine.order_billing(Broker, TradeDate);

COMMENT ON TABLE financial_engine.order_billing IS 'Fee calculado por ordem individual (proporcional ao volume/quantidade)';

-- ============================================
-- Bracket Fee Tracking (fee por faixa EQUITY)
-- ============================================
CREATE TABLE financial_engine.bracket_fee_tracking (
    Id BIGSERIAL PRIMARY KEY,
    DailyBillingId BIGINT NOT NULL REFERENCES financial_engine.daily_billing(Id) ON DELETE CASCADE,
    BracketId BIGINT NOT NULL REFERENCES financial_engine.equity_fee_brackets(Id),
    NotionalInBracket DECIMAL(18,2) NOT NULL,  -- Volume que caiu nesta faixa
    CalculatedFee DECIMAL(18,4) NOT NULL,  -- Fee calculado antes do CAP
    ChargedFee DECIMAL(18,4) NOT NULL,  -- Fee efetivamente cobrado (após CAP)
    AccumulatedFeeBefore DECIMAL(18,4) NOT NULL,  -- Fee acumulado na faixa antes deste dia
    BracketCap DECIMAL(18,2) NOT NULL  -- CAP da faixa no momento do cálculo
);

CREATE INDEX idx_bracket_fee_tracking_daily ON financial_engine.bracket_fee_tracking(DailyBillingId);
CREATE INDEX idx_bracket_fee_tracking_bracket ON financial_engine.bracket_fee_tracking(BracketId);

COMMENT ON TABLE financial_engine.bracket_fee_tracking IS 'Tracking do fee cobrado em cada faixa EQUITY por dia - permite aplicar CAPs por faixa corretamente';

-- ============================================
-- Price Modification Factors
-- ============================================
CREATE TABLE financial_engine.price_modification_factors (
    Id BIGSERIAL PRIMARY KEY,
    Symbol VARCHAR(50) NOT NULL,
    Factor DECIMAL(18,4) NOT NULL,  -- Fator divisor do preço (ex: 10000)
    ValidFrom DATE NOT NULL,
    ValidTo DATE NOT NULL,
    IsActive BOOLEAN NOT NULL DEFAULT TRUE,
    CreatedAt TIMESTAMP NOT NULL DEFAULT NOW(),
    UpdatedAt TIMESTAMP NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_price_modification_factors_symbol ON financial_engine.price_modification_factors(Symbol);
CREATE INDEX idx_price_modification_factors_validity ON financial_engine.price_modification_factors(Symbol, ValidFrom, ValidTo);

COMMENT ON TABLE financial_engine.price_modification_factors IS 'Fatores de modificação de preço por ativo - ajusta notional para ativos cujo preço representa lote';
COMMENT ON COLUMN financial_engine.price_modification_factors.Factor IS 'Fator divisor. Notional real = qty × (price / factor). Ex: 10000 para AZUL54';

-- ============================================
-- Entity Billing Configs
-- ============================================
CREATE TABLE financial_engine.entity_billing_configs (
    Id BIGSERIAL PRIMARY KEY,
    Broker INT NOT NULL,
    Entity VARCHAR(100) NOT NULL,
    ValidFrom DATE NOT NULL,
    ValidTo DATE NOT NULL,
    IsActive BOOLEAN NOT NULL DEFAULT TRUE,
    CreatedAt TIMESTAMP NOT NULL DEFAULT NOW(),
    UpdatedAt TIMESTAMP NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_entity_billing_configs_broker ON financial_engine.entity_billing_configs(Broker);
CREATE INDEX idx_entity_billing_configs_broker_date ON financial_engine.entity_billing_configs(Broker, ValidFrom, ValidTo);
CREATE UNIQUE INDEX idx_entity_billing_configs_unique ON financial_engine.entity_billing_configs(Broker, Entity, ValidFrom, ValidTo) WHERE IsActive = TRUE;

COMMENT ON TABLE financial_engine.entity_billing_configs IS 'Configuração para cobrar por Entity (conta) ao invés de Trader para entities específicas';
COMMENT ON COLUMN financial_engine.entity_billing_configs.Entity IS 'Nome da entity/conta. Quando configurada, ordens dessa entity são agrupadas por entity ao invés de por trader';

-- ============================================
-- Views para agregações
-- ============================================

-- View: Billing diário por segmento
CREATE OR REPLACE VIEW financial_engine.vw_daily_billing_summary AS
SELECT
    Broker,
    Trader,
    TradeDate,
    AssetClass,
    SUM(TotalNotional) as TotalNotional,
    SUM(TotalQty) as TotalQty,
    SUM(TotalFee) as TotalFee
FROM financial_engine.daily_billing
GROUP BY Broker, Trader, TradeDate, AssetClass;

-- View: Billing mensal por segmento
CREATE OR REPLACE VIEW financial_engine.vw_monthly_billing_summary AS
SELECT
    Broker,
    Trader,
    DATE_TRUNC('month', TradeDate) as BillingMonth,
    AssetClass,
    SUM(TotalNotional) as TotalNotional,
    SUM(TotalQty) as TotalQty,
    SUM(TotalFee) as TotalFee,
    COUNT(DISTINCT TradeDate) as TradingDays
FROM financial_engine.daily_billing
GROUP BY Broker, Trader, DATE_TRUNC('month', TradeDate), AssetClass;

-- View: Billing mensal consolidado por broker
CREATE OR REPLACE VIEW financial_engine.vw_monthly_billing_by_broker AS
SELECT
    Broker,
    DATE_TRUNC('month', TradeDate) as BillingMonth,
    AssetClass,
    COUNT(DISTINCT Trader) as TraderCount,
    SUM(TotalNotional) as TotalNotional,
    SUM(TotalQty) as TotalQty,
    SUM(TotalFee) as TotalFee
FROM financial_engine.daily_billing
GROUP BY Broker, DATE_TRUNC('month', TradeDate), AssetClass;

-- ============================================
-- Dados de exemplo (opcional)
-- ============================================

-- Exemplo: Faixas EQUITY para broker 1 (regra padrão)
/*
INSERT INTO financial_engine.equity_fee_brackets
    (Broker, Trader, ValidFrom, ValidTo, NotionalFrom, NotionalTo, Bps, Cap)
VALUES
    (1, NULL, '2024-01-01', '2099-12-31', 0, 50000000, 0.5, 2100),
    (1, NULL, '2024-01-01', '2099-12-31', 50000000, 150000000, 0.45, 5200),
    (1, NULL, '2024-01-01', '2099-12-31', 150000000, 500000000, 0.35, 15000),
    (1, NULL, '2024-01-01', '2099-12-31', 500000000, NULL, 0.30, 31000);

-- Exemplo: Config BMF para broker 1 (regra padrão)
INSERT INTO financial_engine.bmf_fee_configs
    (Broker, Trader, ValidFrom, ValidTo, ValorMini, ValorCheio,
     DI1Venc1a4, DI1Venc5, DI1Venc6, DI1Venc7, DI1Venc8Plus, CapMensal)
VALUES
    (1, NULL, '2024-01-01', '2099-12-31', 0.03, 0.15,
     0.01, 0.02, 0.04, 0.05, 0.10, 25000);
*/
