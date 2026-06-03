using MailArchiver.Data;
using MailArchiver.Models;
using MailArchiver.Services.Providers.Eml;
using MailArchiver.Utilities;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Microsoft.Extensions.Options;
using MimeKit;
using System.Text;

namespace MailArchiver.Services.Shared
{
    public class MailImporter
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<MailImporter> _logger;
        private readonly EmlAttachmentCollector _attachmentCollector;

        public MailImporter(IServiceProvider serviceProvider, ILogger<MailImporter> logger,
            EmlAttachmentCollector attachmentCollector)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            _attachmentCollector = attachmentCollector;
        }

        public async Task<ImportResult> ImportEmailToDatabase(MimeMessage message, MailAccount account, string jobId, string targetFolder)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<MailArchiverDbContext>();

                var messageId = GenerateMessageId(message, jobId);

                var checkFrom = string.Join(",", message.From.Mailboxes.Select(m => m.Address));
                var checkTo = string.Join(",", message.To.Mailboxes.Select(m => m.Address));
                var checkSubject = message.Subject ?? "(No Subject)";

                var existing = await context.ArchivedEmails
                    .Where(e => e.MailAccountId == account.Id)
                    .Where(e =>
                        e.MessageId == messageId ||
                        (e.From == checkFrom && e.To == checkTo && e.Subject == checkSubject &&
                         Math.Abs((e.SentDate - message.Date.DateTime).TotalSeconds) < 2))
                    .FirstOrDefaultAsync();

                if (existing != null)
                {
                    _logger.LogInformation(
                        "Job {JobId}: Skipping duplicate email. Subject: '{Subject}', From: '{From}', MessageId: '{MessageId}'",
                        jobId, checkSubject, checkFrom, messageId);
                    return ImportResult.CreateAlreadyExists();
                }

                var rawTextBody = message.TextBody;
                var rawHtmlBody = message.HtmlBody;
                var hasNullBytesInText = !string.IsNullOrEmpty(rawTextBody) && rawTextBody.Contains('\0');
                var hasNullBytesInHtml = !string.IsNullOrEmpty(rawHtmlBody) && rawHtmlBody.Contains('\0');
                var originalTextBody = !string.IsNullOrEmpty(rawTextBody) ? MailContentHelper.CleanText(rawTextBody) : null;
                var originalHtmlBody = !string.IsNullOrEmpty(rawHtmlBody) ? MailContentHelper.CleanText(rawHtmlBody) : null;

                var body = string.Empty;
                if (!string.IsNullOrEmpty(message.TextBody))
                {
                    var cleaned = MailContentHelper.CleanText(message.TextBody);
                    body = Encoding.UTF8.GetByteCount(cleaned) > 500_000
                        ? MailContentHelper.TruncateTextForStorage(cleaned, 500_000) : cleaned;
                }
                else if (!string.IsNullOrEmpty(message.HtmlBody))
                {
                    var cleaned = MailContentHelper.CleanText(message.HtmlBody);
                    if (Encoding.UTF8.GetByteCount(cleaned) > 500_000)
                    {
                        originalTextBody = message.HtmlBody;
                        body = MailContentHelper.TruncateTextForStorage(cleaned, 500_000);
                    }
                    else body = cleaned;
                }

                var htmlBody = string.Empty;
                if (!string.IsNullOrEmpty(message.HtmlBody))
                {
                    var cleaned = MailContentHelper.CleanText(message.HtmlBody);
                    htmlBody = Encoding.UTF8.GetByteCount(cleaned) > 1_000_000
                        ? MailContentHelper.CleanHtmlForStorage(cleaned) : cleaned;
                }

                var allAttachments = new List<MimePart>();
                _attachmentCollector.CollectAllAttachments(message.Body, allAttachments);

                var dateTimeHelper = scope.ServiceProvider.GetRequiredService<DateTimeHelper>();
                var convertedSentDate = dateTimeHelper.ConvertToDisplayTimeZone(message.Date);

                var subject = MailContentHelper.TruncateFieldForTsvector(
                    MailContentHelper.CleanText(message.Subject ?? "(No Subject)"), 50_000);
                var from = MailContentHelper.TruncateFieldForTsvector(
                    MailContentHelper.CleanText(string.Join(", ", message.From.Mailboxes.Select(m => m.Address))), 10_000);
                var to = MailContentHelper.TruncateFieldForTsvector(
                    MailContentHelper.CleanText(string.Join(", ", message.To.Mailboxes.Select(m => m.Address))), 50_000);
                var cc = MailContentHelper.TruncateFieldForTsvector(
                    MailContentHelper.CleanText(string.Join(", ", message.Cc?.Mailboxes.Select(m => m.Address) ?? Enumerable.Empty<string>())), 50_000);
                var bcc = MailContentHelper.TruncateFieldForTsvector(
                    MailContentHelper.CleanText(string.Join(", ", message.Bcc?.Mailboxes.Select(m => m.Address) ?? Enumerable.Empty<string>())), 50_000);

                var totalTsvectorSize = Encoding.UTF8.GetByteCount(subject + body + from + to + cc + bcc);
                if (totalTsvectorSize > 900_000)
                {
                    var otherFieldsSize = totalTsvectorSize - Encoding.UTF8.GetByteCount(body);
                    var maxBodySize = 900_000 - otherFieldsSize - 10_000;
                    if (maxBodySize > 0 && Encoding.UTF8.GetByteCount(body) > maxBodySize)
                        body = MailContentHelper.TruncateTextForStorage(body, maxBodySize);
                    else if (maxBodySize <= 0)
                        body = "[Body too large - saved as attachment]";
                }

                var rawHeaders = ExtractRawHeaders(message);
                if (!string.IsNullOrEmpty(rawHeaders))
                    rawHeaders = MailContentHelper.CleanText(rawHeaders);

                var archivedEmail = new ArchivedEmail
                {
                    MailAccountId = account.Id, MessageId = messageId,
                    Subject = subject, From = from, To = to, Cc = cc, Bcc = bcc,
                    SentDate = convertedSentDate, ReceivedDate = DateTime.UtcNow,
                    IsOutgoing = DetermineIfOutgoing(message, account, targetFolder),
                    HasAttachments = allAttachments.Any(), Body = body, HtmlBody = htmlBody,
                    BodyUntruncatedText = null, BodyUntruncatedHtml = null,
                    OriginalBodyText = (hasNullBytesInText || (!string.IsNullOrEmpty(originalTextBody) && originalTextBody != body))
                        ? Encoding.UTF8.GetBytes(hasNullBytesInText ? rawTextBody! : originalTextBody!) : null,
                    OriginalBodyHtml = (hasNullBytesInHtml || (!string.IsNullOrEmpty(originalHtmlBody) && originalHtmlBody != htmlBody))
                        ? Encoding.UTF8.GetBytes(hasNullBytesInHtml ? rawHtmlBody! : originalHtmlBody!) : null,
                    FolderName = targetFolder, RawHeaders = rawHeaders,
                    Attachments = new List<EmailAttachment>()
                };

                var attachmentContentCache = new Dictionary<string, EmailAttachmentContent>(StringComparer.OrdinalIgnoreCase);

                await using var transaction = await context.Database.BeginTransactionAsync();
                try
                {
                    var attachmentInfoList = new List<(string Hash, byte[] Content, string FileName, string ContentType, string? ContentId, long Size)>();
                    if (allAttachments.Any())
                    {
                        foreach (var attachment in allAttachments)
                        {
                            try
                            {
                                using var ms = new MemoryStream();
                                await attachment.Content.DecodeToAsync(ms);
                                var fileName = GetAttachmentFileName(attachment);
                                var contentBytes = ms.ToArray();
                                var contentHash = EmailAttachmentContent.CalculateContentHash(contentBytes);
                                attachmentInfoList.Add((contentHash, contentBytes, fileName, MailContentHelper.CleanText(attachment.ContentType?.MimeType ?? "application/octet-stream"),
                                    !string.IsNullOrEmpty(attachment.ContentId) ? MailContentHelper.CleanText(attachment.ContentId) : null, ms.Length));
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Job {JobId}: Failed to process attachment", jobId);
                            }
                        }
                    }

                    if (attachmentInfoList.Any())
                    {
                        var hashKeys = attachmentInfoList.Select(x => x.Hash).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                        var existingContents = await context.EmailAttachmentContents
                            .Where(c => hashKeys.Contains(c.ContentHash))
                            .ToDictionaryAsync(c => c.ContentHash, StringComparer.OrdinalIgnoreCase);

                        foreach (var attachmentInfo in attachmentInfoList)
                        {
                            if (!attachmentContentCache.TryGetValue(attachmentInfo.Hash, out var existingContent))
                            {
                                if (!existingContents.TryGetValue(attachmentInfo.Hash, out existingContent))
                                {
                                    existingContent = new EmailAttachmentContent
                                    {
                                        ContentHash = attachmentInfo.Hash,
                                        Content = attachmentInfo.Content,
                                        Size = attachmentInfo.Size
                                    };
                                    context.EmailAttachmentContents.Add(existingContent);
                                }
                                attachmentContentCache[attachmentInfo.Hash] = existingContent;
                            }

                            archivedEmail.Attachments.Add(new EmailAttachment
                            {
                                FileName = MailContentHelper.CleanText(attachmentInfo.FileName),
                                ContentType = attachmentInfo.ContentType,
                                ContentId = attachmentInfo.ContentId,
                                AttachmentContent = existingContent,
                                Size = attachmentInfo.Size
                            });
                        }
                    }

                    if (attachmentContentCache.Values.Any(c => c.Id == 0))
                    {
                        try
                        {
                            await context.SaveChangesAsync();
                        }
                        catch (Exception ex)
                        {
                            var current = ex;
                            var isUniqueViolation = false;
                            while (current != null)
                            {
                                if (current is PostgresException postgresEx && postgresEx.SqlState == "23505")
                                {
                                    isUniqueViolation = true;
                                    break;
                                }
                                current = current.InnerException;
                            }

                            if (!isUniqueViolation)
                                throw;

                            _logger.LogDebug("Concurrent insert detected while saving EmailAttachmentContent values, reloading existing hashes");

                            await transaction.RollbackAsync();
                            await transaction.DisposeAsync();
                            transaction = await context.Database.BeginTransactionAsync();

                            foreach (var hash in attachmentContentCache.Keys.ToList())
                            {
                                var content = attachmentContentCache[hash];
                                if (content.Id != 0)
                                    continue;

                                var existingContent = await context.EmailAttachmentContents
                                    .AsNoTracking()
                                    .FirstOrDefaultAsync(c => c.ContentHash == hash);

                                if (existingContent == null)
                                    continue;

                                foreach (var attachment in archivedEmail.Attachments.Where(a => a.AttachmentContent?.ContentHash == hash))
                                {
                                    attachment.AttachmentContent = existingContent;
                                    attachment.EmailAttachmentContentId = existingContent.Id;
                                }

                                var localEntries = context.ChangeTracker.Entries<EmailAttachmentContent>()
                                    .Where(e => e.Entity.ContentHash == hash && e.State == EntityState.Added)
                                    .ToList();
                                foreach (var localEntry in localEntries)
                                    localEntry.State = EntityState.Detached;

                                attachmentContentCache[hash] = existingContent;
                            }

                            await context.SaveChangesAsync();
                        }
                    }

                    context.ArchivedEmails.Add(archivedEmail);
                    await context.SaveChangesAsync();
                    await transaction.CommitAsync();
                    return ImportResult.CreateSuccess();
                }
                catch (Exception)
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Job {JobId}: Failed to import email", jobId);
                return ImportResult.CreateFailed(ex.Message);
            }
        }

        private string GenerateMessageId(MimeMessage message, string jobId)
        {
            var messageId = message.MessageId;
            if (!string.IsNullOrEmpty(messageId)) return messageId;

            var uniqueString = $"{string.Join(",", message.From.Mailboxes.Select(m => m.Address))}|{string.Join(",", message.To.Mailboxes.Select(m => m.Address))}|{message.Subject ?? ""}|{message.Date.Ticks}";
            using var sha256 = System.Security.Cryptography.SHA256.Create();
            var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(uniqueString));
            var hashString = Convert.ToBase64String(hashBytes).Replace("+", "-").Replace("/", "_").Substring(0, 16);
            return $"generated-{hashString}@mail-archiver.local";
        }

        private static string GetAttachmentFileName(MimePart attachment)
        {
            if (!string.IsNullOrEmpty(attachment.FileName)) return attachment.FileName;
            var ext = EmlAttachmentCollector.GetFileExtensionFromContentType(attachment.ContentType?.MimeType);
            return !string.IsNullOrEmpty(attachment.ContentId)
                ? $"inline_{attachment.ContentId.Trim('<', '>')}{ext}"
                : $"attachment_{Guid.NewGuid().ToString("N").Substring(0, 8)}{ext}";
        }

        public static string? ExtractRawHeaders(MimeMessage message)
        {
            try
            {
                if (message.Headers == null || !message.Headers.Any()) return null;
                var sb = new StringBuilder();
                foreach (var h in message.Headers) sb.AppendLine($"{h.Field}: {h.Value}");
                var raw = sb.ToString();
                return raw.Length > 100_000 ? raw.Substring(0, 100_000) + "\r\n[...truncated...]" : raw;
            }
            catch { return null; }
        }
                                                    var localEntries = context.ChangeTracker.Entries<EmailAttachmentContent>()
                                                        .Where(e => e.Entity.ContentHash == hash && e.State == EntityState.Added)
                                                        .ToList();
                                                    foreach (var localEntry in localEntries)
                                                        localEntry.State = EntityState.Detached;
                fromAddr.Equals(account.EmailAddress, StringComparison.OrdinalIgnoreCase);
            bool isOutgoingFolder = IsOutgoingFolderByName(folderName);
            bool isDrafts = IsDraftsFolder(folderName);
            return (isOutgoingEmail || isOutgoingFolder) && !isDrafts;
        }

        public static bool IsOutgoingFolderByName(string folderName)
        {
            var names = new[] { "sent", "gesendet", "enviado", "inviato", "verzonden", "envoye",
                "wyslane", "skickat", "trimise", "elkuldott", "odeslane", "poslano" };
            var lower = folderName?.ToLowerInvariant() ?? "";
            return names.Any(n => lower.Contains(n));
        }

        public static bool IsDraftsFolder(string folderName)
        {
            var names = new[] { "drafts", "entwurfe", "brouillons", "bozze", "draft" };
            var lower = folderName?.ToLowerInvariant() ?? "";
            return names.Any(n => lower.Contains(n));
        }
    }

    public class ImportResult
    {
        public bool Success { get; set; }
        public bool AlreadyExists { get; set; }
        public string? Error { get; set; }

        public static ImportResult CreateSuccess() => new ImportResult { Success = true };
        public static ImportResult CreateAlreadyExists() => new ImportResult { AlreadyExists = true };
        public static ImportResult CreateFailed(string error) => new ImportResult { Error = error };
    }
}