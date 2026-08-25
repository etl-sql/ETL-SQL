# Configuration Area

> **Page-type: Reference — configuration**
> Owns: every setting in one configuration area with type, default, scope, and security notes.
> Links to (does not restate): administration pages for operational procedures.
> Required sections: Settings table, Details (for non-trivial settings), Example, Security
> Notes, References.

One sentence: which subsystem these settings control and where they are set
(`appsettings.json` section, environment variable, or `SET` command).

## Settings

| Setting | Type | Default | Scope | Description |
| :--- | :--- | :--- | :--- | :--- |
| `Section:Key` | string/int/bool | `default` | server / session / job | What it does. |

## Details

### `Section:Key`

Expanded explanation when a setting needs more than a table row: valid ranges, interactions
with other settings, and when to change it.

## Example

```json
{
  "Section": {
    "Key": "value"
  }
}
```

## Security Notes

Call out settings that widen a trust boundary, expose a network surface, or hold secrets
(prefer `SECRET:`/`ENC:` references over plaintext).

## References

- [Configuration Reference](../reference/README.md)
- [Administration](../administration/platform/README.md)
