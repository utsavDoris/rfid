using System;
using System.Security.Cryptography;
using System.Text;

namespace RfidScanner.Core;

public static class AESUtils
{
    private const string ENCRYPTION_KEY = "45698235674125896325412563698745";

    public static string Encrypt(string data)
    {
        var keyBytes = Convert.FromBase64String(ENCRYPTION_KEY);
        using (var aes = Aes.Create())
        {
            aes.Key = keyBytes;
            aes.Mode = CipherMode.ECB;
            aes.Padding = PaddingMode.PKCS7;
            using (var encryptor = aes.CreateEncryptor())
            {
                var inputBytes = Encoding.UTF8.GetBytes(data);
                var encrypted = encryptor.TransformFinalBlock(inputBytes, 0, inputBytes.Length);
                return Convert.ToBase64String(encrypted);
            }
        }
    }

    public static string Decrypt(string encryptedData)
    {
        var keyBytes = Convert.FromBase64String(ENCRYPTION_KEY);
        using (var aes = Aes.Create())
        {
            aes.Key = keyBytes;
            aes.Mode = CipherMode.ECB;
            aes.Padding = PaddingMode.PKCS7;
            using (var decryptor = aes.CreateDecryptor())
            {
                var inputBytes = Convert.FromBase64String(encryptedData);
                var decrypted = decryptor.TransformFinalBlock(inputBytes, 0, inputBytes.Length);
                return Encoding.UTF8.GetString(decrypted);
            }
        }
    }
}
