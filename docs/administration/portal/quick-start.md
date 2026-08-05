# Quick Start

The shortest path to a working Portal: the steps you cannot skip, in order.

## Quick Start: Required Steps

To get the Portal running in under 5 minutes:

1. **Standardize Naming**: Ensure you are using the `ETL-SQL-Portal` executable.
2. **Set JWT Secret**: Open `appsettings.json` or set an environment variable `Portal__Jwt__Secret` to a 32-character random string.
3. **Configure Paths**: Verify `ScriptRootPath` points to your `.rptsql` files (defaults to `./Reports`).
4. **Launch**: Run `./ETL-SQL-Portal`.
5. **Admin Login**:
   - URL: `http://localhost:5000`
   - User: `admin`
   - Temp Password: the value of `Portal__FirstRun__AdminPassword`. The Portal will not create the
     first-run admin account until this bootstrap secret is configured.
6. **Secure Account**: Change the admin password immediately upon first login.
7. **Publish**: Go to **Admin -> Folders**, click **Publish Report**, and point to a `.rptsql` file.

---
