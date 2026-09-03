# CANDLESTICK

An OHLC candlestick chart for visualizing financial security and asset price movements over time. Each candle displays the Open, High, Low, and Close prices for a period, with optional volume bars, wick styling, and trendline overlays.

## Syntax

```sql
CREATE VISUAL VisualName AS CANDLESTICK (
  SOURCE = #tableName,
  MAPPINGS (
    X = DateColumn,
    OPEN = OpenColumn,
    HIGH = HighColumn,
    LOW = LowColumn,
    CLOSE = CloseColumn,
    VOLUME = VolumeColumn
  ),
  OPTIONS (
    TITLE = 'Stock Price History',
    COLOR_UP = '#26a69a',
    COLOR_DOWN = '#ef5350',
    WICK_COLOR = '#666666',
    WICK_COLOR_UP = '#26a69a',
    WICK_COLOR_DOWN = '#ef5350',
    VOLUME_COLOR = '#94a3b8'
  ),
  OVERLAYS (
    MOVING_AVG(5) AS SOLID WITH (COLOR = '#f59e0b', LABEL = '5-Period MA'),
    GOAL(150) AS DASHED WITH (COLOR = '#ef4444', LABEL = 'Target')
  )
);
```

## Mappings

- **X** — Date, timestamp, or period label on the horizontal axis (required).
- **OPEN** — Opening price for the period (required).
- **HIGH** — Highest price reached during the period (required).
- **LOW** — Lowest price reached during the period (required).
- **CLOSE** — Closing price for the period (required).
- **VOLUME** — Trading volume for the period, automatically rendered as secondary bars beneath candles (optional).

## Options

- **COLOR_UP = '#hex'** — Color for bullish candles where close >= open (default `#26a69a`).
- **COLOR_DOWN = '#hex'** — Color for bearish candles where close < open (default `#ef5350`).
- **WICK_COLOR = '#hex'** — Overrides the shadow/wick line color for all candles (optional).
- **WICK_COLOR_UP = '#hex'** — Overrides the wick line color for bullish candles (optional).
- **WICK_COLOR_DOWN = '#hex'** — Overrides the wick line color for bearish candles (optional).
- **VOLUME_COLOR = '#hex'** — Fill color for secondary volume bars when `VOLUME` is mapped (default `#94a3b8`).
- **TITLE = 'text'** — Visual title displayed above the chart.

## Overlays

Candlestick charts support analytical and reference overlays computed against the closing price:

- **MOVING_AVG(n)** — Simple moving average line across `n` periods.
- **GOAL(value)** — Horizontal benchmark or price target line.
- **AVERAGE** — Horizontal reference line at the mean close price.
- **REFERENCE_LINE (VALUE = n, LABEL = '...')** — Explicit price reference line.
- **REFERENCE_BAND (LOW = n, HIGH = n, COLOR = '...')** — Highlighted horizontal price zone or channel.

## Examples

### Basic OHLC with Custom Candle & Wick Colors

```sql
SELECT '2026-05-01' AS TradeDate, 100.0 AS OpenPrice, 110.0 AS HighPrice, 95.0 AS LowPrice, 105.0 AS ClosePrice INTO #market
UNION ALL SELECT '2026-05-02', 105.0, 115.0, 102.0, 112.0
UNION ALL SELECT '2026-05-03', 112.0, 113.0, 100.0, 101.0
UNION ALL SELECT '2026-05-04', 101.0, 108.0, 98.0, 106.0;

CREATE VISUAL DailyStockChart AS CANDLESTICK (
  SOURCE = #market,
  MAPPINGS (
    X = TradeDate,
    OPEN = OpenPrice,
    HIGH = HighPrice,
    LOW = LowPrice,
    CLOSE = ClosePrice
  ),
  OPTIONS (
    TITLE = 'Daily Price Action',
    COLOR_UP = '#16a34a',
    COLOR_DOWN = '#dc2626',
    WICK_COLOR = '#334155'
  )
);
```

### Candlestick with Volume Bars and Moving Average Overlay

```sql
SELECT '2026-05-01' AS TradeDate, 150.0 AS OpenPrice, 155.0 AS HighPrice, 148.0 AS LowPrice, 153.0 AS ClosePrice, 12000 AS Volume INTO #stock_data
UNION ALL SELECT '2026-05-02', 153.0, 158.0, 151.0, 156.0, 15400
UNION ALL SELECT '2026-05-03', 156.0, 157.0, 149.0, 150.0, 18200
UNION ALL SELECT '2026-05-04', 150.0, 162.0, 149.5, 161.0, 22000
UNION ALL SELECT '2026-05-05', 161.0, 165.0, 159.0, 164.0, 19500;

CREATE VISUAL FinancialOverview AS CANDLESTICK (
  SOURCE = #stock_data,
  MAPPINGS (
    X = TradeDate,
    OPEN = OpenPrice,
    HIGH = HighPrice,
    LOW = LowPrice,
    CLOSE = ClosePrice,
    VOLUME = Volume
  ),
  OPTIONS (
    TITLE = 'Stock Price & Volume',
    VOLUME_COLOR = '#64748b'
  ),
  OVERLAYS (
    MOVING_AVG(3) AS SOLID WITH (COLOR = '#f59e0b', LABEL = '3-Day SMA'),
    GOAL(160) AS DASHED WITH (COLOR = '#ef4444', LABEL = 'Resistance')
  )
);
```

## References

- [Report SQL Guide](../../../guides/feature-guides/report-sql.md)
- [Visual Reference](../README.md)
