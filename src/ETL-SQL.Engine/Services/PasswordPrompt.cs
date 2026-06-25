using System;
using System.Text;
using ETL_SQL.Core.Common.Exceptions;

namespace ETL_SQL.Engine.Services;

public interface IPasswordPromptProvider
{
    string ReadPassword(string prompt);
}

public sealed class ConsolePasswordPromptProvider : IPasswordPromptProvider
{
    public static ConsolePasswordPromptProvider Instance { get; } = new();

    private ConsolePasswordPromptProvider()
    {
    }

    public string ReadPassword(string prompt)
    {
        return PasswordPrompt.ReadPassword(prompt);
    }
}

public static class PasswordPrompt
{
    public static string ReadPassword(string prompt)
    {
        if (Console.IsInputRedirected)
            throw new ExecutionException($"{prompt} requires an interactive console.");

        Console.Write(prompt);
        var password = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                break;
            }
            if (key.Key == ConsoleKey.Backspace)
            {
                if (password.Length > 0)
                    password.Length--;
                continue;
            }
            if (!char.IsControl(key.KeyChar))
                password.Append(key.KeyChar);
        }

        if (password.Length == 0)
            throw new ExecutionException("Password prompt returned an empty password.");
        return password.ToString();
    }
}
