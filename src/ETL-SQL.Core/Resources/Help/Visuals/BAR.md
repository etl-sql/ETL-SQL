# BAR
Type: BAR, HBAR
Mappings: X (categories), Y (metrics), COLOR (breakdown series).
Options: STACKED (ON|OFF), LEGEND (ON|OFF), LABEL_POSITION (INSIDE|OUTSIDE|NONE).
Example:
```sql
CREATE VISUAL SalesByRegion AS BAR (
  SOURCE = #data, MAPPINGS (X = Region, Y = Sales)
);
```
