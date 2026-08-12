using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using LagersystemLVHome.Data;

namespace LagersystemLVHome.Application.Services;

public interface IEncryptionService
{
    string Encrypt(string plainText);
    string Decrypt(string cipherText);
    string HashPassword(string password);
    bool VerifyPassword(string password, string hashedPassword);
    string GenerateRandomKey(int length = 32);
    Task<(string Key, string IV)> GetOrCreateEncryptionKeysAsync(CancellationToken cancellationToken = default);
    Task<bool> HasKeysConfiguredAsync(CancellationToken cancellationToken = default);
}
