Type: CANDLESTICK
An OHLC candlestick chart for visualizing price movement over time. Each candle shows Open, High, Low, and Close values for a period.

Mappings:
- **X** - date or period label on the category axis (required)
- **OPEN** - opening price (required)
- **HIGH** - session high (required)
- **LOW** - session low (required)
- **CLOSE** - closing price (required)

Options:
- **COLOR_UP = '#hex'** - candle color when close >= open (default green)
- **COLOR_DOWN = '#hex'** - candle color when close < open (default red)
  TITLE      = 'text'

Positional fallback: if MAPPINGS are omitted, columns are assumed in X, OPEN, HIGH, LOW, CLOSE order.

```sql
SELECT
    CAST(trade_date AS VARCHAR(10))  AS period,
    open_price,
    high_price,
    low_price,
    close_price
  INTO #ohlc
  FROM dbo.StockPrices
  WHERE ticker = 'ACME'
    AND trade_date >= DATEADD(day, -90, GETDATE())
  ORDER BY trade_date;

CREATE VISUAL AcmeChart AS CANDLESTICK (
  SOURCE   = #ohlc,
  MAPPINGS (X = period, OPEN = open_price, HIGH = high_price, LOW = low_price, CLOSE = close_price),
  OPTIONS  (COLOR_UP = '#26a69a', COLOR_DOWN = '#ef5350', TITLE = 'ACME 90-Day Price')
);
```

References:
- [Report SQL Guide](../../../guides/report-sql.md)
