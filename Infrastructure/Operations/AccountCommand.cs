using System.Text;
using TrailQuest.Api.Application;
using TrailQuest.Api.Contracts;

namespace TrailQuest.Api.Infrastructure.Operations;

public sealed record AccountCommand(string Action, string Username, string DisplayName, bool PasswordFromStdin)
{
    public static AccountCommand? Parse(IConfiguration configuration)
    {
        var section = configuration.GetSection("Account");
        string[] allowed = ["Action", "Username", "DisplayName", "PasswordInput"];
        if (section.GetChildren().Any(x => !allowed.Contains(x.Key, StringComparer.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Unsupported account setting. Never pass passwords in arguments or environment variables; use the private console.");
        if (section["Action"] is not { Length: > 0 } action) return null;
        if (action is not ("create-owner" or "recover-owner"))
            throw new InvalidOperationException("Account:Action must be create-owner or recover-owner.");
        var username = section["Username"];
        if (string.IsNullOrWhiteSpace(username)) throw new InvalidOperationException("Account:Username is required. No account name is supplied by the image.");
        var displayName = section["DisplayName"] ?? username;
        var input = section["PasswordInput"] ?? "console";
        if (input is not ("console" or "stdin")) throw new InvalidOperationException("Account:PasswordInput must be console or stdin.");
        return new(action, username, displayName, input == "stdin");
    }

    public async Task<string> ReadPasswordAsync(CancellationToken cancellationToken = default)
    {
        if (PasswordFromStdin) return await ReadSecretLineAsync(Console.In, cancellationToken);
        if (Console.IsInputRedirected)
            throw new InvalidOperationException("Use an interactive private console, or explicitly select Account:PasswordInput=stdin for managed secret input.");
        Console.Write("New password (12–128 characters; hidden): ");
        var password = ReadHidden();
        Console.Write("Repeat password (hidden): ");
        if (password != ReadHidden()) throw new InvalidOperationException("The passwords do not match. Nothing was changed.");
        return password;
    }

    public async Task ExecuteAsync(IServiceProvider services, string password, CancellationToken cancellationToken = default)
    {
        var accounts = services.GetRequiredService<AccountService>();
        if (Action == "create-owner")
            await accounts.CreateAsync(new CreateUserRequest { Username = Username, DisplayName = DisplayName, Password = password }, true, cancellationToken);
        else await accounts.RecoverOwnerAsync(Username, password, cancellationToken);
    }

    public static async Task<string> ReadSecretLineAsync(TextReader reader, CancellationToken cancellationToken = default)
    {
        var value = new StringBuilder(128);
        var character = new char[1];
        while (await reader.ReadAsync(character.AsMemory(), cancellationToken) != 0)
        {
            if (character[0] is '\n' or '\r') break;
            if (value.Length == 128) throw new InvalidOperationException("The password exceeds 128 characters. Nothing was changed.");
            value.Append(character[0]);
        }
        return value.ToString();
    }

    private static string ReadHidden()
    {
        var value = new StringBuilder(128);
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter) { Console.WriteLine(); return value.ToString(); }
            if (key.Key == ConsoleKey.Backspace) { if (value.Length > 0) value.Length--; continue; }
            if (char.IsControl(key.KeyChar)) continue;
            if (value.Length == 128) throw new InvalidOperationException("The password exceeds 128 characters. Nothing was changed.");
            value.Append(key.KeyChar);
        }
    }
}
