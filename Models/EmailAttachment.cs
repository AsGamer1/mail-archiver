using System.ComponentModel.DataAnnotations.Schema;

namespace MailArchiver.Models
{
    public class EmailAttachment
    {
        public int Id { get; set; }
        public int ArchivedEmailId { get; set; }
        public string FileName { get; set; }
        public string ContentType { get; set; }
        public string? ContentId { get; set; }
        public int EmailAttachmentContentId { get; set; }
        public virtual EmailAttachmentContent AttachmentContent { get; set; }
        public long Size { get; set; }

        [NotMapped]
        public byte[] Content
        {
            get => AttachmentContent?.Content ?? Array.Empty<byte>();
            set
            {
                if (AttachmentContent == null)
                {
                    AttachmentContent = new EmailAttachmentContent
                    {
                        Content = value,
                        Size = value?.LongLength ?? 0,
                        ContentHash = EmailAttachmentContent.CalculateContentHash(value)
                    };
                }
                else
                {
                    AttachmentContent.Content = value;
                    AttachmentContent.Size = value?.LongLength ?? 0;
                    AttachmentContent.ContentHash = EmailAttachmentContent.CalculateContentHash(value);
                }
            }
        }

        public virtual ArchivedEmail ArchivedEmail { get; set; }
    }
}
