using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace ContextBridge.Infrastructure.Security;

public sealed class TokenStore(IDataProtectionProvider dataProtectionProvider)
{
    private const string Purpose = "ContextBridge.BearerToken.v1";

    private static readonly string TokenFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "ContextBridge",
        "token.dat");

    private readonly IDataProtector _protector = dataProtectionProvider.CreateProtector(Purpose);

    public async Task<string> GetOrCreateTokenAsync(CancellationToken cancellationToken = default)
    {
        if (File.Exists(TokenFilePath))
        {
            try
            {
                var protectedToken = await File.ReadAllTextAsync(TokenFilePath, cancellationToken);
                return _protector.Unprotect(protectedToken);
            }
            catch (CryptographicException)
            {
                // Key ring changed or corrupted — regenerate
            }
        }

        return await CreateAndPersistTokenAsync(cancellationToken);
    }

    private async Task<string> CreateAndPersistTokenAsync(CancellationToken cancellationToken)
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var protectedToken = _protector.Protect(token);

        Directory.CreateDirectory(Path.GetDirectoryName(TokenFilePath)!);
        await File.WriteAllTextAsync(TokenFilePath, protectedToken, cancellationToken);

        return token;
    }
}
