# Multi-Context Join
Join data from three different platforms (SQL, Postgres, and CSV) in a single engine statement.

```sql
-- Pre-requisite: connections named mssql_conn, pg_conn, csv_conn must be established
CREATE CONNECTION mssql_conn AS MSSQL(SERVER='sql01', DATABASE='Sales', TRUSTED_CONNECTION=TRUE);
CREATE CONNECTION pg_conn    AS POSTGRES(HOST='pg01', DATABASE='Geo', USER='etl', PASSWORD='...');
CREATE CONNECTION csv_conn   AS FLATFILE('C:\Data\coupons.csv', HEADER=ON);

-- The engine stages each source and joins them in engine memory
SELECT 
    S.ID, S.Name, 
    P.Region, 
    C.DiscountCode
INTO #CrossPlatformResult
FROM mssql_conn.Sales AS S
JOIN pg_conn.Territories AS P ON S.TerritoryID = P.ID
JOIN csv_conn             AS C ON S.PromoID     = C.ID
WHERE S.Total > 5000;

SELECT * FROM #CrossPlatformResult ORDER BY S.ID;
```
