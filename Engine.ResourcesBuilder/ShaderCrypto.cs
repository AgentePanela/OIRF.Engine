using System.Security.Cryptography;

namespace Engine.ResourcesBuilder;

/// <summary>
/// Encrypts the generated shader cache at rest. The key ships inside this assembly, so this
/// only raises the bar past casually opening a file - not real protection against someone
/// willing to extract it from the built game.
/// </summary>
internal static class ShaderCrypto
{
    private static readonly byte[] Key = Convert.FromHexString("14c4bffa0ab16a06a9092335c7f4f81626224a19c611c44dda26468dcfe375b0");

    public static byte[] Encrypt(byte[] plaintext)
    {
        var nonce = RandomNumberGenerator.GetBytes(AesGcm.NonceByteSizes.MaxSize);
        var tag = new byte[AesGcm.TagByteSizes.MaxSize];
        var ciphertext = new byte[plaintext.Length];

        using var aes = new AesGcm(Key, tag.Length);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);

        var blob = new byte[nonce.Length + tag.Length + ciphertext.Length];
        Buffer.BlockCopy(nonce, 0, blob, 0, nonce.Length);
        Buffer.BlockCopy(tag, 0, blob, nonce.Length, tag.Length);
        Buffer.BlockCopy(ciphertext, 0, blob, nonce.Length + tag.Length, ciphertext.Length);
        return blob;
    }

    public static byte[] Decrypt(byte[] blob)
    {
        var nonceSize = AesGcm.NonceByteSizes.MaxSize;
        var tagSize = AesGcm.TagByteSizes.MaxSize;

        var nonce = blob.AsSpan(0, nonceSize);
        var tag = blob.AsSpan(nonceSize, tagSize);
        var ciphertext = blob.AsSpan(nonceSize + tagSize);

        var plaintext = new byte[ciphertext.Length];
        using var aes = new AesGcm(Key, tagSize);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);
        return plaintext;
    }
}
