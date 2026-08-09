# Automated SFTP Bursting
Split a single large production table into multiple encrypted country-specific CSV files and SFTP them to separate vendor folders.

```sql
CREATE CONNECTION prod        AS MSSQL(SERVER='prod-db', DATABASE='Sales', TRUSTED_CONNECTION=TRUE);
CREATE CONNECTION vendor_sftp AS SFTP(HOST='sftp.vendor.com', USER='upload', PASSWORD='...');

DECLARE @Countries LIST = (SELECT DISTINCT Country FROM prod.Sales);

FOREACH @C IN @Countries
BEGIN
    DECLARE @OutFile     = 'C:\Exports\' + @C + '_Sales.csv';
    DECLARE @EncFile     = @OutFile + '.enc';
    DECLARE @RemotePath  = '/inbox/' + @C + '/';

    -- Create the per-country CSV connection and write
    CREATE OR ALTER CONNECTION country_out AS FLATFILE(@OutFile, HEADER=ON);
    INSERT INTO country_out SELECT * FROM prod.Sales WHERE Country = @C;

    -- Encrypt and transmit (SQL style — includes password)
    ENCRYPT FILE @OutFile TO @EncFile PASSWORD('ExportSecret2026') WITH(OVERWRITE=ON);
    SEND FILE @EncFile TO @RemotePath AT vendor_sftp;

    -- Cleanup local files
    DELETE FILE @OutFile;
    DELETE FILE @EncFile;

    PRINT 'Exported and transmitted: ' + @C;
END
```
