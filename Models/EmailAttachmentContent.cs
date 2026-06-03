using System.Security.Cryptography;
using System.Text;

namespace MailArchiver.Models
{
    public class EmailAttachmentContent
    {
        public int Id { get; set; }
        public string ContentHash { get; set; }
        public byte[] Content { get; set; }
        public long Size { get; set; }

        public virtual ICollection<EmailAttachment> Attachments { get; set; } = new List<EmailAttachment>();

        public static string CalculateContentHash(byte[] content)
        {
            using var sha256 = SHA256.Create();
            var hashBytes = sha256.ComputeHash(content ?? Array.Empty<byte>());
            return Convert.ToHexString(hashBytes).ToLowerInvariant();
        }
    }
}
