using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace SoMan.Services.Security;

public interface IEncryptionService
{
    string Encrypt(string plainText);
    string Decrypt(string cipherText);
}

public class EncryptionService : IEncryptionService
{
    private readonly byte[] _key;
    private const int IvSize = 16;

    public EncryptionService()
    {
        _key = GetOrCreateKey();
    }

    public string Encrypt(string plainText)
    {
        using var aes = Aes.Create();
        aes.Key = _key;
        aes.GenerateIV();

        using var encryptor = aes.CreateEncryptor();
        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var cipherBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

        var result = new byte[IvSize + cipherBytes.Length];
        Buffer.BlockCopy(aes.IV, 0, result, 0, IvSize);
        Buffer.BlockCopy(cipherBytes, 0, result, IvSize, cipherBytes.Length);

        return Convert.ToBase64String(result);
    }

    public string Decrypt(string cipherText)
    {
        var fullCipher = Convert.FromBase64String(cipherText);

        var iv = new byte[IvSize];
        var cipher = new byte[fullCipher.Length - IvSize];
        Buffer.BlockCopy(fullCipher, 0, iv, 0, IvSize);
        Buffer.BlockCopy(fullCipher, IvSize, cipher, 0, cipher.Length);

        using var aes = Aes.Create();
        aes.Key = _key;
        aes.IV = iv;

        using var decryptor = aes.CreateDecryptor();
        var plainBytes = decryptor.TransformFinalBlock(cipher, 0, cipher.Length);
        return Encoding.UTF8.GetString(plainBytes);
    }

    private static byte[] GetOrCreateKey()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var keyDir = Path.Combine(appData, "SoMan");
        Directory.CreateDirectory(keyDir);
        var keyFile = Path.Combine(keyDir, ".key");

        if (File.Exists(keyFile))
        {
            return Convert.FromBase64String(File.ReadAllText(keyFile));
        }

        var key = new byte[32]; // AES-256
        RandomNumberGenerator.Fill(key);
        File.WriteAllText(keyFile, Convert.ToBase64String(key));

        // Restrict file access to current user only
        var fileInfo = new FileInfo(keyFile);
        fileInfo.Attributes |= FileAttributes.Hidden;

        return key;
    }
}
