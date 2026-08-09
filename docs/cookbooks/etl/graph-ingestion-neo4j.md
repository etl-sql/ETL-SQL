# Graph Ingestion & Querying (Neo4j)

This recipe demonstrates how to ingest relational nodes and edges into Neo4j, and query them back using native Cypher pushdown block syntax.

```sql
-- Connect to relational source and graph target
CREATE CONNECTION sql_src AS POSTGRES(HOST='localhost', DATABASE='crm', USER='admin', PASSWORD='password');
CREATE CONNECTION graph AS NEO4J(
    URI='bolt://localhost:7687',
    USER='neo4j',
    PASSWORD='password',
    KEY_COLUMNS='customer_id'
);
CREATE CONNECTION graph_edges AS NEO4J(
    URI='bolt://localhost:7687',
    USER='neo4j',
    PASSWORD='password',
    FROM_LABEL='CUSTOMER',
    TO_LABEL='CUSTOMER',
    FROM_KEY_COLUMN='customer_id',
    TO_KEY_COLUMN='customer_id',
    KEY_COLUMNS='referral_id'
);

BEGIN TRY
    -- 1. Extract staging node entities from Postgres
    SELECT customer_id, name, city, 'Active' AS status
    INTO #staging_customers
    FROM sql_src.customers
    WHERE signup_date >= '2025-01-01';

    -- 2. Ingest Customers as Nodes (MERGE on customer_id via KEY_COLUMNS)
    INSERT INTO graph.NODE_CUSTOMER (customer_id, name, city, status)
    SELECT customer_id, name, city, status
    FROM #staging_customers;

    -- 3. Extract staging relationships from Postgres
    SELECT referral_id, source_customer_id, target_customer_id, '2025' AS since
    INTO #staging_relationships
    FROM sql_src.customer_referrals;

    -- 4. Use stable endpoint keys instead of Neo4j element IDs
    SELECT 
        source_customer_id AS _from_key,
        target_customer_id AS _to_key,
        referral_id,
        r.since
    INTO #edges_to_write
    FROM #staging_relationships AS r;

    -- 5. Ingest Relationships (MERGE on referral_id via KEY_COLUMNS)
    INSERT INTO graph_edges.EDGE_REFERRAL (_from_key, _to_key, referral_id, since)
    SELECT _from_key, _to_key, referral_id, since
    FROM #edges_to_write;

    -- 6. Query the Graph using native Cypher pass-through (EXECUTE)
    -- Find customers who referred someone who then referred someone else (2-hop referral chain)
    DECLARE @minYear INT = 2025;

    EXECUTE graph INTO #referral_chains WITH (@minYear)
    BEGIN
        MATCH (a:CUSTOMER)-[:REFERRAL]->(b:CUSTOMER)-[:REFERRAL]->(c:CUSTOMER)
        WHERE toInteger(b.since) >= ?1
        RETURN a.name AS initiator, b.name AS intermediary, c.name AS final_recipient
    END;

    -- 7. Export the results to a CSV report
    CREATE CONNECTION csv_report AS CSV('C:/exports/referral_chain_report.csv');
    INSERT INTO csv_report.chains
    SELECT initiator, intermediary, final_recipient FROM #referral_chains;

    PRINT 'Graph ETL complete.';
END TRY
BEGIN CATCH
    PRINT 'Graph ETL failed: ' + ERROR_MESSAGE();
    THROW;
END CATCH
```
