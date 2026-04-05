using System;
using ETL_SQL.Common;

class Program {
    static void Main() {
        string plain = "Server=myserver;Database=mydb;User Id=myuser;Password=mypass;";
        string pass = "mysecret";
        string encrypted = CryptoUtils.Encrypt(plain, pass);
        Console.WriteLine(encrypted);
    }
}
