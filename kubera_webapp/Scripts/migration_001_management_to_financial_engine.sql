-- ============================================
-- Migration 001
-- - Move brokers e process_billing de management -> financial_engine
-- - Amplia precisao de equity_fee_brackets.Bps (10,4 -> 18,8)
-- - Adiciona entity_billing_configs.FloorFee
-- Idempotente: pode ser executada mais de uma vez.
-- ============================================

BEGIN;

DO $$
BEGIN
    IF to_regclass('management.brokers') IS NOT NULL
       AND to_regclass('financial_engine.brokers') IS NULL THEN
        -- SET SCHEMA leva junto sequence (serial), constraints e indices
        ALTER TABLE management.brokers SET SCHEMA financial_engine;
    END IF;

    IF to_regclass('management.process_billing') IS NOT NULL
       AND to_regclass('financial_engine.process_billing') IS NULL THEN
        ALTER TABLE management.process_billing SET SCHEMA financial_engine;
    END IF;
END $$;

ALTER TABLE financial_engine.equity_fee_brackets
    ALTER COLUMN Bps TYPE DECIMAL(18,8);

ALTER TABLE financial_engine.entity_billing_configs
    ADD COLUMN IF NOT EXISTS FloorFee DECIMAL(18,2) NULL;

-- Remove o schema antigo apenas se ficou vazio (sem CASCADE de proposito)
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM information_schema.schemata WHERE schema_name = 'management')
       AND NOT EXISTS (SELECT 1 FROM pg_class c
                       JOIN pg_namespace n ON n.oid = c.relnamespace
                       WHERE n.nspname = 'management') THEN
        DROP SCHEMA management;
    END IF;
END $$;

COMMIT;
