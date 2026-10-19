-- ============================================
-- Migration 002
-- - CAPs passam a aceitar NULL = sem teto
--   (equity_fee_brackets.Cap, bmf_fee_configs.CapMensal, bracket_fee_tracking.BracketCap)
-- - CAP = 0 continua significando cobranca zero. Valores existentes NAO sao alterados:
--   revisar cadastros com CAP = 0 que deveriam ser "sem teto" e atualizar para NULL.
-- Idempotente: pode ser executada mais de uma vez.
-- ============================================

BEGIN;

ALTER TABLE financial_engine.equity_fee_brackets
    ALTER COLUMN Cap DROP NOT NULL;

ALTER TABLE financial_engine.bmf_fee_configs
    ALTER COLUMN CapMensal DROP NOT NULL;

ALTER TABLE financial_engine.bracket_fee_tracking
    ALTER COLUMN BracketCap DROP NOT NULL;

COMMIT;

-- Conferencia: cadastros ativos com CAP zero (candidatos a NULL)
-- SELECT Id, Broker, Trader, NotionalFrom, Cap FROM financial_engine.equity_fee_brackets WHERE IsActive AND Cap = 0;
-- SELECT Id, Broker, Trader, CapMensal FROM financial_engine.bmf_fee_configs WHERE IsActive AND CapMensal = 0;
