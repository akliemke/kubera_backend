-- ============================================
-- Schema history (orders de sistema externo)
-- ============================================

CREATE SCHEMA history AUTHORIZATION "admin";

-- ============================================
-- Orders
-- ============================================
CREATE TABLE history.orders (
    Id BIGSERIAL PRIMARY KEY,
    CumQty DECIMAL(18,4) NOT NULL,
    AvgPx DECIMAL(18,6) NOT NULL,
    Symbol VARCHAR(50) NOT NULL,
    ExchangeSymbol VARCHAR(50),
    AssetClass VARCHAR(20) NOT NULL,
    EnteringTrader VARCHAR(100) NOT NULL,
    Entity VARCHAR(100) NOT NULL,
    TradeDate DATE NOT NULL,
    EnteringFirm VARCHAR(20) NOT NULL,
    OmsInstance VARCHAR(50) NOT NULL,
    Side VARCHAR(10) NOT NULL,
    businessDate VARCHAR(20) NOT NULL,
    TimeInForce VARCHAR(10) NULL
);

CREATE INDEX idx_orders_business_date ON history.orders(businessDate);
CREATE INDEX idx_orders_firm_date ON history.orders(EnteringFirm, businessDate);

-- ============================================
-- Trades (para GTC/GTD - aggregacao diaria)
-- ============================================
CREATE TABLE history.trades (
    Id BIGSERIAL PRIMARY KEY,
    OrderId BIGINT NOT NULL,
    LastQty DECIMAL(18,4) NOT NULL,
    LastPx DECIMAL(18,6) NOT NULL,
    Symbol VARCHAR(50) NOT NULL,
    ExchangeSymbol VARCHAR(50),
    AssetClass VARCHAR(20) NOT NULL,
    EnteringTrader VARCHAR(100) NOT NULL,
    Entity VARCHAR(100) NOT NULL,
    TradeDate DATE NOT NULL,
    EnteringFirm VARCHAR(20) NOT NULL,
    OmsInstance VARCHAR(50) NOT NULL,
    Side VARCHAR(10) NOT NULL,
    businessDate VARCHAR(20) NOT NULL
);

CREATE INDEX idx_trades_orderid_date ON history.trades(OrderId, businessDate);

-- ============================================
-- View: billable orders (trata GTC/GTD EQUITY)
-- ============================================
CREATE OR REPLACE VIEW history.vw_billable_orders AS

-- Non-GTC/GTD ou non-EQUITY: passthrough direto
SELECT Id, CumQty, AvgPx, Symbol, ExchangeSymbol, AssetClass,
       EnteringTrader, Entity, TradeDate, EnteringFirm, OmsInstance, Side,
       businessDate, TimeInForce
FROM history.orders
WHERE TimeInForce IS NULL
   OR TimeInForce NOT IN ('GTC', 'GTD')
   OR AssetClass != 'EQUITY'

UNION ALL

-- GTC/GTD EQUITY: agrega trades do dia (VWAP)
SELECT o.Id,
       SUM(t.LastQty) as CumQty,
       SUM(t.LastQty * t.LastPx) / SUM(t.LastQty) as AvgPx,
       o.Symbol, o.ExchangeSymbol, o.AssetClass,
       o.EnteringTrader, o.Entity, o.TradeDate, o.EnteringFirm,
       o.OmsInstance, o.Side, o.businessDate, o.TimeInForce
FROM history.orders o
INNER JOIN history.trades t ON t.OrderId = o.Id AND t.businessDate = o.businessDate
WHERE o.TimeInForce IN ('GTC', 'GTD')
  AND o.AssetClass = 'EQUITY'
GROUP BY o.Id, o.Symbol, o.ExchangeSymbol, o.AssetClass,
         o.EnteringTrader, o.Entity, o.TradeDate, o.EnteringFirm,
         o.OmsInstance, o.Side, o.businessDate, o.TimeInForce;
