// @ts-nocheck — generated copy; check the canonical source.
/* GENERATED FILE - DO NOT EDIT.
 * Source: src/ETL-SQL.ReportRuntime/Resources/Shared/designer/data-prep-recipes.js
 * Edit the canonical source, then run: node .\scripts\sync-assets.js
 */

/**
 * Copyright 2026 Charles Clemens and ETL-SQL contributors
 * Licensed under the Apache License, Version 2.0.
 *
 * data-prep-recipes.js — split out of designer.js, TODO.md §2.
 * The catalogue of data-preparation recipes offered by the script workbench.
 */

export const DATA_PREP_RECIPES = [
    {
        id: 'rolling_aggregate',
        label: 'Rolling Aggregate (Moving Average)',
        algorithm: 'ROLLING_AGGREGATE',
        description: 'Smooths noisy trends or computes moving averages and cumulative aggregates.',
        targetSuffix: 'rolling',
        template: (target, source) => `TRANSFORM #${target}\nFROM #${source}\nUSING ROLLING_AGGREGATE (\n  VALUE_COL = 'Value',\n  ORDER_COL = 'Date',\n  WINDOW_SIZE = 7,\n  AGGREGATE = 'AVG',\n  ROLLING_COL = 'Value_Rolling'\n);`
    },
    {
        id: 'period_comparison',
        label: 'Period Comparison (MoM / YoY Growth)',
        algorithm: 'PERIOD_COMPARISON',
        description: 'Calculates period-over-period difference and growth percentages on time-series.',
        targetSuffix: 'mom',
        template: (target, source) => `TRANSFORM #${target}\nFROM #${source}\nUSING PERIOD_COMPARISON (\n  DATE_COL = 'MonthStart',\n  VALUE_COL = 'Revenue',\n  PERIOD = 'MONTH',\n  DIFF_COL = 'Revenue_Diff',\n  PCT_COL = 'Revenue_Pct'\n);`
    },
    {
        id: 'share_of_total',
        label: 'Share of Total (%)',
        algorithm: 'SHARE_OF_TOTAL',
        description: 'Computes percentage contribution of numeric values relative to group or total.',
        targetSuffix: 'share',
        template: (target, source) => `TRANSFORM #${target}\nFROM #${source}\nUSING SHARE_OF_TOTAL (\n  VALUE_COL = 'Amount',\n  BY_GROUP = 'Category',\n  SHARE_COL = 'Amount_Share'\n);`
    },
    {
        id: 'top_n_others',
        label: 'Top N + Others Bucket',
        algorithm: 'TOP_N_OTHERS',
        description: 'Ranks top N categories and aggregates remaining low-volume categories into Others.',
        targetSuffix: 'top5',
        template: (target, source) => `TRANSFORM #${target}\nFROM #${source}\nUSING TOP_N_OTHERS (\n  N = 5,\n  VALUE_COL = 'Amount',\n  CATEGORY_COL = 'Category',\n  OTHERS_LABEL = 'Others',\n  AGGREGATE = 'SUM'\n);`
    },
    {
        id: 'fill_dates',
        label: 'Fill Missing Dates',
        algorithm: 'FILL_DATES',
        description: 'Fills missing calendar dates in daily time-series with default/zero values.',
        targetSuffix: 'filled',
        template: (target, source) => `TRANSFORM #${target}\nFROM #${source}\nUSING FILL_DATES (\n  DATE_COL = 'OrderDate',\n  GAPS_FILL = 0\n);`
    },
    {
        id: 'pivot',
        label: 'Pivot Cross-Tabulation',
        algorithm: 'PIVOT',
        description: 'Rotates category rows into columns to construct cross-tabulation matrix summaries.',
        targetSuffix: 'pivot',
        template: (target, source) => `TRANSFORM #${target}\nFROM #${source}\nUSING PIVOT (\n  ROW_FIELDS = 'Region',\n  PIVOT_FIELD = 'Quarter',\n  VALUE_FIELD = 'SalesAmount',\n  AGGREGATE = 'SUM'\n);`
    },
    {
        id: 'interpolate',
        label: 'Interpolate Missing Values',
        algorithm: 'INTERPOLATE',
        description: 'Fills missing numeric null values via linear or forward/backward progression.',
        targetSuffix: 'interpolated',
        template: (target, source) => `TRANSFORM #${target}\nFROM #${source}\nUSING INTERPOLATE (\n  VALUE_COL = 'Reading',\n  ORDER_COL = 'Timestamp',\n  METHOD = 'LINEAR'\n);`
    },
    {
        id: 'normalize',
        label: 'Normalize (Min-Max / Z-Score)',
        algorithm: 'NORMALIZE',
        description: 'Scales numeric columns to standard ranges [0, 1] or standardized Z-scores.',
        targetSuffix: 'normalized',
        template: (target, source) => `TRANSFORM #${target}\nFROM #${source}\nUSING NORMALIZE (\n  VALUE_COL = 'Score',\n  METHOD = 'MIN_MAX'\n);`
    },
    {
        id: 'deduplicate',
        label: 'Deduplicate Rows',
        algorithm: 'DEDUPLICATE',
        description: 'Removes duplicate rows based on key columns with deterministic sorting.',
        targetSuffix: 'deduped',
        template: (target, source) => `TRANSFORM #${target}\nFROM #${source}\nUSING DEDUPLICATE (\n  KEY_COLS = 'Id',\n  ORDER_BY = 'UpdatedUtc DESC',\n  KEEP = 'FIRST'\n);`
    }
];
