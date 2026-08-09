# The Secure Vendor Handshake (Export & Transmit)
A robust pattern for exporting sensitive internal data, securing it, and transmitting it to a vendor SFTP.

**Pattern Scenario:** Monthly Ledger Export → Zip → Encrypt → SFTP.

```sql
-- Setup connections
-- Note: SFTP uses PASSWORD, not PASS
CREATE CONNECTION sftp_vendor AS SFTP('vendor.corp.com', USER='ext_user', PASSWORD='...');
CREATE CONNECTION ledger_out AS FLATFILE('C:\Exports\monthly_ledger.csv', HEADER=ON);

BEGIN TRY
    -- 1. Extract to local formatted file via a FLATFILE connection
    INSERT INTO ledger_out
    SELECT AccountID, Balance, Status
    FROM #ProdLedger
    WHERE MonthID = 202604;

    -- 2. Post-Processing (Archive & Secure)
    -- Note: WITH() options use = for assignment
    COMPRESS FILE 'C:\Exports\monthly_ledger.csv' TO 'C:\Exports\ledger.zip' WITH(OVERWRITE=ON);
    ENCRYPT FILE  'C:\Exports\ledger.zip' TO 'C:\Exports\ledger.zip.enc' PASSWORD('MasterSecret2026') WITH(OVERWRITE=ON);

    -- 3. Transmit
    SEND FILE 'C:\Exports\ledger.zip.enc' TO '/inbox/incoming/' AT sftp_vendor;

    -- 4. Local Cleanup
    DELETE FILE 'C:\Exports\monthly_ledger.csv';
    DELETE FILE 'C:\Exports\ledger.zip';
    
    PRINT 'Vendor transmission successful.';
END TRY
BEGIN CATCH
    -- Alert on failure — use a string expression for BODY
    SEND EMAIL
        FROM    'alerts@corp.com'
        TO      'admin@corp.com'
        SUBJECT 'VENDOR EXPORT FAILED'
        BODY    ('Export pipeline failed: ' + ERROR_MESSAGE())
        AT      alerts_smtp;
    THROW;
END CATCH;
```
