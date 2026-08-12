using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;

namespace LagersystemLVHome.Application.Services;

public interface ISecureConnectionStringProvider
{
    /// <summary>
    /// Replaces the password in the connection string with the decrypted value.
    /// </summary>
    string GetSecureConnectionString(string connectionStringTemplate);

    bool HasSecureSecrets();
}
