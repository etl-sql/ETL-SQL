# TRANSFORM Reference

[« Back to parent](../README.md)

| Page | Description |
| :--- | :--- |
| [BIN](bin.md) | Buckets numeric values into equal-width, quantile, or threshold intervals (`BUCKETIZE` synonym) emitting `Bin_Start`, `Bin_End`, and `Bin_Label`. |
| [CLEAN](clean.md) | Applies ordered text cleaning and standardization rules across multiple or all string columns. |
| [DEDUPLICATE](deduplicate.md) | Removes duplicate rows based on key columns, with optional sorting to control which row is kept. |
| [FILL_DATES](fill_dates.md) | Fills missing daily rows in a time-series dataset. |
| [IMPUTE](impute.md) | Replaces missing (`NULL`) values across one or more columns using `MEAN`, `MEDIAN`, `MODE`, or `CONSTANT` per group. |
| [INTERPOLATE](interpolate.md) | Fills missing numeric values between known data points in a series. |
| [NORMALIZE](normalize.md) | Scales numeric columns to a standard range or distribution. |
| [OUTLIERS](outliers.md) | Detects and clips, flags, filters, or nulls numeric outliers using `IQR`, `Z_SCORE`, or `PERCENTILE` bounds. |
| [PERIOD_COMPARISON](period_comparison.md) | Calculates period-over-period differences and growth percentages (DoD, MoM, YoY) on sequential time-series data. |
| [PIVOT](pivot.md) | Rotates rows into columns to build cross-tabulation summaries. |
| [ROLLING_AGGREGATE](rolling_aggregate.md) | Smooths noisy data trends (e.g. 7-day moving average) or tracks running/cumulative metrics. |
| [SHARE_OF_TOTAL](share_of_total.md) | Computes the percentage contribution of a row's numeric value relative to the group or grand total. |
| [SPLIT](split.md) | Expands delimited strings or nested JSON arrays into rows or columns (`EXPLODE` synonym) while preserving parent keys. |
| [TOP_N_OTHERS](top_n_others.md) | Reduces high-cardinality values by aggregating low-volume categories into a single row. Useful for keeping pie and bar charts clean. |
| [UNPIVOT](unpivot.md) | Reshapes wide columns into normalized key/value rows (`MELT` synonym). |
