# Quick Start

## 12. Quick Start: Required Steps

To get the Portal running in under 5 minutes:

1. **Standardize Naming**: Ensure you are using the `ETL-SQL-Portal` executable.
2. **Set JWT Secret**: Open `appsettings.json` or set an environment variable `Portal__Jwt__Secret` to a 32-character random string.
3. **Configure Paths**: Verify `ScriptRootPath` points to your `.rptsql` files (defaults to `./Reports`).
4. **Launch**: Run `./ETL-SQL-Portal`.
5. **Admin Login**:
   - URL: `http://localhost:5000`
   - User: `admin`
   - Temp Password: the value of `Portal__FirstRun__AdminPassword` if you configured one; otherwise a
     randomly generated password printed once to the startup log (look for the `Portal.FirstRun` category).
6. **Secure Account**: Change the admin password immediately upon first login.
7. **Publish**: Go to **Admin -> Folders**, click **Publish Report**, and point to a `.rptsql` file.

---

