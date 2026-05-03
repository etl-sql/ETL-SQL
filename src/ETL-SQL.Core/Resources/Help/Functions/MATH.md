Math and numeric functions.

Rounding:
  ABS(n)                     — absolute value
  ROUND(n, d)                — round to d decimal places
  TRUNCATE(n, d) / TRUNC(n, d) — truncate (no rounding)
  CEILING(n) / CEIL(n)       — round up to nearest integer
  FLOOR(n)                   — round down to nearest integer
  SIGN(n)                    — -1, 0, or 1

Power & roots:
  POWER(base, exp) / POW(base, exp)
  SQRT(n)
  EXP(n)                     — e raised to power n

Logarithms:
  LOG(n)                     — natural log (base e)
  LOG(n, base)               — log to specified base
  LOG10(n)                   — base-10 log

Trigonometry (all in radians):
  SIN(n), COS(n), TAN(n)
  ASIN(n), ACOS(n), ATAN(n)
  ATAN2(y, x)                — angle from x-axis to point (x, y)
  DEGREES(n)                 — radians → degrees
  RADIANS(n)                 — degrees → radians
  PI()

Modulo & integer:
  MOD(n, m) / n % m          — remainder
  QUOTIENT(n, m) / n / m     — integer division (floor)

Random (non-cryptographic):
  RANDOM() / RAND()          — float in [0.0, 1.0)
  RANDOM_INT(min, max)       — inclusive integer
  RANDOM_DECIMAL(min, max)   — float in [min, max]

Example:
```sql
SELECT ROUND(price * 1.15, 2) AS with_tax,
       ABS(target - actual)   AS variance,
       POWER(growth, years)   AS future_value
FROM #forecast;
```
