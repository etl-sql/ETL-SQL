-- TPC-H Schema
CREATE TABLE region (
    r_regionkey  INT PRIMARY KEY,
    r_name       STRING,
    r_comment    STRING
);

CREATE TABLE nation (
    n_nationkey  INT PRIMARY KEY,
    n_name       STRING,
    n_regionkey  INT,
    n_comment    STRING
);

CREATE TABLE part (
    p_partkey     INT PRIMARY KEY,
    p_name        STRING,
    p_mfgr        STRING,
    p_brand       STRING,
    p_type        STRING,
    p_size        INT,
    p_container   STRING,
    p_retailprice DECIMAL(15,2),
    p_comment     STRING
);

CREATE TABLE supplier (
    s_suppkey     INT PRIMARY KEY,
    s_name        STRING,
    s_address     STRING,
    s_nationkey   INT,
    s_phone       STRING,
    s_acctbal     DECIMAL(15,2),
    s_comment     STRING
);

CREATE TABLE partsupp (
    ps_partkey     INT,
    ps_suppkey     INT,
    ps_availqty    INT,
    ps_supplycost  DECIMAL(15,2),
    ps_comment     STRING,
    PRIMARY KEY (ps_partkey, ps_suppkey)
);

CREATE TABLE customer (
    c_custkey     INT PRIMARY KEY,
    c_name        STRING,
    c_address     STRING,
    c_nationkey   INT,
    c_phone       STRING,
    c_acctbal     DECIMAL(15,2),
    c_mktsegment  STRING,
    c_comment     STRING
);

CREATE TABLE orders (
    o_orderkey       INT PRIMARY KEY,
    o_custkey        INT,
    o_orderstatus    STRING,
    o_totalprice     DECIMAL(15,2),
    o_orderdate      DATE,
    o_orderpriority  STRING,
    o_clerk          STRING,
    o_shippriority   INT,
    o_comment        STRING
);

CREATE TABLE lineitem (
    l_orderkey    INT,
    l_partkey     INT,
    l_suppkey     INT,
    l_linenumber  INT,
    l_quantity    DECIMAL(15,2),
    l_extendedprice  DECIMAL(15,2),
    l_discount    DECIMAL(15,2),
    l_tax         DECIMAL(15,2),
    l_returnflag  STRING,
    l_linestatus  STRING,
    l_shipdate    DATE,
    l_commitdate  DATE,
    l_receiptdate DATE,
    l_shipinstruct STRING,
    l_shipmode     STRING,
    l_comment      STRING,
    PRIMARY KEY (l_orderkey, l_linenumber)
);
