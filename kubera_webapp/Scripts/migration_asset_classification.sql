-- Migracao de bancos criados antes da classificacao por Asset (AssetClass deduzida no codigo).
-- Bancos novos: usar schema.sql direto.
BEGIN;

-- A view depende das colunas antigas
DROP VIEW IF EXISTS history.vw_billable_orders;

-- history.orders / trades: sai AssetClass e ExchangeSymbol. Asset (raiz) existe so nos trades
ALTER TABLE history.trades ADD COLUMN IF NOT EXISTS Asset VARCHAR(50) NULL;
ALTER TABLE history.trades ADD COLUMN IF NOT EXISTS UniqueTradeId VARCHAR(100) NULL;

-- Preenche Asset a partir do ExchangeSymbol antigo (que ja era a raiz: WIN, DOL, DI1...) ou do Symbol
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM information_schema.columns
               WHERE table_schema = 'history' AND table_name = 'trades' AND column_name = 'exchangesymbol') THEN
        UPDATE history.trades SET Asset = ExchangeSymbol WHERE Asset IS NULL AND ExchangeSymbol IS NOT NULL;
    END IF;
END $$;

-- Sem ExchangeSymbol: EQUITY = 4 primeiros chars do Symbol, BMF = 3
UPDATE history.trades SET Asset = LEFT(Symbol, CASE WHEN Symbol ~ '^[A-Z0-9]{3}[FGHJKMNQUVXZ][0-9]{2}' THEN 3 ELSE 4 END)
WHERE Asset IS NULL;

ALTER TABLE history.orders DROP COLUMN IF EXISTS Asset;
ALTER TABLE history.orders DROP COLUMN IF EXISTS AssetClass;
ALTER TABLE history.orders DROP COLUMN IF EXISTS ExchangeSymbol;
ALTER TABLE history.trades DROP COLUMN IF EXISTS AssetClass;
ALTER TABLE history.trades DROP COLUMN IF EXISTS ExchangeSymbol;

-- Billing
ALTER TABLE financial_engine.order_billing ADD COLUMN IF NOT EXISTS Asset VARCHAR(50) NULL;
ALTER TABLE financial_engine.bmf_fee_configs ADD COLUMN IF NOT EXISTS ValorOutros DECIMAL(10,4) NOT NULL DEFAULT 0;

-- Ordens cobraveis do dia. AssetClass nao existe na base: e deduzida no codigo (AssetClassifier) por Asset/Symbol.
-- history.orders nao tem Asset: a raiz vem do primeiro trade da ordem (ta). Ordem sem trade -> Asset NULL
-- (o codigo deduz pelo Symbol). Sintetico = Symbol que nao comeca com o Asset dos trades (ex: BTGDOLZ26, Asset DOL).
CREATE VIEW history.vw_billable_orders AS

-- Ordem normal (nao GTC/GTD, nao sintetico): passthrough
SELECT o.Id, o.CumQty, o.AvgPx, o.Symbol, ta.Asset,
       o.EnteringTrader, o.Entity, o.TradeDate, o.EnteringFirm, o.OmsInstance, o.Side,
       o.businessDate, o.TimeInForce
FROM history.orders o
LEFT JOIN LATERAL (
    SELECT t.Asset FROM history.trades t
    WHERE t.OrderId = o.Id AND t.Asset IS NOT NULL
    ORDER BY t.Id LIMIT 1
) ta ON TRUE
WHERE (o.TimeInForce IS NULL OR o.TimeInForce NOT IN ('GTC', 'GTD'))
  AND (ta.Asset IS NULL OR o.Symbol LIKE ta.Asset || '%')

UNION ALL

-- GTC/GTD (so existe em EQUITY): agrega trades do dia (VWAP)
SELECT o.Id,
       SUM(t.LastQty) as CumQty,
       SUM(t.LastQty * t.LastPx) / SUM(t.LastQty) as AvgPx,
       o.Symbol, ta.Asset,
       o.EnteringTrader, o.Entity, o.TradeDate, o.EnteringFirm,
       o.OmsInstance, o.Side, o.businessDate, o.TimeInForce
FROM history.orders o
INNER JOIN history.trades t ON t.OrderId = o.Id AND t.businessDate = o.businessDate
LEFT JOIN LATERAL (
    SELECT t2.Asset FROM history.trades t2
    WHERE t2.OrderId = o.Id AND t2.Asset IS NOT NULL
    ORDER BY t2.Id LIMIT 1
) ta ON TRUE
WHERE o.TimeInForce IN ('GTC', 'GTD')
  AND (ta.Asset IS NULL OR o.Symbol LIKE ta.Asset || '%')
GROUP BY o.Id, o.Symbol, ta.Asset,
         o.EnteringTrader, o.Entity, o.TradeDate, o.EnteringFirm,
         o.OmsInstance, o.Side, o.businessDate, o.TimeInForce

UNION ALL

-- Sintetico: 1 linha por perna (sufixo do UniqueTradeId), Asset = perna.
-- LastQty vem no padrao do cheio: converte pelo fator da perna (1 DOL = 5 WDO). Perna sem fator = 1;
-- trade sem sufixo vira perna '?' (nao classificada: nao cobrada, gera warning).
SELECT o.Id,
       SUM(t.LastQty) * COALESCE(MAX(f.Factor), 1) as CumQty,
       SUM(t.LastQty * t.LastPx) / SUM(t.LastQty) as AvgPx,
       o.Symbol, leg.Leg as Asset,
       o.EnteringTrader, o.Entity, o.TradeDate, o.EnteringFirm,
       o.OmsInstance, o.Side, o.businessDate, o.TimeInForce
FROM history.orders o
INNER JOIN history.trades t ON t.OrderId = o.Id AND t.businessDate = o.businessDate
INNER JOIN LATERAL (
    SELECT t2.Asset FROM history.trades t2
    WHERE t2.OrderId = o.Id AND t2.Asset IS NOT NULL
    ORDER BY t2.Id LIMIT 1
) ta ON o.Symbol NOT LIKE ta.Asset || '%'
CROSS JOIN LATERAL (
    SELECT COALESCE(UPPER(substring(t.UniqueTradeId from '-([^-]+)$')), '?') as Leg
) leg
LEFT JOIN (VALUES ('WDO', 5), ('DOL', 1)) AS f(Leg, Factor) ON f.Leg = leg.Leg
GROUP BY o.Id, leg.Leg, o.Symbol,
         o.EnteringTrader, o.Entity, o.TradeDate, o.EnteringFirm,
         o.OmsInstance, o.Side, o.businessDate, o.TimeInForce;

COMMIT;
