-- Ordens/trades passam a vir de history.orders_new / history.trades_new (estrutura do sistema externo):
-- OrderId string (GUID) no lugar de Id BIGSERIAL, ExecBroker (era EnteringFirm), Instance (era OmsInstance),
-- sem businessDate (o dia e o TradeDate). order_billing.OrderId acompanha o tipo.
-- history.orders / history.trades antigas nao sao mais lidas (nao sao removidas aqui).
-- Bancos novos: usar schema.sql direto.
BEGIN;

CREATE TABLE IF NOT EXISTS history.orders_new (
    OrderId VARCHAR(100) NOT NULL,
    CumQty DECIMAL(18,4) NOT NULL,
    AvgPx DECIMAL(18,6) NOT NULL,
    Symbol VARCHAR(50) NOT NULL,
    EnteringTrader VARCHAR(100) NOT NULL,
    Entity VARCHAR(100) NOT NULL,
    TradeDate DATE NOT NULL,
    ExecBroker VARCHAR(20) NOT NULL,
    Instance VARCHAR(50) NOT NULL,
    Side VARCHAR(10) NOT NULL,
    TimeInForce VARCHAR(10) NULL,
    PRIMARY KEY (OrderId, TradeDate)
);

CREATE INDEX IF NOT EXISTS idx_orders_new_trade_date ON history.orders_new(TradeDate);
CREATE INDEX IF NOT EXISTS idx_orders_new_broker_date ON history.orders_new(ExecBroker, TradeDate);

CREATE TABLE IF NOT EXISTS history.trades_new (
    Id BIGSERIAL PRIMARY KEY,
    OrderId VARCHAR(100) NOT NULL,
    Qty DECIMAL(18,4) NOT NULL,
    Price DECIMAL(18,6) NOT NULL,
    UniqueTradeId VARCHAR(100) NULL,
    Symbol VARCHAR(50) NOT NULL,
    Asset VARCHAR(50) NULL,
    EnteringTrader VARCHAR(100) NOT NULL,
    EntityId VARCHAR(100) NOT NULL,     -- trades nao tem Entity, so o id (nao usado no calculo)
    TradeDate DATE NOT NULL,
    ExecBroker VARCHAR(20) NOT NULL,
    Instance VARCHAR(50) NOT NULL,
    Side VARCHAR(10) NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_trades_new_orderid_date ON history.trades_new(OrderId, TradeDate);
CREATE INDEX IF NOT EXISTS idx_trades_new_trade_date ON history.trades_new(TradeDate);

ALTER TABLE financial_engine.order_billing
    ALTER COLUMN OrderId TYPE VARCHAR(100) USING OrderId::text;

COMMIT;
