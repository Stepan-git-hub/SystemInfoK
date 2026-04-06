// AesEncryption.cs
using System;
using System.Security.Cryptography;
using System.Text;

namespace SystemInfoService
{
    public static class AesEncryption
    {
        // ДОЛЖЕН БЫТЬ ТОЧНО ТАКОЙ ЖЕ КЛЮЧ, КАК В PasswordEncoder!
        private static readonly byte[] EncryptionKey = new byte[]
        {
            0x4A, 0x5F, 0x3C, 0x8E, 0xD1, 0x7B, 0x2F, 0x9A,
            0xC4, 0x6E, 0x1D, 0x8F, 0x3B, 0x5A, 0xE9, 0x2C,
            0x7D, 0x4F, 0x1A, 0x6C, 0x8E, 0x3D, 0x5F, 0x9B,
            0x2A, 0x7E, 0x4C, 0x1F, 0x6D, 0x3E, 0x8A, 0x5C
        };

        private static readonly byte[] EncryptionIV = new byte[]
        {
            0x1A, 0x2B, 0x3C, 0x4D, 0x5E, 0x6F, 0x7A, 0x8B,
            0x9C, 0xAD, 0xBE, 0xCF, 0xD0, 0xE1, 0xF2, 0x03
        };

        public static string Decrypt(string encryptedText)
        {
            if (string.IsNullOrEmpty(encryptedText))
                return "";

            try
            {
                using (Aes aes = Aes.Create())
                {
                    aes.Key = EncryptionKey;
                    aes.IV = EncryptionIV;
                    aes.Mode = CipherMode.CBC;
                    aes.Padding = PaddingMode.PKCS7;

                    ICryptoTransform decryptor = aes.CreateDecryptor();
                    byte[] encryptedBytes = Convert.FromBase64String(encryptedText);
                    byte[] decryptedBytes = decryptor.TransformFinalBlock(encryptedBytes, 0, encryptedBytes.Length);

                    return Encoding.UTF8.GetString(decryptedBytes);
                }
            }
            catch
            {
                return encryptedText;
            }
        }
    }
}