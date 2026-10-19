-- ============================================
-- Migration 003
-- - Cobranca de Market Data por usuario com conta vinculada ao broker
--   (snapshots diarios da API externa, precos, exclusoes, apuracao mensal)
-- Idempotente: pode ser executada mais de uma vez.
-- ============================================

BEGIN;

-- Market Data: cobranca mensal por usuario (EnteringTrader) com conta vinculada ao broker.
-- Vinculos vem de API externa (so estado atual) -> snapshot diario.
CREATE TABLE IF NOT EXISTS financial_engine.user_exchange_account_snapshots (
    SnapshotDate DATE NOT NULL,
    Broker INT NOT NULL,
    Trader VARCHAR(100) NOT NULL,
    CONSTRAINT user_exchange_account_snapshots_pk PRIMARY KEY (SnapshotDate, Broker, Trader)
);

CREATE INDEX IF NOT EXISTS idx_user_exchange_account_snapshots_broker_date
    ON financial_engine.user_exchange_account_snapshots(Broker, SnapshotDate);

CREATE TABLE IF NOT EXISTS financial_engine.market_data_fee_configs (
    Id BIGSERIAL PRIMARY KEY,
    Broker INT NOT NULL,
    AmountPerUser DECIMAL(18,2) NOT NULL,
    ValidFrom DATE NOT NULL,
    ValidTo DATE NOT NULL,
    IsActive BOOLEAN NOT NULL DEFAULT TRUE,
    CreatedAt TIMESTAMP NOT NULL DEFAULT NOW(),
    UpdatedAt TIMESTAMP NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_market_data_fee_configs_validity
    ON financial_engine.market_data_fee_configs(Broker, ValidFrom, ValidTo);

-- Usuarios nao cobrados (administrativos, de API...). Broker NULL = todos os brokers.
CREATE TABLE IF NOT EXISTS financial_engine.market_data_exclusions (
    Id BIGSERIAL PRIMARY KEY,
    Broker INT NULL,
    Trader VARCHAR(100) NOT NULL,
    Reason VARCHAR(200) NULL,
    IsActive BOOLEAN NOT NULL DEFAULT TRUE,
    CreatedAt TIMESTAMP NOT NULL DEFAULT NOW(),
    UpdatedAt TIMESTAMP NOT NULL DEFAULT NOW()
);

CREATE UNIQUE INDEX IF NOT EXISTS idx_market_data_exclusions_unique
    ON financial_engine.market_data_exclusions(COALESCE(Broker, 0), UPPER(Trader)) WHERE IsActive = TRUE;

-- Apuracao mensal congelada (um registro por usuario cobrado)
CREATE TABLE IF NOT EXISTS financial_engine.market_data_billing (
    Id BIGSERIAL PRIMARY KEY,
    Broker INT NOT NULL,
    Year INT NOT NULL,
    Month INT NOT NULL,
    Trader VARCHAR(100) NOT NULL,
    Amount DECIMAL(18,2) NOT NULL,
    ConfigId BIGINT NOT NULL REFERENCES financial_engine.market_data_fee_configs(Id),
    CalculatedAt TIMESTAMP NOT NULL DEFAULT NOW(),
    CONSTRAINT market_data_billing_unique UNIQUE (Broker, Year, Month, Trader)
);

COMMIT;
