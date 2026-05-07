# BAR
Type: BAR, HBAR
Mappings: X (categories), Y (metrics), COLOR (breakdown series).
Options: STACKED (ON|OFF), LEGEND (ON|OFF), LABEL_POSITION (INSIDE|OUTSIDE|NONE).
Note: Use HBAR for horizontal bars.
Example:
```sql
CREATE VISUAL SalesByRegion AS BAR (
  SOURCE = #data, MAPPINGS (X = Region, Y = Sales)
);
```
