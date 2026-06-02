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
            using var md5 = MD5.Create();
            var hashBytes = md5.ComputeHash(content ?? Array.Empty<byte>());
            var sb = new StringBuilder(hashBytes.Length * 2);
            foreach (var b in hashBytes)
            {
                sb.Append(b.ToString("x2"));
            }

            return sb.ToString();
        }
    }
}
