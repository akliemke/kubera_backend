-- Ordens cobraveis passam a ser montadas no codigo (OrdersRepository + BillableOrderBuilder):
-- a view sai e o mapa Symbol -> Asset do dia precisa de indice em trades(businessDate).
-- Bancos novos: usar schema.sql direto.
BEGIN;

DROP VIEW IF EXISTS history.vw_billable_orders;

CREATE INDEX IF NOT EXISTS idx_trades_business_date ON history.trades(businessDate);

COMMIT;
