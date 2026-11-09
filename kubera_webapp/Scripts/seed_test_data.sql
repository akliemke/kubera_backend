-- ============================================
-- Kubera - Dados Ficticios para Teste Manual
-- Periodo: Janeiro/2026, Brokers: 1 (BTG), 3 (XP)
-- ============================================

-- Garante brokers existem
INSERT INTO financial_engine.brokers (Code, Name, Document, Status, BillingCurrency, Contacts)
VALUES (1, 'BTG Pactual', '11.111.111/0001-11', true, 'BRL', ARRAY['btg@test.com'])
ON CONFLICT DO NOTHING;

INSERT INTO financial_engine.brokers (Code, Name, Document, Status, BillingCurrency, Contacts)
VALUES (3, 'XP Investimentos', '33.333.333/0001-33', true, 'BRL', ARRAY['xp@test.com'])
ON CONFLICT DO NOTHING;

-- ============================================
-- EQUITY FEE BRACKETS
-- ============================================

-- BTG (broker 1) - Faixas padrao (trader NULL)
INSERT INTO financial_engine.equity_fee_brackets
    (Broker, Trader, KeyType, ValidFrom, ValidTo, NotionalFrom, NotionalTo, Bps, Cap, IsActive)
VALUES
    (1, NULL, NULL, '2025-01-01', '2026-12-31', 0,        500000,   0.50, 250.00, true),
    (1, NULL, NULL, '2025-01-01', '2026-12-31', 500000,   2000000,  0.40, 800.00, true),
    (1, NULL, NULL, '2025-01-01', '2026-12-31', 2000000,  NULL,     0.30, 1500.00, true);

-- BTG (broker 1) - Faixas especiais para JOAO
INSERT INTO financial_engine.equity_fee_brackets
    (Broker, Trader, KeyType, ValidFrom, ValidTo, NotionalFrom, NotionalTo, Bps, Cap, IsActive)
VALUES
    (1, 'JOAO', 'TRADER', '2025-01-01', '2026-12-31', 0,        1000000,  0.45, 450.00, true),
    (1, 'JOAO', 'TRADER', '2025-01-01', '2026-12-31', 1000000,  NULL,     0.25, 1000.00, true);

-- XP (broker 3) - Faixas padrao (trader NULL)
INSERT INTO financial_engine.equity_fee_brackets
    (Broker, Trader, KeyType, ValidFrom, ValidTo, NotionalFrom, NotionalTo, Bps, Cap, IsActive)
VALUES
    (3, NULL, NULL, '2025-01-01', '2026-12-31', 0,        300000,   0.60, 180.00, true),
    (3, NULL, NULL, '2025-01-01', '2026-12-31', 300000,   1000000,  0.45, 450.00, true),
    (3, NULL, NULL, '2025-01-01', '2026-12-31', 1000000,  5000000,  0.35, 1750.00, true),
    (3, NULL, NULL, '2025-01-01', '2026-12-31', 5000000,  NULL,     0.25, 2500.00, true);

-- ============================================
-- BMF FEE CONFIGS
-- ============================================

-- BTG (broker 1) - Config padrao
INSERT INTO financial_engine.bmf_fee_configs
    (Broker, Trader, KeyType, ValidFrom, ValidTo, ValorMini, ValorCheio, ValorOutros, DI1Venc1a4, DI1Venc5, DI1Venc6, DI1Venc7, DI1Venc8Plus, CapMensal, IsActive)
VALUES
    (1, NULL, NULL, '2025-01-01', '2026-12-31', 0.50, 2.50, 1.00, 1.20, 1.00, 0.80, 0.60, 0.40, 5000.00, true);

-- BTG (broker 1) - Config especial CARLOS
INSERT INTO financial_engine.bmf_fee_configs
    (Broker, Trader, KeyType, ValidFrom, ValidTo, ValorMini, ValorCheio, ValorOutros, DI1Venc1a4, DI1Venc5, DI1Venc6, DI1Venc7, DI1Venc8Plus, CapMensal, IsActive)
VALUES
    (1, 'CARLOS', 'TRADER', '2025-01-01', '2026-12-31', 0.40, 2.00, 1.00, 1.00, 0.80, 0.65, 0.50, 0.35, 3000.00, true);

-- XP (broker 3) - Config padrao
INSERT INTO financial_engine.bmf_fee_configs
    (Broker, Trader, KeyType, ValidFrom, ValidTo, ValorMini, ValorCheio, ValorOutros, DI1Venc1a4, DI1Venc5, DI1Venc6, DI1Venc7, DI1Venc8Plus, CapMensal, IsActive)
VALUES
    (3, NULL, NULL, '2025-01-01', '2026-12-31', 0.55, 2.80, 1.00, 1.30, 1.10, 0.90, 0.70, 0.50, 6000.00, true);

-- ============================================
-- ORDERS - history.orders (broker 1 - BTG)
-- ============================================

-- Trader JOAO, EQUITY, 3 dias em janeiro
INSERT INTO history.orders (Id, CumQty, AvgPx, Symbol, EnteringTrader, Entity, TradeDate, EnteringFirm, OmsInstance, Side, businessDate)
VALUES
(900001, 1000, 28.50, 'PETR4', 'JOAO', 'MESA1', '2026-01-06', '1', 'OMS1', 'BUY', '2026-01-06'),
(900002, 2000, 58.30, 'VALE3', 'JOAO', 'MESA1', '2026-01-06', '1', 'OMS1', 'SELL', '2026-01-06'),
(900003, 500, 33.20, 'ITUB4', 'JOAO', 'MESA1', '2026-01-07', '1', 'OMS1', 'BUY', '2026-01-07'),
(900004, 3000, 12.45, 'BBDC4', 'JOAO', 'MESA1', '2026-01-07', '1', 'OMS1', 'BUY', '2026-01-07'),
(900005, 1500, 28.80, 'PETR4', 'JOAO', 'MESA1', '2026-01-08', '1', 'OMS1', 'SELL', '2026-01-08'),
(900006, 800, 95.10, 'WEGE3', 'JOAO', 'MESA1', '2026-01-08', '1', 'OMS1', 'BUY', '2026-01-08');

-- Trader MARIA, EQUITY, 2 dias
INSERT INTO history.orders (Id, CumQty, AvgPx, Symbol, EnteringTrader, Entity, TradeDate, EnteringFirm, OmsInstance, Side, businessDate)
VALUES
(900007, 5000, 28.60, 'PETR4', 'MARIA', 'MESA2', '2026-01-06', '1', 'OMS1', 'BUY', '2026-01-06'),
(900008, 1000, 45.70, 'ABEV3', 'MARIA', 'MESA2', '2026-01-06', '1', 'OMS1', 'SELL', '2026-01-06'),
(900009, 3000, 58.50, 'VALE3', 'MARIA', 'MESA2', '2026-01-08', '1', 'OMS1', 'BUY', '2026-01-08'),
(900010, 2000, 11.30, 'MGLU3', 'MARIA', 'MESA2', '2026-01-08', '1', 'OMS1', 'SELL', '2026-01-08');

-- Trader CARLOS, BMF, 2 dias
INSERT INTO history.orders (Id, CumQty, AvgPx, Symbol, EnteringTrader, Entity, TradeDate, EnteringFirm, OmsInstance, Side, businessDate)
VALUES
(900011, 10, 126500.00, 'WINFEB26', 'CARLOS', 'MESA3', '2026-01-06', '1', 'OMS1', 'BUY', '2026-01-06'),
(900012, 5, 126300.00, 'WINFEB26', 'CARLOS', 'MESA3', '2026-01-06', '1', 'OMS1', 'SELL', '2026-01-06'),
(900013, 20, 126800.00, 'WINFEB26', 'CARLOS', 'MESA3', '2026-01-07', '1', 'OMS1', 'BUY', '2026-01-07'),
(900014, 2, 5780.00, 'WDOFEB26', 'CARLOS', 'MESA3', '2026-01-07', '1', 'OMS1', 'SELL', '2026-01-07');

-- ============================================
-- ORDERS - history.orders (broker 3 - XP)
-- ============================================

-- Trader PEDRO, EQUITY
INSERT INTO history.orders (Id, CumQty, AvgPx, Symbol, EnteringTrader, Entity, TradeDate, EnteringFirm, OmsInstance, Side, businessDate)
VALUES
(900015, 4000, 28.70, 'PETR4', 'PEDRO', 'MESAXP', '2026-01-06', '3', 'OMS2', 'BUY', '2026-01-06'),
(900016, 1500, 33.40, 'ITUB4', 'PEDRO', 'MESAXP', '2026-01-06', '3', 'OMS2', 'SELL', '2026-01-06'),
(900017, 2500, 58.90, 'VALE3', 'PEDRO', 'MESAXP', '2026-01-07', '3', 'OMS2', 'BUY', '2026-01-07');

-- Trader ANA, BMF
INSERT INTO history.orders (Id, CumQty, AvgPx, Symbol, EnteringTrader, Entity, TradeDate, EnteringFirm, OmsInstance, Side, businessDate)
VALUES
(900018, 50, 126400.00, 'INDFEB26', 'ANA', 'MESAXP', '2026-01-07', '3', 'OMS2', 'BUY', '2026-01-07'),
(900019, 30, 5790.00, 'DOLFEB26', 'ANA', 'MESAXP', '2026-01-08', '3', 'OMS2', 'SELL', '2026-01-08');

-- ============================================
-- TRADES - history.trades (1 execucao por ordem; Asset = raiz, so existe nos trades)
-- ============================================
INSERT INTO history.trades (OrderId, LastQty, LastPx, Symbol, Asset, EnteringTrader, Entity, TradeDate, EnteringFirm, OmsInstance, Side, businessDate)
VALUES
(900001, 1000, 28.50, 'PETR4', 'PETR', 'JOAO', 'MESA1', '2026-01-06', '1', 'OMS1', 'BUY', '2026-01-06'),
(900002, 2000, 58.30, 'VALE3', 'VALE', 'JOAO', 'MESA1', '2026-01-06', '1', 'OMS1', 'SELL', '2026-01-06'),
(900003, 500, 33.20, 'ITUB4', 'ITUB', 'JOAO', 'MESA1', '2026-01-07', '1', 'OMS1', 'BUY', '2026-01-07'),
(900004, 3000, 12.45, 'BBDC4', 'BBDC', 'JOAO', 'MESA1', '2026-01-07', '1', 'OMS1', 'BUY', '2026-01-07'),
(900005, 1500, 28.80, 'PETR4', 'PETR', 'JOAO', 'MESA1', '2026-01-08', '1', 'OMS1', 'SELL', '2026-01-08'),
(900006, 800, 95.10, 'WEGE3', 'WEGE', 'JOAO', 'MESA1', '2026-01-08', '1', 'OMS1', 'BUY', '2026-01-08'),
(900007, 5000, 28.60, 'PETR4', 'PETR', 'MARIA', 'MESA2', '2026-01-06', '1', 'OMS1', 'BUY', '2026-01-06'),
(900008, 1000, 45.70, 'ABEV3', 'ABEV', 'MARIA', 'MESA2', '2026-01-06', '1', 'OMS1', 'SELL', '2026-01-06'),
(900009, 3000, 58.50, 'VALE3', 'VALE', 'MARIA', 'MESA2', '2026-01-08', '1', 'OMS1', 'BUY', '2026-01-08'),
(900010, 2000, 11.30, 'MGLU3', 'MGLU', 'MARIA', 'MESA2', '2026-01-08', '1', 'OMS1', 'SELL', '2026-01-08'),
(900011, 10, 126500.00, 'WINFEB26', 'WIN', 'CARLOS', 'MESA3', '2026-01-06', '1', 'OMS1', 'BUY', '2026-01-06'),
(900012, 5, 126300.00, 'WINFEB26', 'WIN', 'CARLOS', 'MESA3', '2026-01-06', '1', 'OMS1', 'SELL', '2026-01-06'),
(900013, 20, 126800.00, 'WINFEB26', 'WIN', 'CARLOS', 'MESA3', '2026-01-07', '1', 'OMS1', 'BUY', '2026-01-07'),
(900014, 2, 5780.00, 'WDOFEB26', 'WDO', 'CARLOS', 'MESA3', '2026-01-07', '1', 'OMS1', 'SELL', '2026-01-07'),
(900015, 4000, 28.70, 'PETR4', 'PETR', 'PEDRO', 'MESAXP', '2026-01-06', '3', 'OMS2', 'BUY', '2026-01-06'),
(900016, 1500, 33.40, 'ITUB4', 'ITUB', 'PEDRO', 'MESAXP', '2026-01-06', '3', 'OMS2', 'SELL', '2026-01-06'),
(900017, 2500, 58.90, 'VALE3', 'VALE', 'PEDRO', 'MESAXP', '2026-01-07', '3', 'OMS2', 'BUY', '2026-01-07'),
(900018, 50, 126400.00, 'INDFEB26', 'IND', 'ANA', 'MESAXP', '2026-01-07', '3', 'OMS2', 'BUY', '2026-01-07'),
(900019, 30, 5790.00, 'DOLFEB26', 'DOL', 'ANA', 'MESAXP', '2026-01-08', '3', 'OMS2', 'SELL', '2026-01-08');

-- ============================================
-- DAILY BILLING - Broker 1 (BTG)
-- ============================================

-- JOAO EQUITY dia 06/01 (notional: 28500 + 116600 = 145100)
INSERT INTO financial_engine.daily_billing
    (Broker, Trader, KeyType, TradeDate, AssetClass, TotalNotional, TotalQty, TotalFee, AccumulatedBefore, AccumulatedAfter, ProcessBillingId, CalculatedAt)
VALUES (1, 'JOAO', 'TRADER', '2026-01-06', 'EQUITY', 145100.00, 3000, 72.55, 0.00, 145100.00, 1, NOW())
RETURNING Id;
-- assume Id = lastval() para os order_billings abaixo

INSERT INTO financial_engine.order_billing (OrderId, DailyBillingId, Broker, Trader, KeyType, TradeDate, AssetClass, Notional, Qty, FeeAmount)
VALUES
(900001, currval('financial_engine.daily_billing_id_seq'), 1, 'JOAO', 'TRADER', '2026-01-06', 'EQUITY', 28500.00, 1000, 14.25),
(900002, currval('financial_engine.daily_billing_id_seq'), 1, 'JOAO', 'TRADER', '2026-01-06', 'EQUITY', 116600.00, 2000, 58.30);

-- JOAO EQUITY dia 07/01 (notional: 16600 + 37350 = 53950)
INSERT INTO financial_engine.daily_billing
    (Broker, Trader, KeyType, TradeDate, AssetClass, TotalNotional, TotalQty, TotalFee, AccumulatedBefore, AccumulatedAfter, ProcessBillingId, CalculatedAt)
VALUES (1, 'JOAO', 'TRADER', '2026-01-07', 'EQUITY', 53950.00, 3500, 26.98, 145100.00, 199050.00, 1, NOW());

INSERT INTO financial_engine.order_billing (OrderId, DailyBillingId, Broker, Trader, KeyType, TradeDate, AssetClass, Notional, Qty, FeeAmount)
VALUES
(900003, currval('financial_engine.daily_billing_id_seq'), 1, 'JOAO', 'TRADER', '2026-01-07', 'EQUITY', 16600.00, 500, 8.30),
(900004, currval('financial_engine.daily_billing_id_seq'), 1, 'JOAO', 'TRADER', '2026-01-07', 'EQUITY', 37350.00, 3000, 18.68);

-- JOAO EQUITY dia 08/01 (notional: 43200 + 76080 = 119280)
INSERT INTO financial_engine.daily_billing
    (Broker, Trader, KeyType, TradeDate, AssetClass, TotalNotional, TotalQty, TotalFee, AccumulatedBefore, AccumulatedAfter, ProcessBillingId, CalculatedAt)
VALUES (1, 'JOAO', 'TRADER', '2026-01-08', 'EQUITY', 119280.00, 2300, 59.64, 199050.00, 318330.00, 1, NOW());

INSERT INTO financial_engine.order_billing (OrderId, DailyBillingId, Broker, Trader, KeyType, TradeDate, AssetClass, Notional, Qty, FeeAmount)
VALUES
(900005, currval('financial_engine.daily_billing_id_seq'), 1, 'JOAO', 'TRADER', '2026-01-08', 'EQUITY', 43200.00, 1500, 21.60),
(900006, currval('financial_engine.daily_billing_id_seq'), 1, 'JOAO', 'TRADER', '2026-01-08', 'EQUITY', 76080.00, 800, 38.04);

-- MARIA EQUITY dia 06/01 (notional: 143000 + 45700 = 188700)
INSERT INTO financial_engine.daily_billing
    (Broker, Trader, KeyType, TradeDate, AssetClass, TotalNotional, TotalQty, TotalFee, AccumulatedBefore, AccumulatedAfter, ProcessBillingId, CalculatedAt)
VALUES (1, 'MARIA', 'TRADER', '2026-01-06', 'EQUITY', 188700.00, 6000, 94.35, 0.00, 188700.00, 1, NOW());

INSERT INTO financial_engine.order_billing (OrderId, DailyBillingId, Broker, Trader, KeyType, TradeDate, AssetClass, Notional, Qty, FeeAmount)
VALUES
(900007, currval('financial_engine.daily_billing_id_seq'), 1, 'MARIA', 'TRADER', '2026-01-06', 'EQUITY', 143000.00, 5000, 71.50),
(900008, currval('financial_engine.daily_billing_id_seq'), 1, 'MARIA', 'TRADER', '2026-01-06', 'EQUITY', 45700.00, 1000, 22.85);

-- MARIA EQUITY dia 08/01 (notional: 175500 + 22600 = 198100)
INSERT INTO financial_engine.daily_billing
    (Broker, Trader, KeyType, TradeDate, AssetClass, TotalNotional, TotalQty, TotalFee, AccumulatedBefore, AccumulatedAfter, ProcessBillingId, CalculatedAt)
VALUES (1, 'MARIA', 'TRADER', '2026-01-08', 'EQUITY', 198100.00, 5000, 99.05, 188700.00, 386800.00, 1, NOW());

INSERT INTO financial_engine.order_billing (OrderId, DailyBillingId, Broker, Trader, KeyType, TradeDate, AssetClass, Notional, Qty, FeeAmount)
VALUES
(900009, currval('financial_engine.daily_billing_id_seq'), 1, 'MARIA', 'TRADER', '2026-01-08', 'EQUITY', 175500.00, 3000, 87.75),
(900010, currval('financial_engine.daily_billing_id_seq'), 1, 'MARIA', 'TRADER', '2026-01-08', 'EQUITY', 22600.00, 2000, 11.30);

-- CARLOS BMF dia 06/01 (qty: 10 + 5 = 15 mini)
INSERT INTO financial_engine.daily_billing
    (Broker, Trader, KeyType, TradeDate, AssetClass, TotalNotional, TotalQty, TotalFee, AccumulatedBefore, AccumulatedAfter, ProcessBillingId, CalculatedAt)
VALUES (1, 'CARLOS', 'TRADER', '2026-01-06', 'BMF', 0.00, 15, 7.50, 0.00, 15.00, 1, NOW());

INSERT INTO financial_engine.order_billing (OrderId, DailyBillingId, Broker, Trader, KeyType, TradeDate, AssetClass, Notional, Qty, FeeAmount)
VALUES
(900011, currval('financial_engine.daily_billing_id_seq'), 1, 'CARLOS', 'TRADER', '2026-01-06', 'BMF', 0.00, 10, 5.00),
(900012, currval('financial_engine.daily_billing_id_seq'), 1, 'CARLOS', 'TRADER', '2026-01-06', 'BMF', 0.00, 5, 2.50);

-- CARLOS BMF dia 07/01 (qty: 20 + 2 = 22)
INSERT INTO financial_engine.daily_billing
    (Broker, Trader, KeyType, TradeDate, AssetClass, TotalNotional, TotalQty, TotalFee, AccumulatedBefore, AccumulatedAfter, ProcessBillingId, CalculatedAt)
VALUES (1, 'CARLOS', 'TRADER', '2026-01-07', 'BMF', 0.00, 22, 11.00, 15.00, 37.00, 1, NOW());

INSERT INTO financial_engine.order_billing (OrderId, DailyBillingId, Broker, Trader, KeyType, TradeDate, AssetClass, Notional, Qty, FeeAmount)
VALUES
(900013, currval('financial_engine.daily_billing_id_seq'), 1, 'CARLOS', 'TRADER', '2026-01-07', 'BMF', 0.00, 20, 10.00),
(900014, currval('financial_engine.daily_billing_id_seq'), 1, 'CARLOS', 'TRADER', '2026-01-07', 'BMF', 0.00, 2, 1.00);

-- ============================================
-- DAILY BILLING - Broker 3 (XP)
-- ============================================

-- PEDRO EQUITY dia 06/01 (notional: 114800 + 50100 = 164900)
INSERT INTO financial_engine.daily_billing
    (Broker, Trader, KeyType, TradeDate, AssetClass, TotalNotional, TotalQty, TotalFee, AccumulatedBefore, AccumulatedAfter, ProcessBillingId, CalculatedAt)
VALUES (3, 'PEDRO', 'TRADER', '2026-01-06', 'EQUITY', 164900.00, 5500, 82.45, 0.00, 164900.00, 2, NOW());

INSERT INTO financial_engine.order_billing (OrderId, DailyBillingId, Broker, Trader, KeyType, TradeDate, AssetClass, Notional, Qty, FeeAmount)
VALUES
(900015, currval('financial_engine.daily_billing_id_seq'), 3, 'PEDRO', 'TRADER', '2026-01-06', 'EQUITY', 114800.00, 4000, 57.40),
(900016, currval('financial_engine.daily_billing_id_seq'), 3, 'PEDRO', 'TRADER', '2026-01-06', 'EQUITY', 50100.00, 1500, 25.05);

-- PEDRO EQUITY dia 07/01 (notional: 147250)
INSERT INTO financial_engine.daily_billing
    (Broker, Trader, KeyType, TradeDate, AssetClass, TotalNotional, TotalQty, TotalFee, AccumulatedBefore, AccumulatedAfter, ProcessBillingId, CalculatedAt)
VALUES (3, 'PEDRO', 'TRADER', '2026-01-07', 'EQUITY', 147250.00, 2500, 73.63, 164900.00, 312150.00, 2, NOW());

INSERT INTO financial_engine.order_billing (OrderId, DailyBillingId, Broker, Trader, KeyType, TradeDate, AssetClass, Notional, Qty, FeeAmount)
VALUES
(900017, currval('financial_engine.daily_billing_id_seq'), 3, 'PEDRO', 'TRADER', '2026-01-07', 'EQUITY', 147250.00, 2500, 73.63);

-- ANA BMF dia 07/01 (qty: 50 cheio)
INSERT INTO financial_engine.daily_billing
    (Broker, Trader, KeyType, TradeDate, AssetClass, TotalNotional, TotalQty, TotalFee, AccumulatedBefore, AccumulatedAfter, ProcessBillingId, CalculatedAt)
VALUES (3, 'ANA', 'TRADER', '2026-01-07', 'BMF', 0.00, 50, 125.00, 0.00, 50.00, 2, NOW());

INSERT INTO financial_engine.order_billing (OrderId, DailyBillingId, Broker, Trader, KeyType, TradeDate, AssetClass, Notional, Qty, FeeAmount)
VALUES
(900018, currval('financial_engine.daily_billing_id_seq'), 3, 'ANA', 'TRADER', '2026-01-07', 'BMF', 0.00, 50, 125.00);

-- ANA BMF dia 08/01 (qty: 30 cheio)
INSERT INTO financial_engine.daily_billing
    (Broker, Trader, KeyType, TradeDate, AssetClass, TotalNotional, TotalQty, TotalFee, AccumulatedBefore, AccumulatedAfter, ProcessBillingId, CalculatedAt)
VALUES (3, 'ANA', 'TRADER', '2026-01-08', 'BMF', 0.00, 30, 75.00, 50.00, 80.00, 2, NOW());

INSERT INTO financial_engine.order_billing (OrderId, DailyBillingId, Broker, Trader, KeyType, TradeDate, AssetClass, Notional, Qty, FeeAmount)
VALUES
(900019, currval('financial_engine.daily_billing_id_seq'), 3, 'ANA', 'TRADER', '2026-01-08', 'BMF', 0.00, 30, 75.00);

-- ============================================
-- Process billing entries (referenciados acima)
-- ============================================
INSERT INTO financial_engine.process_billing (ProcessId, ProcessDate, Status, ProcessBroker)
VALUES
(1, '2026-01-06', 'COMPLETED', '1'),
(2, '2026-01-06', 'COMPLETED', '3')
ON CONFLICT DO NOTHING;

-- IDs explicitos nao avancam a identity: realinha para o proximo INSERT nao colidir
SELECT setval(
    pg_get_serial_sequence('financial_engine.process_billing', 'processid'),
    (SELECT MAX(ProcessId) FROM financial_engine.process_billing));
