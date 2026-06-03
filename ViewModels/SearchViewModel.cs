using Microsoft.AspNetCore.Mvc.Rendering;

namespace MailArchiver.Models.ViewModels
{
    public class SearchViewModel
    {
        public string SearchTerm { get; set; } = string.Empty;
        public DateTime? FromDate { get; set; }
        public DateTime? ToDate { get; set; }
        public int? SelectedAccountId { get; set; }
        public string SelectedFolder { get; set; } = string.Empty;
        public bool? IsOutgoing { get; set; }

        // Attachment filters
        public bool? HasAttachment { get; set; }
        public long? MinAttachmentSize { get; set; }
        public long? MaxAttachmentSize { get; set; }
        public string FileNameWildcard { get; set; } = string.Empty;
        public int? MinAttachments { get; set; }
        public int? MaxAttachments { get; set; }

        public int PageNumber { get; set; } = 1;
        public int PageSize { get; set; } = 20;
        public string UserTimezone { get; set; } = "UTC";
        
        // Available page sizes
        public List<SelectListItem> PageSizeOptions => new List<SelectListItem>
        {
            new SelectListItem { Text = "20", Value = "20", Selected = PageSize == 20 },
            new SelectListItem { Text = "50", Value = "50", Selected = PageSize == 50 },
            new SelectListItem { Text = "75", Value = "75", Selected = PageSize == 75 },
            new SelectListItem { Text = "100", Value = "100", Selected = PageSize == 100 },
            new SelectListItem { Text = "150", Value = "150", Selected = PageSize == 150 }
        };
        
        // Sorting properties
        public string SortBy { get; set; } = "SentDate";
        public string SortOrder { get; set; } = "desc";

        // Dropdown-Optionen
        public List<SelectListItem> AccountOptions { get; set; } = new List<SelectListItem>();
        public List<SelectListItem> FolderOptions { get; set; } = new List<SelectListItem>();
        public List<SelectListItem> DirectionOptions { get; set; } = new List<SelectListItem>();

        // Folder tree for hierarchical display
        public List<FolderTreeNode> FolderTree { get; set; } = new List<FolderTreeNode>();

        public SearchViewModel() { }

        // Methode zur Konvertierung von UTC zu lokaler Zeit
        public DateTime ConvertToLocalTime(DateTime utcTime)
        {
            try
            {
                if (string.IsNullOrEmpty(UserTimezone) || UserTimezone == "UTC")
                    return utcTime;

                var tz = TimeZoneInfo.FindSystemTimeZoneById(UserTimezone);
                return TimeZoneInfo.ConvertTimeFromUtc(utcTime, tz);
            }
            catch
            {
                return utcTime; // Fallback zu UTC
            }
        }

        // Suchergebnisse
        public List<ArchivedEmail> SearchResults { get; set; } = new List<ArchivedEmail>();
        public int TotalResults { get; set; }
        public int TotalPages => (int)Math.Ceiling(TotalResults / (double)PageSize);

        // Für die Batch-Operationen
        public List<int> SelectedEmailIds { get; set; } = new List<int>();
        public bool ShowSelectionControls { get; set; } = false;
    }
}
