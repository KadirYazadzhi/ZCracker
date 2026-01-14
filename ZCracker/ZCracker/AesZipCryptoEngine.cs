using System;
using System.Security.Cryptography;
using System.Text;

namespace ZCracker
{
    public class AesZipCryptoEngine
    {
        private const int Iterations = 1000;

        /// <summary>
        /// Checks if the password is correct by deriving the key and comparing the verification bytes.
        /// This avoids full file decryption, making it much faster.
        /// </summary>
        public static bool CheckPassword(string password, byte[] salt, byte[] expectedVerifier, int keyStrengthBits)
        {
            // Calculate total required length for derived bytes.
            // WinZip AES Format:
            // - Encryption Key (KeyLen)
            // - Authentication Key (KeyLen)
            // - Password Verification Value (2 bytes)
            // Total = 2 * (KeyBits / 8) + 2
            
            int keyLenBytes = keyStrengthBits / 8;
            int totalLen = 2 * keyLenBytes + 2;

            // PBKDF2-HMAC-SHA1
            // Note: Rfc2898DeriveBytes uses HMAC-SHA1 by default in .NET Standard 2.0 / .NET Framework,
            // but explicitly specifying HashAlgorithmName.SHA1 is safer for .NET 8+.
            
            // Optimization: We don't allocate a new Rfc2898 instance every time if possible, 
            // but for safety/cleanliness in this threaded context, we do.
            // Since this is CPU intensive (1000 hashes), allocation overhead is negligible compared to computation.
            
            try 
            {
                byte[] pwdBytes = Encoding.UTF8.GetBytes(password);
                
                // Using the static method is available in newer .NET versions and is slightly more efficient
                byte[] derived = Rfc2898DeriveBytes.Pbkdf2(pwdBytes, salt, Iterations, HashAlgorithmName.SHA1, totalLen);

                // Check the last 2 bytes against the expected verifier
                if (derived[totalLen - 2] == expectedVerifier[0] && 
                    derived[totalLen - 1] == expectedVerifier[1])
                {
                    return true;
                }
            }
            catch
            {
                // Handle potential encoding issues or interrupts
            }

            return false;
        }
    }
}