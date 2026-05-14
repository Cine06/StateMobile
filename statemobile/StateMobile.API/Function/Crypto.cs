using System.Security.Cryptography;

namespace StateMobile.API.Function
{
    public class Crypto
    {
        public byte[] _salt { get; set; }

        // Explicitly defining the old defaults to maintain backward compatibility 
        // with strings encrypted by the obsolete code.
        private const int Iterations = 1000;

        /// <summary>
        /// Encrypt the given string using AES.  The string can be decrypted using 
        /// DecryptStringAES().  The sharedSecret parameters must match.
        /// </summary>
        /// <param name="plainText">The text to encrypt.</param>
        /// <param name="sharedSecret">A password used to generate a key for encryption.</param>
        public string EncryptStringAES(string plainText, string sharedSecret)
        {
            if (string.IsNullOrEmpty(plainText))
                throw new ArgumentNullException(nameof(plainText));
            if (string.IsNullOrEmpty(sharedSecret))
                throw new ArgumentNullException(nameof(sharedSecret));

            string outStr = null;

            // Updated: Explicitly declare the hash algorithm and iteration count
            using (var key = new Rfc2898DeriveBytes(sharedSecret, _salt, Iterations, HashAlgorithmName.SHA1))
            {
                // Updated: Use Aes.Create() instead of RijndaelManaged
                using (Aes aesAlg = Aes.Create())
                {
                    aesAlg.Key = key.GetBytes(aesAlg.KeySize / 8);

                    // Create an encryptor to perform the stream transform.
                    ICryptoTransform encryptor = aesAlg.CreateEncryptor(aesAlg.Key, aesAlg.IV);

                    // Create the streams used for encryption.
                    using (MemoryStream msEncrypt = new MemoryStream())
                    {
                        // prepend the IV
                        msEncrypt.Write(BitConverter.GetBytes(aesAlg.IV.Length), 0, sizeof(int));
                        msEncrypt.Write(aesAlg.IV, 0, aesAlg.IV.Length);

                        using (CryptoStream csEncrypt = new CryptoStream(msEncrypt, encryptor, CryptoStreamMode.Write))
                        {
                            using (StreamWriter swEncrypt = new StreamWriter(csEncrypt))
                            {
                                //Write all data to the stream.
                                swEncrypt.Write(plainText);
                            }
                        }
                        outStr = Convert.ToBase64String(msEncrypt.ToArray());
                    }
                } // aesAlg is automatically disposed/cleared here
            }

            return outStr;
        }

        /// <summary>
        /// Decrypt the given string.  Assumes the string was encrypted using 
        /// EncryptStringAES(), using an identical sharedSecret.
        /// </summary>
        /// <param name="cipherText">The text to decrypt.</param>
        /// <param name="sharedSecret">A password used to generate a key for decryption.</param>
        public string DecryptStringAES(string cipherText, string sharedSecret)
        {
            if (string.IsNullOrEmpty(cipherText))
                throw new ArgumentNullException(nameof(cipherText));
            if (string.IsNullOrEmpty(sharedSecret))
                throw new ArgumentNullException(nameof(sharedSecret));

            string plaintext = null;

            // Updated: Explicitly declare the hash algorithm and iteration count
            using (var key = new Rfc2898DeriveBytes(sharedSecret, _salt, Iterations, HashAlgorithmName.SHA1))
            {
                byte[] bytes = Convert.FromBase64String(cipherText);
                using (MemoryStream msDecrypt = new MemoryStream(bytes))
                {
                    // Updated: Use Aes.Create() instead of RijndaelManaged
                    using (Aes aesAlg = Aes.Create())
                    {
                        aesAlg.Key = key.GetBytes(aesAlg.KeySize / 8);

                        // Get the initialization vector from the encrypted stream
                        aesAlg.IV = ReadByteArray(msDecrypt);

                        // Create a decryptor to perform the stream transform.
                        ICryptoTransform decryptor = aesAlg.CreateDecryptor(aesAlg.Key, aesAlg.IV);

                        using (CryptoStream csDecrypt = new CryptoStream(msDecrypt, decryptor, CryptoStreamMode.Read))
                        {
                            using (StreamReader srDecrypt = new StreamReader(csDecrypt))
                            {
                                // Read the decrypted bytes from the decrypting stream
                                // and place them in a string.
                                plaintext = srDecrypt.ReadToEnd();
                            }
                        }
                    } // aesAlg is automatically disposed/cleared here
                }
            }

            return plaintext;
        }

        private byte[] ReadByteArray(Stream s)
        {
            byte[] rawLength = new byte[sizeof(int)];
            if (s.Read(rawLength, 0, rawLength.Length) != rawLength.Length)
            {
                throw new SystemException("Stream did not contain properly formatted byte array");
            }

            byte[] buffer = new byte[BitConverter.ToInt32(rawLength, 0)];
            if (s.Read(buffer, 0, buffer.Length) != buffer.Length)
            {
                throw new SystemException("Did not read byte array properly");
            }

            return buffer;
        }
    }
}
